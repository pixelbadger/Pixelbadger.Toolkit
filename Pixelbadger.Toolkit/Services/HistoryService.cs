using Microsoft.Data.Sqlite;
using Pixelbadger.Toolkit.Models;

namespace Pixelbadger.Toolkit.Services;

public class HistoryService : IHistoryService, IDisposable
{
    private readonly SqliteConnection _connection;

    public HistoryService(string? dbPath = null)
    {
        var path = dbPath ?? GetDefaultDbPath();
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        _connection = new SqliteConnection($"Data Source={path}");
        _connection.Open();
        InitializeSchema();
    }

    private static string GetDefaultDbPath()
    {
        var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userHome, ".pbtk", "history.db");
    }

    private void InitializeSchema()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            PRAGMA foreign_keys = ON;
            CREATE TABLE IF NOT EXISTS sessions (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                command TEXT NOT NULL,
                prompt_tokens INTEGER NOT NULL DEFAULT 0,
                completion_tokens INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS messages (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id INTEGER NOT NULL REFERENCES sessions(id) ON DELETE CASCADE,
                role TEXT NOT NULL,
                content TEXT NOT NULL,
                created_at TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    public async Task<long> CreateSessionAsync(string command)
    {
        var now = DateTime.UtcNow.ToString("O");
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO sessions (command, prompt_tokens, completion_tokens, created_at, updated_at)
            VALUES ($command, 0, 0, $now, $now);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$command", command);
        cmd.Parameters.AddWithValue("$now", now);
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(result);
    }

    public async Task AddMessageAsync(long sessionId, string role, string content)
    {
        var now = DateTime.UtcNow.ToString("O");
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO messages (session_id, role, content, created_at)
            VALUES ($sessionId, $role, $content, $now);
            """;
        cmd.Parameters.AddWithValue("$sessionId", sessionId);
        cmd.Parameters.AddWithValue("$role", role);
        cmd.Parameters.AddWithValue("$content", content);
        cmd.Parameters.AddWithValue("$now", now);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpdateTokenUsageAsync(long sessionId, int additionalPromptTokens, int additionalCompletionTokens)
    {
        var now = DateTime.UtcNow.ToString("O");
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            UPDATE sessions
            SET prompt_tokens = prompt_tokens + $promptTokens,
                completion_tokens = completion_tokens + $completionTokens,
                updated_at = $now
            WHERE id = $sessionId;
            """;
        cmd.Parameters.AddWithValue("$promptTokens", additionalPromptTokens);
        cmd.Parameters.AddWithValue("$completionTokens", additionalCompletionTokens);
        cmd.Parameters.AddWithValue("$now", now);
        cmd.Parameters.AddWithValue("$sessionId", sessionId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<IEnumerable<Session>> ListSessionsAsync()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, command, prompt_tokens, completion_tokens, created_at, updated_at
            FROM sessions
            ORDER BY id DESC;
            """;
        var sessions = new List<Session>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            sessions.Add(new Session
            {
                Id = reader.GetInt64(0),
                Command = reader.GetString(1),
                PromptTokens = reader.GetInt32(2),
                CompletionTokens = reader.GetInt32(3),
                CreatedAt = reader.GetString(4),
                UpdatedAt = reader.GetString(5)
            });
        }
        return sessions;
    }

    public async Task DeleteSessionAsync(long sessionId)
    {
        using var cmdFk = _connection.CreateCommand();
        cmdFk.CommandText = "PRAGMA foreign_keys = ON;";
        await cmdFk.ExecuteNonQueryAsync();

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM sessions WHERE id = $sessionId;";
        cmd.Parameters.AddWithValue("$sessionId", sessionId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<IEnumerable<Message>> GetSessionMessagesAsync(long sessionId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, session_id, role, content, created_at
            FROM messages
            WHERE session_id = $sessionId
            ORDER BY id ASC;
            """;
        cmd.Parameters.AddWithValue("$sessionId", sessionId);
        var messages = new List<Message>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            messages.Add(new Message
            {
                Id = reader.GetInt64(0),
                SessionId = reader.GetInt64(1),
                Role = reader.GetString(2),
                Content = reader.GetString(3),
                CreatedAt = reader.GetString(4)
            });
        }
        return messages;
    }

    public async Task<bool> SessionExistsAsync(long sessionId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM sessions WHERE id = $sessionId;";
        cmd.Parameters.AddWithValue("$sessionId", sessionId);
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(result) > 0;
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
