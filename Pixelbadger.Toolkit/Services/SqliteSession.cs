using System.Diagnostics;
using System.Text;
using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace Pixelbadger.Toolkit.Services;

/// <summary>Thrown by <see cref="SqliteSession.Open(string)"/> when the database cannot be opened.</summary>
public class SqliteSessionOpenException : Exception
{
    public SqliteSessionOpenException(string message) : base(message) { }
}

/// <summary>
/// One live connection over an existing SQLite file. Writes stay in an open transaction (holding
/// SQLite's write lock) until <see cref="Save"/> commits or <see cref="Discard"/> rolls back.
/// </summary>
public sealed class SqliteSession : ISqliteSession
{
    public const int MaxRows = 1000;

    private const string RefusedMessage = "Transactions are managed by the TUI: Ctrl+S saves, F8 discards";

    private static readonly HashSet<string> TransactionKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "BEGIN", "COMMIT", "END", "ROLLBACK", "SAVEPOINT", "RELEASE"
    };

    private readonly SqliteConnection _connection;
    private readonly int _timeoutSeconds;
    private bool _dirty;
    private bool _disposed;

    private SqliteSession(SqliteConnection connection, string path, bool readOnly, int timeoutSeconds)
    {
        _connection = connection;
        _timeoutSeconds = timeoutSeconds;
        DatabasePath = path;
        IsReadOnly = readOnly;
    }

    public string DatabasePath { get; }
    public bool IsReadOnly { get; }
    public bool HasUnsavedChanges => _dirty;

    public static SqliteSession Open(string path) => Open(path, 2);

    // The busy timeout is injectable so tests can provoke lock contention without waiting 2 s.
    internal static SqliteSession Open(string path, int timeoutSeconds)
    {
        if (!File.Exists(path))
            throw new SqliteSessionOpenException($"Database file '{path}' not found");

        var fullPath = Path.GetFullPath(path);
        var readOnly = !IsWritable(fullPath);

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWrite,
            Pooling = false,
            DefaultTimeout = timeoutSeconds
        };

        var connection = new SqliteConnection(builder.ConnectionString);
        try
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT count(*) FROM sqlite_master";
            cmd.ExecuteScalar();
        }
        catch (SqliteException ex)
        {
            connection.Dispose();
            throw new SqliteSessionOpenException($"'{path}' is not a valid SQLite database: {ex.Message}");
        }

        return new SqliteSession(connection, fullPath, readOnly, timeoutSeconds);
    }

    // Mode bits are checked explicitly because root bypasses permission checks yet a 0444 file must still count as read-only.
    private static bool IsWritable(string fullPath)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                var mode = File.GetUnixFileMode(fullPath);
                if ((mode & (UnixFileMode.UserWrite | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)) == 0)
                    return false;
            }

            if ((File.GetAttributes(fullPath) & FileAttributes.ReadOnly) != 0)
                return false;

            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    public IReadOnlyList<SchemaObject> GetSchema()
    {
        var entries = new List<(string Name, SchemaObjectKind Kind)>();
        using (var cmd = CreateCommand("SELECT name, type FROM sqlite_master WHERE type IN ('table','view')"))
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                var name = reader.GetString(0);
                var kind = reader.GetString(1) == "view" ? SchemaObjectKind.View : SchemaObjectKind.Table;
                if (kind == SchemaObjectKind.Table && name.StartsWith("sqlite_", StringComparison.OrdinalIgnoreCase))
                    continue;
                entries.Add((name, kind));
            }
        }

        return entries
            .OrderBy(e => e.Kind)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Name, StringComparer.Ordinal)
            .Select(e => new SchemaObject(e.Name, e.Kind, ReadColumns(e.Name)))
            .ToList();
    }

    // Uses table_xinfo (not table_info) so generated columns (hidden 2/3) appear; hidden = 1
    // marks a virtual table's internal column and is dropped. A broken view yields no columns.
    private List<SchemaColumn> ReadColumns(string objectName)
    {
        var columns = new List<SchemaColumn>();
        try
        {
            using var cmd = CreateCommand(
                "SELECT name, type, \"notnull\", pk, hidden FROM pragma_table_xinfo(@name) ORDER BY cid");
            cmd.Parameters.AddWithValue("@name", objectName);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                if (reader.GetInt64(4) == 1)
                    continue;
                var type = reader.IsDBNull(1) ? "" : reader.GetString(1);
                columns.Add(new SchemaColumn(reader.GetString(0), type, reader.GetInt64(3) > 0, reader.GetInt64(2) == 1));
            }
        }
        catch (SqliteException)
        {
            columns.Clear();
        }
        return columns;
    }

    public QueryResult Execute(string sql)
    {
        var stopwatch = Stopwatch.StartNew();

        var plan = Classify(sql);
        if (plan.Statements == 0)
            return Result(QueryOutcome.NothingToExecute, stopwatch, message: "Nothing to execute");
        if (plan.HasTransactionControl)
            return Result(QueryOutcome.Refused, stopwatch, message: RefusedMessage);

        var columns = new List<string>();
        var rows = new List<IReadOnlyList<object?>>();
        var truncated = false;
        var hasResultSet = false;
        var affected = 0;

        try
        {
            if (Handle.AutoCommit)
                Exec("BEGIN");
            Exec("SAVEPOINT pbtk_batch");

            using (var cmd = CreateCommand(sql))
            using (var reader = cmd.ExecuteReader())
            {
                do
                {
                    if (reader.FieldCount > 0)
                    {
                        // Keep only the last result set; fetch MaxRows + 1 to detect truncation.
                        hasResultSet = true;
                        columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
                        rows = new List<IReadOnlyList<object?>>();
                        truncated = false;
                        while (reader.Read())
                        {
                            if (rows.Count == MaxRows)
                            {
                                truncated = true;
                                break;
                            }
                            var row = new object?[reader.FieldCount];
                            for (var i = 0; i < row.Length; i++)
                                row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                            rows.Add(row);
                        }
                    }
                    // Drain remaining rows so every statement runs to completion.
                    while (reader.Read()) { }
                } while (reader.NextResult());

                affected = Math.Max(0, reader.RecordsAffected);
            }

            Exec("RELEASE pbtk_batch");
        }
        catch (SqliteException ex)
        {
            RollBackBatch();
            // An error such as SQLITE_FULL can roll the whole transaction back by itself.
            if (Handle.AutoCommit)
                _dirty = false;
            EndReadTransactionIfClean();
            return Result(QueryOutcome.Error, stopwatch, message: ex.Message);
        }

        _dirty |= plan.HasWrite;
        EndReadTransactionIfClean();

        return hasResultSet
            ? new QueryResult(QueryOutcome.ResultSet, columns, rows, truncated, 0, stopwatch.Elapsed, null)
            : new QueryResult(QueryOutcome.RowsAffected, [], [], false, affected, stopwatch.Elapsed, null);
    }

    public SessionActionResult Save()
    {
        if (!_dirty)
            return new SessionActionResult(true, "No changes to save");
        try
        {
            Exec("COMMIT");
            _dirty = false;
            return new SessionActionResult(true, "Changes saved");
        }
        catch (SqliteException ex)
        {
            return new SessionActionResult(false, ex.Message);
        }
    }

    public SessionActionResult Discard()
    {
        if (!_dirty)
            return new SessionActionResult(true, "No changes to discard");
        try
        {
            Exec("ROLLBACK");
            _dirty = false;
            return new SessionActionResult(true, "Changes discarded");
        }
        catch (SqliteException ex)
        {
            return new SessionActionResult(false, ex.Message);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        if (_dirty)
        {
            try { Exec("ROLLBACK"); } catch (Exception) { /* connection disposal rolls back anyway */ }
            _dirty = false;
        }
        _connection.Dispose();
    }

    private sealed record BatchPlan(int Statements, bool HasTransactionControl, bool HasWrite);

    private sealed class HandleView(SqliteConnection connection)
    {
        public sqlite3 Raw => connection.Handle!;
        public bool AutoCommit => raw.sqlite3_get_autocommit(Raw) != 0;
    }

    private HandleView Handle => new(_connection);

    // Splits the batch with sqlite3_prepare_v2 so statements are classified exactly as SQLite parses them.
    // A statement that cannot be prepared (typically it depends on an object created earlier in the
    // same batch) switches to a lexical scan of the remaining text and counts as writing.
    private BatchPlan Classify(string sql)
    {
        var raw_ = Handle.Raw;
        var statements = 0;
        var control = false;
        var write = false;
        var remaining = sql;

        while (remaining.Length > 0)
        {
            sqlite3_stmt? stmt = null;
            try
            {
                var rc = raw.sqlite3_prepare_v2(raw_, remaining, out stmt, out var tail);
                if (rc != raw.SQLITE_OK)
                {
                    var (count, scanControl) = ScanLexically(remaining);
                    if (count == 0)
                        break;
                    return new BatchPlan(statements + count, control || scanControl, true);
                }

                tail ??= "";
                if (stmt is not null && !stmt.IsInvalid)
                {
                    statements++;
                    var consumed = remaining[..(remaining.Length - tail.Length)];
                    var keyword = FirstKeyword(consumed);
                    if (keyword is not null && TransactionKeywords.Contains(keyword))
                        control = true;
                    if (raw.sqlite3_stmt_readonly(stmt) == 0)
                        write = true;
                }

                if (tail.Length >= remaining.Length)
                    break;
                remaining = tail;
            }
            finally
            {
                if (stmt is not null && !stmt.IsInvalid)
                    raw.sqlite3_finalize(stmt);
            }
        }

        return new BatchPlan(statements, control, write);
    }

    private static (int Count, bool Control) ScanLexically(string text)
    {
        var count = 0;
        var control = false;
        foreach (var piece in SplitStatements(text))
        {
            var keyword = FirstKeyword(piece);
            if (keyword is null)
                continue;
            count++;
            if (TransactionKeywords.Contains(keyword))
                control = true;
        }
        return (count, control);
    }

    // Splits on ';' outside '...', "...", [...], `...` and comments; comments stay out of the pieces.
    private static IEnumerable<string> SplitStatements(string text)
    {
        var current = new StringBuilder();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '-' && i + 1 < text.Length && text[i + 1] == '-')
            {
                while (i < text.Length && text[i] != '\n') i++;
                current.Append(' ');
            }
            else if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                var end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? text.Length : end + 2;
                current.Append(' ');
            }
            else if (c is '\'' or '"' or '`' or '[')
            {
                var close = c == '[' ? ']' : c;
                current.Append(c);
                i++;
                while (i < text.Length)
                {
                    current.Append(text[i]);
                    if (text[i] == close)
                    {
                        // A doubled quote is an escaped quote, not the end.
                        if (close != ']' && i + 1 < text.Length && text[i + 1] == close)
                        {
                            current.Append(text[i + 1]);
                            i += 2;
                            continue;
                        }
                        i++;
                        break;
                    }
                    i++;
                }
            }
            else if (c == ';')
            {
                yield return current.ToString();
                current.Clear();
                i++;
            }
            else
            {
                current.Append(c);
                i++;
            }
        }
        yield return current.ToString();
    }

    // First keyword of a statement after leading whitespace and comments; null when there is none.
    private static string? FirstKeyword(string text)
    {
        var i = 0;
        while (i < text.Length)
        {
            if (char.IsWhiteSpace(text[i]) || text[i] == ';')
                i++;
            else if (text[i] == '-' && i + 1 < text.Length && text[i + 1] == '-')
            {
                while (i < text.Length && text[i] != '\n') i++;
            }
            else if (text[i] == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                var end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? text.Length : end + 2;
            }
            else
                break;
        }

        var start = i;
        while (i < text.Length && (char.IsLetter(text[i]) || text[i] == '_'))
            i++;
        return i > start ? text[start..i] : (start < text.Length ? text[start].ToString() : null);
    }

    private void RollBackBatch()
    {
        try
        {
            Exec("ROLLBACK TO pbtk_batch");
            Exec("RELEASE pbtk_batch");
        }
        catch (SqliteException)
        {
            // Savepoint already gone (transaction auto-rolled back); nothing more to undo.
        }
    }

    private void EndReadTransactionIfClean()
    {
        if (_dirty || Handle.AutoCommit)
            return;
        try { Exec("COMMIT"); } catch (SqliteException) { /* nothing was written; the lock clears on dispose */ }
    }

    private void Exec(string sql)
    {
        using var cmd = CreateCommand(sql);
        cmd.ExecuteNonQuery();
    }

    private SqliteCommand CreateCommand(string sql)
    {
        var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = _timeoutSeconds;
        return cmd;
    }

    private static QueryResult Result(QueryOutcome outcome, Stopwatch stopwatch, string? message) =>
        new(outcome, [], [], false, 0, stopwatch.Elapsed, message);
}
