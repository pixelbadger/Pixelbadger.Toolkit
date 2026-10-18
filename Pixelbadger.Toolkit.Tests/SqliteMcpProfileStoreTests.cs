using Dapper;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Tests;

public class SqliteMcpProfileStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly string _db;
    private readonly SqliteMcpProfileStore _store;

    public SqliteMcpProfileStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _db = Path.Combine(_dir, "sub", "config.db");
        _store = new SqliteMcpProfileStore(_db);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private void RawInsert(string name, string definition)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_db)!);
        using var c = new SqliteConnection($"Data Source={_db};Mode=ReadWriteCreate;Pooling=False");
        c.Open();
        c.Execute("CREATE TABLE IF NOT EXISTS mcp_profiles (name TEXT PRIMARY KEY, definition TEXT NOT NULL)");
        c.Execute("INSERT INTO mcp_profiles (name, definition) VALUES (@name, @definition)", new { name, definition });
    }

    [Fact]
    public void SaveAndGet_ShouldRoundTrip_WhenHostAndPortSet()
    {
        _store.Save(new McpProfile("p1", ["interpreters", "strings/reverse"], "0.0.0.0", 6000));
        var p = _store.Get("p1");
        p.Should().NotBeNull();
        p!.Name.Should().Be("p1");
        p.Include.Should().Equal("interpreters", "strings/reverse");
        p.Host.Should().Be("0.0.0.0");
        p.Port.Should().Be(6000);
    }

    [Fact]
    public void SaveAndGet_ShouldRoundTrip_WhenHostAndPortNull()
    {
        _store.Save(new McpProfile("p1", ["llm/history/list"], null, null));
        var p = _store.Get("p1")!;
        p.Include.Should().Equal("llm/history/list");
        p.Host.Should().BeNull();
        p.Port.Should().BeNull();
    }

    [Fact]
    public void Save_ShouldReplaceIncludeAndOverrides_WhenNameExists()
    {
        _store.Save(new McpProfile("p1", ["a", "b"], "h", 1));
        _store.Save(new McpProfile("p1", ["c"], null, null));
        var p = _store.Get("p1")!;
        p.Include.Should().Equal("c");
        p.Host.Should().BeNull();
        p.Port.Should().BeNull();
        _store.GetAll().Should().HaveCount(1);
    }

    [Fact]
    public void GetAll_ShouldBeOrderedByName_WhenSeveralSaved()
    {
        _store.Save(new McpProfile("zeta", ["x"], null, null));
        _store.Save(new McpProfile("alpha", ["x"], null, null));
        _store.Save(new McpProfile("mid-1", ["x"], null, null));
        _store.GetAll().Select(p => p.Name).Should().Equal("alpha", "mid-1", "zeta");
    }

    [Fact]
    public void GetAll_ShouldBeEmptyAndNotCreateDatabase_WhenNoDatabase()
    {
        _store.GetAll().Should().BeEmpty();
        File.Exists(_db).Should().BeFalse();
        Directory.Exists(Path.GetDirectoryName(_db)).Should().BeFalse();
    }

    [Fact]
    public void GetAll_ShouldBeEmptyAndNotCreateTable_WhenTableMissing()
    {
        new SqliteConfigStore(_db).ForTopic("strings").Set("", "x", ["1"]);
        _store.GetAll().Should().BeEmpty();
        _store.Get("p1").Should().BeNull();
        _store.Delete("p1").Should().BeFalse();
        TableNames().Should().NotContain("mcp_profiles");
    }

    [Fact]
    public void GetAndDelete_ShouldReturnNullAndFalseWithoutCreatingDatabase_WhenNoDatabase()
    {
        _store.Get("p1").Should().BeNull();
        _store.Delete("p1").Should().BeFalse();
        File.Exists(_db).Should().BeFalse();
    }

    [Fact]
    public void Get_ShouldReturnNull_WhenOnlyOtherProfilesExist()
    {
        _store.Save(new McpProfile("p1", ["a"], null, null));
        _store.Get("default").Should().BeNull();
        _store.Get("p2").Should().BeNull();
    }

    [Fact]
    public void Delete_ShouldRemoveProfile_WhenExists()
    {
        _store.Save(new McpProfile("p1", ["a"], null, null));
        _store.Delete("p1").Should().BeTrue();
        _store.Get("p1").Should().BeNull();
        _store.Delete("p1").Should().BeFalse();
    }

    [Theory]
    [InlineData("Upper")]
    [InlineData("has space")]
    [InlineData("")]
    [InlineData("a\n")]
    [InlineData("under_score")]
    public void Operations_ShouldThrowArgumentException_WhenNameInvalid(string name)
    {
        var save = () => _store.Save(new McpProfile(name, ["a"], null, null));
        var get = () => _store.Get(name);
        var delete = () => _store.Delete(name);
        save.Should().Throw<ArgumentException>();
        get.Should().Throw<ArgumentException>();
        delete.Should().Throw<ArgumentException>();
        File.Exists(_db).Should().BeFalse();
    }

    [Fact]
    public void Save_ShouldThrowArgumentException_WhenIncludeEmptyOrHasEmptyEntry()
    {
        var empty = () => _store.Save(new McpProfile("p1", [], null, null));
        var blank = () => _store.Save(new McpProfile("p1", ["a", ""], null, null));
        var nul = () => _store.Save(new McpProfile("p1", null!, null, null));
        empty.Should().Throw<ArgumentException>();
        blank.Should().Throw<ArgumentException>();
        nul.Should().Throw<ArgumentException>();
        File.Exists(_db).Should().BeFalse();
    }

    [Fact]
    public void Store_ShouldCoexistWithConfigStore_WhenSharingDatabaseFile()
    {
        var config = new SqliteConfigStore(_db);
        config.ForTopic("mcp").Set("serve", "profile", ["x"]);
        _store.Save(new McpProfile("p1", ["a"], "h", 2));

        var entries = config.ForTopic("mcp").GetAll();
        entries.Should().HaveCount(1);
        entries[0].Tokens.Should().Equal("x");
        _store.Get("p1")!.Host.Should().Be("h");
        TableNames().Should().Contain(["mcp", "mcp_profiles"]);
    }

    [Fact]
    public void Get_ShouldThrowCorruptConfigStoreException_WhenDefinitionNotJson()
    {
        RawInsert("bad", "not json");
        var act = () => _store.Get("bad");
        act.Should().Throw<ConfigStoreException>().WithMessage($"mcp profile 'bad' in {_db} is corrupt");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"include\":[]}")]
    [InlineData("null")]
    [InlineData("{\"include\":\"a\"}")]
    public void GetAll_ShouldThrowCorruptConfigStoreException_WhenDefinitionShapeInvalid(string definition)
    {
        RawInsert("bad", definition);
        var act = () => _store.GetAll();
        act.Should().Throw<ConfigStoreException>().WithMessage("mcp profile 'bad' in * is corrupt");
    }

    [Fact]
    public void Reads_ShouldThrowConfigStoreException_WhenFileIsNotSqlite()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_db)!);
        File.WriteAllText(_db, new string('x', 2048));
        var all = () => _store.GetAll();
        var get = () => _store.Get("p1");
        var del = () => _store.Delete("p1");
        all.Should().Throw<ConfigStoreException>().WithMessage($"config database {_db} is unreadable:*");
        get.Should().Throw<ConfigStoreException>().WithMessage($"config database {_db} is unreadable:*");
        del.Should().Throw<ConfigStoreException>().WithMessage($"config database {_db} could not be written:*");
    }

    [Fact]
    public void Save_ShouldThrowConfigStoreException_WhenDatabasePathIsDirectory()
    {
        Directory.CreateDirectory(_db);
        var act = () => _store.Save(new McpProfile("p1", ["a"], null, null));
        act.Should().Throw<ConfigStoreException>().WithMessage($"config database {_db} could not be written:*");
    }

    [Fact]
    public void Constructor_ShouldUseSameDefaultPathAsConfigStore_WhenNoPathGiven()
    {
        new SqliteMcpProfileStore().DatabasePath.Should().Be(new SqliteConfigStore().DatabasePath);
    }

    private List<string> TableNames()
    {
        using var c = new SqliteConnection($"Data Source={_db};Mode=ReadOnly;Pooling=False");
        c.Open();
        return c.Query<string>("SELECT name FROM sqlite_master WHERE type='table'").ToList();
    }
}
