using FluentAssertions;
using Microsoft.Data.Sqlite;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Tests;

public class SqliteConfigStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly string _db;
    private readonly SqliteConfigStore _store;

    public SqliteConfigStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _db = Path.Combine(_dir, "sub", "config.db");
        _store = new SqliteConfigStore(_db);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private List<string> Tables()
    {
        using var c = new SqliteConnection($"Data Source={_db};Mode=ReadOnly;Pooling=False");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name";
        using var r = cmd.ExecuteReader();
        var l = new List<string>();
        while (r.Read()) l.Add(r.GetString(0));
        return l;
    }

    private void Exec(string sql)
    {
        using var c = new SqliteConnection($"Data Source={_db};Mode=ReadWrite;Pooling=False");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public void SetGetAll_ShouldRoundTrip_WhenMultipleTokens()
    {
        var t = _store.ForTopic("llm");
        t.Set("chat", "model", ["a", "b"]);
        var all = t.GetAll();
        all.Should().ContainSingle();
        all[0].ActionPath.Should().Be("chat");
        all[0].Option.Should().Be("model");
        all[0].Tokens.Should().Equal("a", "b");
        all[0].RawValue.Should().Be("[\"a\",\"b\"]");
    }

    [Fact]
    public void SetGetAll_ShouldRoundTrip_WhenZeroTokens()
    {
        var t = _store.ForTopic("llm");
        t.Set("chat", "flag", []);
        var e = t.GetAll().Single();
        e.Tokens.Should().BeEmpty();
        e.RawValue.Should().Be("[]");
    }

    [Fact]
    public void SetGetAll_ShouldRoundTrip_WhenTokensHaveSpacesQuotesUnicode()
    {
        var tokens = new[] { "hello world", "say \"hi\"", "héllo ✓ 日本", "it's" };
        var t = _store.ForTopic("llm");
        t.Set("chat", "message", tokens);
        t.GetAll().Single().Tokens.Should().Equal(tokens);
    }

    [Fact]
    public void Set_ShouldOverwrite_WhenKeyExists()
    {
        var t = _store.ForTopic("llm");
        t.Set("chat", "model", ["one"]);
        t.Set("chat", "model", ["two"]);
        t.GetAll().Single().Tokens.Should().Equal("two");
    }

    [Fact]
    public void Set_ShouldKeepTopicLevelAndNestedPathsSeparate_WhenSameOption()
    {
        var t = _store.ForTopic("llm");
        t.Set("", "model", ["top"]);
        t.Set("history delete", "model", ["nested"]);
        var all = t.GetAll();
        all.Should().HaveCount(2);
        all.Single(e => e.ActionPath == "").Tokens.Should().Equal("top");
        all.Single(e => e.ActionPath == "history delete").Tokens.Should().Equal("nested");
    }

    [Fact]
    public void Unset_ShouldReturnTrueThenFalse_WhenRowExists()
    {
        var t = _store.ForTopic("llm");
        t.Set("chat", "model", ["x"]);
        t.Unset("chat", "model").Should().BeTrue();
        t.Unset("chat", "model").Should().BeFalse();
        t.GetAll().Should().BeEmpty();
    }

    [Fact]
    public void GetAllAndUnset_ShouldNotCreateAnything_WhenDatabaseMissing()
    {
        var t = _store.ForTopic("llm");
        t.GetAll().Should().BeEmpty();
        t.Unset("chat", "model").Should().BeFalse();
        File.Exists(_db).Should().BeFalse();
        Directory.Exists(_dir).Should().BeFalse();
    }

    [Fact]
    public void GetAllAndUnset_ShouldNotCreateTable_WhenTableAbsent()
    {
        _store.ForTopic("gpt").Set("train", "steps", ["1"]);
        var t = _store.ForTopic("llm");
        t.GetAll().Should().BeEmpty();
        t.Unset("chat", "model").Should().BeFalse();
        Tables().Should().Equal("gpt");
    }

    [Fact]
    public void ForTopic_ShouldIsolateTopics_WhenSameKeys()
    {
        var llm = _store.ForTopic("llm");
        var gpt = _store.ForTopic("gpt");
        llm.Set("chat", "model", ["l"]);
        gpt.GetAll().Should().BeEmpty();
        Tables().Should().Equal("llm");
        gpt.Set("chat", "model", ["g"]);
        Tables().Should().Equal("gpt", "llm");
        llm.GetAll().Single().Tokens.Should().Equal("l");
        gpt.GetAll().Single().Tokens.Should().Equal("g");
        gpt.Unset("chat", "model").Should().BeTrue();
        llm.GetAll().Should().ContainSingle();
    }

    [Fact]
    public void Set_ShouldWork_WhenTopicIsHyphenated()
    {
        var t = _store.ForTopic("my-topic");
        t.Topic.Should().Be("my-topic");
        t.Set("a", "opt", ["v"]);
        t.GetAll().Single().Tokens.Should().Equal("v");
        Tables().Should().Equal("my-topic");
    }

    [Theory]
    [InlineData("LLM")]
    [InlineData("")]
    [InlineData("a b")]
    [InlineData("x\"; DROP TABLE y; --")]
    [InlineData("llm\n")]
    public void ForTopic_ShouldThrowArgumentException_WhenTopicInvalid(string topic)
    {
        var act = () => _store.ForTopic(topic);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("--model")]
    [InlineData("-m")]
    public void Set_ShouldThrowArgumentException_WhenOptionInvalid(string option)
    {
        var act = () => _store.ForTopic("llm").Set("chat", option, ["x"]);
        act.Should().Throw<ArgumentException>();
        File.Exists(_db).Should().BeFalse();
    }

    [Fact]
    public void Set_ShouldThrowArgumentNullException_WhenActionPathNull()
    {
        var act = () => _store.ForTopic("llm").Set(null!, "model", ["x"]);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetAll_ShouldReturnNullTokensAndRawValue_WhenValueNotJsonArrayOfStrings()
    {
        var t = _store.ForTopic("llm");
        t.Set("chat", "seed", ["x"]);
        Exec("INSERT INTO \"llm\" (action, option, value) VALUES ('chat','bad','not json'), ('chat','num','[1,2]'), ('chat','obj','{\"a\":1}')");
        var all = t.GetAll().ToDictionary(e => e.Option);
        all["bad"].Tokens.Should().BeNull();
        all["bad"].RawValue.Should().Be("not json");
        all["num"].Tokens.Should().BeNull();
        all["num"].RawValue.Should().Be("[1,2]");
        all["obj"].Tokens.Should().BeNull();
        all["seed"].Tokens.Should().Equal("x");
    }

    [Fact]
    public void Operations_ShouldThrowConfigStoreException_WhenDatabaseCorrupt()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_db)!);
        File.WriteAllBytes(_db, Enumerable.Range(0, 4096).Select(i => (byte)(i * 31 + 7)).ToArray());
        var t = _store.ForTopic("llm");

        t.Invoking(x => x.GetAll()).Should().Throw<ConfigStoreException>().WithMessage($"*{_db}*unreadable*");
        t.Invoking(x => x.Set("a", "b", ["c"])).Should().Throw<ConfigStoreException>().WithMessage($"*{_db}*could not be written*");
        t.Invoking(x => x.Unset("a", "b")).Should().Throw<ConfigStoreException>().WithMessage($"*{_db}*could not be written*");
    }

    [Fact]
    public void Constructor_ShouldUseApplicationDataDefaultPath_WhenNoPathGiven()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify),
            "pbtk", "config.db");
        new SqliteConfigStore().DatabasePath.Should().Be(expected);
        expected.Should().EndWith(Path.Combine("pbtk", "config.db"));
        Path.IsPathRooted(expected).Should().BeTrue();
    }
}
