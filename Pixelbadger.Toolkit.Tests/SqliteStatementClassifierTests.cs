using FluentAssertions;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Tests;

public class SqliteStatementClassifierTests : IDisposable
{
    private readonly string _dir;
    private readonly string _dbPath;

    public SqliteStatementClassifierTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_dir);
        _dbPath = Path.Combine(_dir, "t.db");
        using var c = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_dbPath};Pooling=False");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "CREATE TABLE t(id INTEGER)";
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    private BatchPlan Plan(string sql)
    {
        using var session = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_dbPath};Pooling=False");
        session.Open();
        return SqliteStatementClassifier.Classify(session.Handle!, sql);
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("  -- c\n ; ", 0)]
    [InlineData("SELECT 1", 1)]
    [InlineData("SELECT 1; SELECT 2;", 2)]
    public void Classify_ShouldCountStatements(string sql, int expected) =>
        Plan(sql).Statements.Should().Be(expected);

    [Theory]
    [InlineData("SELECT * FROM t", false)]
    [InlineData("INSERT INTO t VALUES (1)", true)]
    [InlineData("SELECT 1; DELETE FROM t", true)]
    [InlineData("CREATE TABLE a(x); INSERT INTO a VALUES (1)", true)]
    public void Classify_ShouldDetectWrites(string sql, bool write) =>
        Plan(sql).HasWrite.Should().Be(write);

    [Theory]
    [InlineData("BEGIN")]
    [InlineData("commit")]
    [InlineData("SELECT 1; ROLLBACK")]
    [InlineData("CREATE TABLE a(x); SAVEPOINT s")]
    public void Classify_ShouldDetectTransactionControl(string sql) =>
        Plan(sql).HasTransactionControl.Should().BeTrue();

    [Theory]
    [InlineData("VACUUM")]
    [InlineData("PRAGMA journal_mode=DELETE")]
    [InlineData("CREATE TABLE a(x); ATTACH ':memory:' AS o")]
    public void Classify_ShouldDetectTransactionIncompatible(string sql) =>
        Plan(sql).HasTransactionIncompatible.Should().BeTrue();

    [Theory]
    [InlineData("SELECT 'VACUUM'")]
    [InlineData("PRAGMA table_info(t)")]
    [InlineData("PRAGMA foreign_keys")]
    [InlineData("INSERT INTO t VALUES (1)")]
    public void Classify_ShouldNotFlagCompatibleStatements(string sql) =>
        Plan(sql).HasTransactionIncompatible.Should().BeFalse();

    [Fact]
    public void ScanLexically_ShouldNotSplitInsideTriggerBodyStringsOrComments()
    {
        var (count, control, _) = SqliteStatementClassifier.ScanLexically(
            "CREATE TRIGGER g AFTER INSERT ON t BEGIN SELECT ';'; SELECT CASE WHEN 1 THEN 2 END; END; /* ; */ SELECT 'a;b' -- ;");

        count.Should().Be(2);
        control.Should().BeFalse();
    }

    [Theory]
    [InlineData("  /* x */ select 1", "select")]
    [InlineData("-- c\nBEGIN", "BEGIN")]
    [InlineData("   ", null)]
    public void FirstKeyword_ShouldSkipWhitespaceAndComments(string text, string? expected) =>
        SqliteStatementClassifier.FirstKeyword(text).Should().Be(expected);

    [Theory]
    [InlineData("CREATE TRIGGER g AFTER INSERT ON t BEGIN SELECT 1;", true)]
    [InlineData("CREATE TEMP TRIGGER g AFTER INSERT ON t BEGIN SELECT 1; END", false)]
    [InlineData("SELECT 1", false)]
    public void IsOpenTrigger_ShouldReportUnclosedBody(string text, bool open) =>
        SqliteStatementClassifier.IsOpenTrigger(text).Should().Be(open);
}
