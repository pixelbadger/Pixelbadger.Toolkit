using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Pixelbadger.Toolkit.Services;

public sealed class SqliteMcpProfileStore : IMcpProfileStore
{
    private const string Table = "mcp_profiles";

    public string DatabasePath { get; }

    public SqliteMcpProfileStore(string? databasePath = null)
    {
        DatabasePath = databasePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify),
            "pbtk", "config.db");
    }

    private sealed class Definition
    {
        [JsonPropertyName("include")] public List<string>? Include { get; set; }
        [JsonPropertyName("host")] public string? Host { get; set; }
        [JsonPropertyName("port")] public int? Port { get; set; }
    }

    private string ConnectionString(SqliteOpenMode mode) => new SqliteConnectionStringBuilder
    {
        DataSource = DatabasePath,
        Mode = mode,
        Pooling = false
    }.ToString();

    private static bool TableExists(SqliteConnection c) =>
        c.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @name", new { name = Table }) > 0;

    private static void RequireValidName(string? name)
    {
        if (!McpProfiles.IsValidName(name))
            throw new ArgumentException($"Invalid profile name '{name}'. Profile names must match ^[a-z0-9-]+$.", nameof(name));
    }

    public IReadOnlyList<McpProfile> GetAll()
    {
        if (!File.Exists(DatabasePath)) return [];
        try
        {
            using var c = new SqliteConnection(ConnectionString(SqliteOpenMode.ReadWrite));
            c.Open();
            if (!TableExists(c)) return [];
            var rows = c.Query<(string name, string definition)>($"SELECT name, definition FROM {Table}").ToList();
            return rows.OrderBy(r => r.name, StringComparer.Ordinal).Select(r => Parse(r.name, r.definition)).ToList();
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            throw ReadFailure(ex);
        }
    }

    public McpProfile? Get(string name)
    {
        RequireValidName(name);
        if (!File.Exists(DatabasePath)) return null;
        try
        {
            using var c = new SqliteConnection(ConnectionString(SqliteOpenMode.ReadWrite));
            c.Open();
            if (!TableExists(c)) return null;
            var definition = c.QuerySingleOrDefault<string?>($"SELECT definition FROM {Table} WHERE name = @name", new { name });
            return definition is null ? null : Parse(name, definition);
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            throw ReadFailure(ex);
        }
    }

    public void Save(McpProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        RequireValidName(profile.Name);
        if (profile.Include is null || profile.Include.Count == 0)
            throw new ArgumentException("Include must contain at least one entry.", nameof(profile));
        if (profile.Include.Any(string.IsNullOrEmpty))
            throw new ArgumentException("Include entries must not be null or empty.", nameof(profile));
        var json = JsonSerializer.Serialize(new Definition
        {
            Include = profile.Include.ToList(),
            Host = profile.Host,
            Port = profile.Port
        });
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(DatabasePath));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            using var c = new SqliteConnection(ConnectionString(SqliteOpenMode.ReadWriteCreate));
            c.Open();
            c.Execute($"CREATE TABLE IF NOT EXISTS {Table} (name TEXT PRIMARY KEY, definition TEXT NOT NULL)");
            c.Execute(
                $"INSERT INTO {Table} (name, definition) VALUES (@name, @definition) " +
                "ON CONFLICT(name) DO UPDATE SET definition = excluded.definition",
                new { name = profile.Name, definition = json });
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            throw WriteFailure(ex);
        }
    }

    public bool Delete(string name)
    {
        RequireValidName(name);
        if (!File.Exists(DatabasePath)) return false;
        try
        {
            using var c = new SqliteConnection(ConnectionString(SqliteOpenMode.ReadWrite));
            c.Open();
            if (!TableExists(c)) return false;
            return c.Execute($"DELETE FROM {Table} WHERE name = @name", new { name }) > 0;
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            throw WriteFailure(ex);
        }
    }

    private McpProfile Parse(string name, string json)
    {
        try
        {
            var d = JsonSerializer.Deserialize<Definition>(json);
            if (d?.Include is { Count: > 0 } include && include.All(e => !string.IsNullOrEmpty(e)))
                return new McpProfile(name, include, d.Host, d.Port);
        }
        catch (JsonException) { }
        throw new ConfigStoreException($"mcp profile '{name}' in {DatabasePath} is corrupt");
    }

    private ConfigStoreException ReadFailure(Exception ex) =>
        new($"config database {DatabasePath} is unreadable: {ex.Message}", ex);

    private ConfigStoreException WriteFailure(Exception ex) =>
        new($"config database {DatabasePath} could not be written: {ex.Message}", ex);
}
