namespace Pixelbadger.Toolkit.Services;

/// <summary>Per-user store of saved MCP profiles, kept in the same database as the config store.</summary>
public interface IMcpProfileStore
{
    string DatabasePath { get; }

    /// <summary>Saved profiles ordered by name (ordinal). Empty when the database or table does not exist; never creates either.</summary>
    IReadOnlyList<McpProfile> GetAll();

    /// <summary>The saved profile or null. Never creates the database or table. Does NOT fall back to the built-in.</summary>
    McpProfile? Get(string name);

    /// <summary>Inserts or replaces (silent overwrite). Creates the directory, database and table as needed.</summary>
    void Save(McpProfile profile);

    /// <summary>Returns false if no row existed. Never creates the database or table.</summary>
    bool Delete(string name);
}
