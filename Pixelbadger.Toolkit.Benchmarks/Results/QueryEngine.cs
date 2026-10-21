using System.Data;
using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Pixelbadger.Toolkit.Benchmarks.Results;

public static class QueryEngine
{
    private const string Schema = """
        CREATE TABLE devices (slug TEXT PRIMARY KEY, os TEXT, os_description TEXT, arch TEXT, cpu TEXT,
            physical_cores INTEGER, logical_cores INTEGER, memory_gb INTEGER, hypervisor TEXT);
        CREATE TABLE runs (id INTEGER PRIMARY KEY, path TEXT, device_slug TEXT, version TEXT, git_sha TEXT, git_dirty INTEGER,
            captured_at_utc TEXT, tier TEXT, job TEXT, total_seconds REAL);
        CREATE TABLE results (run_id INTEGER, class TEXT, method TEXT, parameters TEXT, success INTEGER,
            mean_ns REAL, median_ns REAL, std_dev_ns REAL, error_ns REAL, min_ns REAL, max_ns REAL, n INTEGER,
            allocated_bytes INTEGER, gen0 REAL, gen1 REAL, gen2 REAL);
        """;

    /// <summary>Builds the in-memory database. The caller owns and disposes the connection.</summary>
    public static SqliteConnection BuildDatabase(IReadOnlyList<StoredRun> runs)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using (var create = connection.CreateCommand())
        {
            create.CommandText = Schema;
            create.ExecuteNonQuery();
        }

        using var tx = connection.BeginTransaction();
        var seenDevices = new HashSet<string>(StringComparer.Ordinal);
        var runId = 0;
        foreach (var stored in runs)
        {
            if (seenDevices.Add(stored.DeviceSlug))
            {
                var f = stored.Device.Fingerprint;
                Exec(connection, tx,
                    "INSERT INTO devices VALUES ($slug,$os,$osd,$arch,$cpu,$pc,$lc,$mem,$hv)",
                    ("$slug", stored.DeviceSlug), ("$os", f.Os), ("$osd", f.OsDescription), ("$arch", f.Arch), ("$cpu", f.Cpu),
                    ("$pc", f.PhysicalCores), ("$lc", f.LogicalCores), ("$mem", f.MemoryGb), ("$hv", f.Hypervisor));
            }

            runId++;
            var run = stored.Run;
            Exec(connection, tx,
                "INSERT INTO runs VALUES ($id,$path,$dev,$ver,$sha,$dirty,$at,$tier,$job,$total)",
                ("$id", runId), ("$path", stored.RelativePath), ("$dev", stored.DeviceSlug), ("$ver", stored.Version),
                ("$sha", run.GitSha), ("$dirty", run.GitDirty is null ? null : run.GitDirty.Value ? 1 : 0),
                ("$at", run.CapturedAtUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)),
                ("$tier", run.Tier), ("$job", run.Job.Name), ("$total", run.TotalSeconds));

            foreach (var r in run.Results)
            {
                Exec(connection, tx,
                    "INSERT INTO results VALUES ($rid,$c,$m,$p,$ok,$mean,$med,$sd,$err,$min,$max,$n,$alloc,$g0,$g1,$g2)",
                    ("$rid", runId), ("$c", r.Class), ("$m", r.Method), ("$p", r.Parameters), ("$ok", r.Success ? 1 : 0),
                    ("$mean", r.MeanNs), ("$med", r.MedianNs), ("$sd", r.StdDevNs), ("$err", r.ErrorNs),
                    ("$min", r.MinNs), ("$max", r.MaxNs), ("$n", r.N), ("$alloc", r.AllocatedBytes),
                    ("$g0", r.Gen0), ("$g1", r.Gen1), ("$g2", r.Gen2));
            }
        }
        tx.Commit();
        return connection;
    }

    /// <summary>Runs the SQL and returns a tab-separated table: header row, then rows. Throws SqliteException on bad SQL.</summary>
    public static string Execute(IReadOnlyList<StoredRun> runs, string sql)
    {
        using var connection = BuildDatabase(runs);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var sb = new StringBuilder();
        if (reader.FieldCount == 0) return "";
        sb.Append(string.Join('\t', Enumerable.Range(0, reader.FieldCount).Select(reader.GetName))).Append('\n');
        while (reader.Read())
        {
            sb.Append(string.Join('\t', Enumerable.Range(0, reader.FieldCount).Select(i => Format(reader, i)))).Append('\n');
        }
        return sb.ToString();
    }

    private static string Format(IDataRecord reader, int i)
    {
        if (reader.IsDBNull(i)) return "";
        var value = reader.GetValue(i);
        return value switch
        {
            double d => d.ToString("R", CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            byte[] b => Convert.ToHexString(b),
            _ => Escape(value.ToString() ?? ""),
        };
    }

    private static string Escape(string s) => s.Replace("\t", " ").Replace("\r", " ").Replace("\n", " ");

    private static void Exec(SqliteConnection c, SqliteTransaction tx, string sql, params (string Name, object? Value)[] ps)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (name, value) in ps) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }
}
