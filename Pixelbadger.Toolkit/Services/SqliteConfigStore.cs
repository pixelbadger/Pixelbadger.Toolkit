using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Pixelbadger.Toolkit.Services;

public sealed class SqliteConfigStore : IConfigStore
{
    private static readonly Regex TopicPattern = new("^[a-z0-9-]+$", RegexOptions.Compiled);

    public string DatabasePath { get; }

    public SqliteConfigStore(string? databasePath = null)
    {
        DatabasePath = databasePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify),
            "pbtk", "config.db");
    }

    public ITopicConfig ForTopic(string topic)
    {
        if (topic is null || !TopicPattern.IsMatch(topic) || topic.EndsWith('\n'))
            throw new ArgumentException($"Invalid topic name '{topic}'. Topic names must match ^[a-z0-9-]+$.", nameof(topic));
        return new TopicConfig(this, topic);
    }

    private string ConnectionString(SqliteOpenMode mode) => new SqliteConnectionStringBuilder
    {
        DataSource = DatabasePath,
        Mode = mode,
        Pooling = false
    }.ToString();

    private sealed class TopicConfig : ITopicConfig
    {
        private readonly SqliteConfigStore _store;
        private readonly string _table;

        public string Topic { get; }

        public TopicConfig(SqliteConfigStore store, string topic)
        {
            _store = store;
            Topic = topic;
            _table = "\"" + topic + "\"";
        }

        private bool TableExists(SqliteConnection c) =>
            c.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @name", new { name = Topic }) > 0;

        public IReadOnlyList<ConfigEntry> GetAll()
        {
            if (!File.Exists(_store.DatabasePath)) return [];
            try
            {
                using var c = new SqliteConnection(_store.ConnectionString(SqliteOpenMode.ReadWrite));
                c.Open();
                if (!TableExists(c)) return [];
                var rows = c.Query<(string action, string option, string value)>(
                    $"SELECT action, option, value FROM {_table} ORDER BY action, option").ToList();
                return rows.Select(r => new ConfigEntry(r.action, r.option, TryParse(r.value), r.value)).ToList();
            }
            catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
            {
                throw new ConfigStoreException($"config database {_store.DatabasePath} is unreadable: {ex.Message}", ex);
            }
        }

        public void Set(string actionPath, string option, IReadOnlyList<string> tokens)
        {
            ArgumentNullException.ThrowIfNull(actionPath);
            ArgumentNullException.ThrowIfNull(tokens);
            if (string.IsNullOrEmpty(option) || option.StartsWith('-'))
                throw new ArgumentException("Option must be non-empty and must not start with '-'.", nameof(option));
            try
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(_store.DatabasePath));
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                using var c = new SqliteConnection(_store.ConnectionString(SqliteOpenMode.ReadWriteCreate));
                c.Open();
                c.Execute($"CREATE TABLE IF NOT EXISTS {_table} (action TEXT NOT NULL, option TEXT NOT NULL, value TEXT NOT NULL, PRIMARY KEY (action, option))");
                c.Execute(
                    $"INSERT INTO {_table} (action, option, value) VALUES (@action, @option, @value) " +
                    "ON CONFLICT(action, option) DO UPDATE SET value = excluded.value",
                    new { action = actionPath, option, value = JsonSerializer.Serialize(tokens) });
            }
            catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
            {
                throw WriteFailure(ex);
            }
        }

        public bool Unset(string actionPath, string option)
        {
            ArgumentNullException.ThrowIfNull(actionPath);
            ArgumentNullException.ThrowIfNull(option);
            if (!File.Exists(_store.DatabasePath)) return false;
            try
            {
                using var c = new SqliteConnection(_store.ConnectionString(SqliteOpenMode.ReadWrite));
                c.Open();
                if (!TableExists(c)) return false;
                return c.Execute($"DELETE FROM {_table} WHERE action = @action AND option = @option",
                    new { action = actionPath, option }) > 0;
            }
            catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
            {
                throw WriteFailure(ex);
            }
        }

        private ConfigStoreException WriteFailure(Exception ex) =>
            new($"config database {_store.DatabasePath} could not be written: {ex.Message}", ex);

        private static IReadOnlyList<string>? TryParse(string json)
        {
            try { return JsonSerializer.Deserialize<List<string>>(json); }
            catch (JsonException) { return null; }
        }
    }
}
