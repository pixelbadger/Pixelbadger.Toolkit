namespace Pixelbadger.Toolkit.Services;

/// <summary>Per-user store of configured option defaults. Each topic's values live in their own table.</summary>
public interface IConfigStore
{
    /// <summary>Full path of the SQLite database backing the store.</summary>
    string DatabasePath { get; }

    /// <summary>Returns an accessor that can only read and write <paramref name="topic"/>'s table.</summary>
    /// <exception cref="ArgumentException">The topic name does not match ^[a-z0-9-]+$.</exception>
    ITopicConfig ForTopic(string topic);
}

/// <summary>Read/write access to a single topic's configured defaults.</summary>
public interface ITopicConfig
{
    string Topic { get; }

    /// <summary>All rows for this topic. Empty when the database or table does not exist; never creates either.</summary>
    /// <exception cref="ConfigStoreException">The database exists but cannot be read.</exception>
    IReadOnlyList<ConfigEntry> GetAll();

    /// <summary>Inserts or replaces the value for (actionPath, option). Creates the directory, database and table as needed.</summary>
    void Set(string actionPath, string option, IReadOnlyList<string> tokens);

    /// <summary>Deletes the value for (actionPath, option). Returns false if no row existed. Never creates the database or table.</summary>
    bool Unset(string actionPath, string option);
}

/// <summary>
/// A stored default. <paramref name="ActionPath"/> is "" for topic level, otherwise the space-joined path below the topic
/// (e.g. "chat", "history delete"). <paramref name="Option"/> has no leading dashes. <paramref name="Tokens"/> is null when
/// <paramref name="RawValue"/> is not a JSON array of strings.
/// </summary>
public sealed record ConfigEntry(string ActionPath, string Option, IReadOnlyList<string>? Tokens, string RawValue);

public sealed class ConfigStoreException(string message, Exception? inner = null) : Exception(message, inner);
