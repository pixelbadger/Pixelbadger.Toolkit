using FluentAssertions;
using Microsoft.Data.Sqlite;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Tests;

public class SqliteSessionTests : IDisposable
{
    private const string RefusedMessage = "Transactions are managed by the TUI: Ctrl+S saves, F8 discards";

    private readonly string _testDirectory;
    private readonly string _dbPath;
    private readonly List<IDisposable> _disposables = new();

    public SqliteSessionTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDirectory);
        _dbPath = Path.Combine(_testDirectory, "test.db");
        using var setup = Raw(_dbPath);
        Run(setup, "CREATE TABLE items(id INTEGER PRIMARY KEY, name TEXT NOT NULL); INSERT INTO items VALUES (1,'a'),(2,'b');");
    }

    public void Dispose()
    {
        foreach (var d in _disposables)
            d.Dispose();
        if (Directory.Exists(_testDirectory))
        {
            foreach (var f in Directory.GetFiles(_testDirectory))
                File.SetUnixFileMode(f, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            Directory.Delete(_testDirectory, true);
        }
    }

    private SqliteSession Open(int timeout = 2)
    {
        var session = SqliteSession.Open(_dbPath, timeout);
        _disposables.Add(session);
        return session;
    }

    private SqliteConnection Raw(string path, int timeout = 1)
    {
        var connection = new SqliteConnection($"Data Source={path};Pooling=False;Default Timeout={timeout}");
        connection.Open();
        _disposables.Add(connection);
        return connection;
    }

    private static void Run(SqliteConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static long Scalar(SqliteConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private long CountItems()
    {
        using var c = Raw(_dbPath);
        return Scalar(c, "SELECT count(*) FROM items");
    }

    // ---- Open ----

    [Fact]
    public void Open_ShouldThrowNotFoundAndNotCreateFile_WhenFileMissing()
    {
        var missing = Path.Combine(_testDirectory, "missing.db");

        var act = () => SqliteSession.Open(missing);

        act.Should().Throw<SqliteSessionOpenException>().WithMessage($"Database file '{missing}' not found");
        File.Exists(missing).Should().BeFalse();
    }

    [Fact]
    public void Open_ShouldThrowInvalidDatabase_WhenFileIsNotSqlite()
    {
        var path = Path.Combine(_testDirectory, "text.db");
        File.WriteAllText(path, "this is definitely not a sqlite database, just some plain text padding " + new string('x', 200));

        var act = () => SqliteSession.Open(path);

        act.Should().Throw<SqliteSessionOpenException>()
            .Which.Message.Should().StartWith($"'{path}' is not a valid SQLite database: ")
            .And.Contain("file is not a database");
    }

    [Fact]
    public void Open_ShouldSucceed_WhenFileIsValid()
    {
        var session = Open();

        session.DatabasePath.Should().Be(Path.GetFullPath(_dbPath));
        session.IsReadOnly.Should().BeFalse();
        session.HasUnsavedChanges.Should().BeFalse();
    }

    [Fact]
    public void Open_ShouldBeReadOnlyAndRejectWrites_WhenFileModeIs0444()
    {
        File.SetUnixFileMode(_dbPath, UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        var session = Open();

        session.IsReadOnly.Should().BeTrue();
        session.Execute("SELECT count(*) FROM items").Outcome.Should().Be(QueryOutcome.ResultSet);

        var result = session.Execute("INSERT INTO items VALUES (3,'c')");

        result.Outcome.Should().Be(QueryOutcome.Error);
        result.Message.Should().Contain("readonly");
        session.HasUnsavedChanges.Should().BeFalse();
    }

    // ---- Schema ----

    [Fact]
    public void GetSchema_ShouldOrderTablesThenViewsCaseInsensitively_AndExcludeSqliteTables()
    {
        using (var c = Raw(_dbPath))
        {
            Run(c, "CREATE TABLE Zeta(x); CREATE TABLE alpha(x); CREATE TABLE beta(x);");
            Run(c, "CREATE VIEW v_b AS SELECT 1 AS one; CREATE VIEW V_a AS SELECT 2 AS two;");
            Run(c, "CREATE TABLE seq_user(id INTEGER PRIMARY KEY AUTOINCREMENT);");
        }
        var session = Open();

        var schema = session.GetSchema();

        schema.Select(s => (s.Name, s.Kind)).Should().Equal(
            ("alpha", SchemaObjectKind.Table),
            ("beta", SchemaObjectKind.Table),
            ("items", SchemaObjectKind.Table),
            ("seq_user", SchemaObjectKind.Table),
            ("Zeta", SchemaObjectKind.Table),
            ("V_a", SchemaObjectKind.View),
            ("v_b", SchemaObjectKind.View));
        schema.Should().NotContain(s => s.Name.StartsWith("sqlite_"));
    }

    [Fact]
    public void GetSchema_ShouldReturnColumnsWithTypePrimaryKeyAndNotNull_InDeclaredOrder()
    {
        using (var c = Raw(_dbPath))
            Run(c, "CREATE TABLE t(z INTEGER PRIMARY KEY, a TEXT NOT NULL, untyped, m REAL);");
        var session = Open();

        var columns = session.GetSchema().Single(s => s.Name == "t").Columns;

        columns.Should().Equal(
            new SchemaColumn("z", "INTEGER", true, false),
            new SchemaColumn("a", "TEXT", false, true),
            new SchemaColumn("untyped", "", false, false),
            new SchemaColumn("m", "REAL", false, false));
    }

    [Fact]
    public void GetSchema_ShouldReturnViewColumns()
    {
        using (var c = Raw(_dbPath))
            Run(c, "CREATE VIEW v AS SELECT id, name FROM items;");
        var session = Open();

        var view = session.GetSchema().Single(s => s.Name == "v");

        view.Kind.Should().Be(SchemaObjectKind.View);
        view.Columns.Select(c => c.Name).Should().Equal("id", "name");
    }

    [Fact]
    public void GetSchema_ShouldIncludeGeneratedColumns()
    {
        using (var c = Raw(_dbPath))
            Run(c, "CREATE TABLE g(a INTEGER, b INTEGER GENERATED ALWAYS AS (a * 2) VIRTUAL);");
        var session = Open();

        session.GetSchema().Single(s => s.Name == "g").Columns.Select(c => c.Name).Should().Equal("a", "b");
    }

    [Fact]
    public void GetSchema_ShouldReflectUncommittedDdl()
    {
        var session = Open();

        session.Execute("CREATE TABLE fresh(a INTEGER)").Outcome.Should().Be(QueryOutcome.RowsAffected);

        session.GetSchema().Should().Contain(s => s.Name == "fresh");
        using var other = Raw(_dbPath);
        Scalar(other, "SELECT count(*) FROM sqlite_master WHERE name='fresh'").Should().Be(0);
    }

    // ---- Execute: results ----

    [Fact]
    public void Execute_ShouldReturnTypedValues_WhenSelectingEachStorageClass()
    {
        var session = Open();

        var result = session.Execute("SELECT NULL AS n, 42 AS i, 1.5 AS r, 'txt' AS t, x'0102' AS b");

        result.Outcome.Should().Be(QueryOutcome.ResultSet);
        result.Columns.Should().Equal("n", "i", "r", "t", "b");
        result.Rows.Should().HaveCount(1);
        var row = result.Rows[0];
        row[0].Should().BeNull();
        row[1].Should().Be(42L);
        row[2].Should().Be(1.5);
        row[3].Should().Be("txt");
        row[4].Should().BeOfType<byte[]>().Which.Should().Equal(1, 2);
        result.Truncated.Should().BeFalse();
        result.Message.Should().BeNull();
        session.HasUnsavedChanges.Should().BeFalse();
    }

    [Fact]
    public void Execute_ShouldKeepLastResultSetWithColumns_WhenBatchHasSeveralSelects()
    {
        var session = Open();

        var result = session.Execute("SELECT 1 AS first; SELECT id FROM items ORDER BY id; ");

        result.Columns.Should().Equal("id");
        result.Rows.Select(r => r[0]).Should().Equal(1L, 2L);
    }

    [Fact]
    public void Execute_ShouldReturnEmptyResultSetWithColumns_WhenSelectMatchesNoRows()
    {
        var session = Open();

        var result = session.Execute("SELECT id, name FROM items WHERE id > 100");

        result.Outcome.Should().Be(QueryOutcome.ResultSet);
        result.Columns.Should().Equal("id", "name");
        result.Rows.Should().BeEmpty();
    }

    [Theory]
    [InlineData(1000, false)]
    [InlineData(1001, true)]
    [InlineData(2500, true)]
    public void Execute_ShouldCapRowsAtMaxRows_AndFlagTruncation(int total, bool truncated)
    {
        var session = Open();

        var result = session.Execute(
            $"WITH RECURSIVE n(x) AS (SELECT 1 UNION ALL SELECT x+1 FROM n WHERE x < {total}) SELECT x FROM n");

        result.Rows.Should().HaveCount(SqliteSession.MaxRows);
        result.Truncated.Should().Be(truncated);
        result.Rows[^1][0].Should().Be(1000L);
    }

    [Fact]
    public void Execute_ShouldSumRowsAffected_AcrossStatements()
    {
        var session = Open();

        var result = session.Execute("INSERT INTO items VALUES (3,'c'),(4,'d'); UPDATE items SET name='z' WHERE id IN (1,2);");

        result.Outcome.Should().Be(QueryOutcome.RowsAffected);
        result.RowsAffected.Should().Be(4);
        result.Columns.Should().BeEmpty();
        result.Rows.Should().BeEmpty();
    }

    [Fact]
    public void Execute_ShouldReportZeroRowsAffected_ForDdl()
    {
        var session = Open();

        var result = session.Execute("CREATE TABLE d(a)");

        result.Outcome.Should().Be(QueryOutcome.RowsAffected);
        result.RowsAffected.Should().Be(0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n\t ")]
    [InlineData("-- just a comment")]
    [InlineData("/* block */ ; ;  ")]
    public void Execute_ShouldReturnNothingToExecute_WhenNoStatements(string sql)
    {
        var session = Open();

        var result = session.Execute(sql);

        result.Outcome.Should().Be(QueryOutcome.NothingToExecute);
        result.Message.Should().Be("Nothing to execute");
        session.HasUnsavedChanges.Should().BeFalse();
    }

    [Fact]
    public void Execute_ShouldReportElapsedTime()
    {
        Open().Execute("SELECT 1").Elapsed.Should().BeGreaterThan(TimeSpan.Zero);
    }

    // ---- Execute: refusal ----

    [Theory]
    [InlineData("BEGIN")]
    [InlineData("begin transaction")]
    [InlineData("COMMIT")]
    [InlineData("commit")]
    [InlineData("END")]
    [InlineData("end")]
    [InlineData("ROLLBACK")]
    [InlineData("Rollback")]
    [InlineData("SAVEPOINT sp")]
    [InlineData("savepoint sp")]
    [InlineData("RELEASE sp")]
    [InlineData("release sp")]
    [InlineData("-- hi\nCOMMIT")]
    [InlineData("/* x */ rollback")]
    public void Execute_ShouldRefuseAndExecuteNothing_WhenBatchContainsTransactionControl(string control)
    {
        var session = Open();

        var result = session.Execute($"INSERT INTO items VALUES (9,'nine'); {control}; INSERT INTO items VALUES (10,'ten');");

        result.Outcome.Should().Be(QueryOutcome.Refused);
        result.Message.Should().Be(RefusedMessage);
        session.HasUnsavedChanges.Should().BeFalse();
        CountItems().Should().Be(2);
        session.Execute("SELECT count(*) FROM items").Rows[0][0].Should().Be(2L);
    }

    [Theory]
    [InlineData("VACUUM")]
    [InlineData("PRAGMA journal_mode=WAL")]
    [InlineData("PRAGMA foreign_keys = ON")]
    [InlineData("PRAGMA main.foreign_keys(1)")]
    [InlineData("ATTACH DATABASE ':memory:' AS other")]
    [InlineData("DETACH DATABASE other")]
    [InlineData("DELETE FROM items; VACUUM")]
    public void Execute_ShouldRefuseAndExecuteNothing_WhenBatchContainsTransactionIncompatibleStatement(string sql)
    {
        var session = Open();

        var result = session.Execute(sql);

        result.Outcome.Should().Be(QueryOutcome.Refused);
        result.Message.Should().StartWith("Refused: VACUUM, ATTACH, DETACH");
        session.HasUnsavedChanges.Should().BeFalse();
        session.Execute("SELECT count(*) FROM items").Rows[0][0].Should().Be(2L);
    }

    [Fact]
    public void Execute_ShouldAllowPragmaRead_WhenNoValueAssigned()
    {
        var session = Open();

        session.Execute("PRAGMA foreign_keys").Outcome.Should().Be(QueryOutcome.ResultSet);
    }

    [Fact]
    public void Execute_ShouldRefuse_WhenTransactionControlFollowsUnpreparableStatement()
    {
        var session = Open();

        var result = session.Execute("CREATE TABLE later(a); INSERT INTO later VALUES (1); 'x'; COMMIT;");

        result.Outcome.Should().Be(QueryOutcome.Refused);
        session.GetSchema().Should().NotContain(s => s.Name == "later");
    }

    [Fact]
    public void Execute_ShouldNotRefuse_WhenKeywordOnlyAppearsInsideStringOrIdentifier()
    {
        var session = Open();

        var result = session.Execute("CREATE TABLE later(a); INSERT INTO later VALUES ('; COMMIT; --'); SELECT a FROM later");

        result.Outcome.Should().Be(QueryOutcome.ResultSet);
        result.Rows[0][0].Should().Be("; COMMIT; --");
    }

    // ---- Execute: errors ----

    [Fact]
    public void Execute_ShouldRollBackEarlierStatementsOfBatch_AndKeepPriorDirtyChanges_WhenStatementFails()
    {
        var session = Open();
        session.Execute("INSERT INTO items VALUES (3,'c')").Outcome.Should().Be(QueryOutcome.RowsAffected);

        var result = session.Execute("INSERT INTO items VALUES (4,'d'); INSERT INTO items VALUES (1,'dup');");

        result.Outcome.Should().Be(QueryOutcome.Error);
        result.Message.Should().Contain("UNIQUE constraint failed");
        session.HasUnsavedChanges.Should().BeTrue();
        session.Execute("SELECT id FROM items ORDER BY id").Rows.Select(r => r[0]).Should().Equal(1L, 2L, 3L);
        session.Save().Success.Should().BeTrue();
        CountItems().Should().Be(3);
    }

    [Fact]
    public void Execute_ShouldStayClean_WhenFirstWriteBatchFails()
    {
        var session = Open();

        var result = session.Execute("INSERT INTO items VALUES (4,'d'); INSERT INTO nope VALUES (1);");

        result.Outcome.Should().Be(QueryOutcome.Error);
        result.Message.Should().Contain("no such table");
        session.HasUnsavedChanges.Should().BeFalse();
        using var other = Raw(_dbPath);
        Run(other, "INSERT INTO items VALUES (7,'g')"); // lock was released
    }

    [Fact]
    public void Execute_ShouldReturnError_WhenSqlIsInvalid()
    {
        var result = Open().Execute("SELEC 1");

        result.Outcome.Should().Be(QueryOutcome.Error);
        result.Message.Should().Contain("syntax error");
    }

    [Fact]
    public void Execute_ShouldReturnDatabaseLockedError_WhenAnotherConnectionHoldsWriteLock()
    {
        var session = Open(timeout: 1);
        using var other = Raw(_dbPath);
        Run(other, "BEGIN; INSERT INTO items VALUES (50,'x');");

        var result = session.Execute("INSERT INTO items VALUES (51,'y')");

        result.Outcome.Should().Be(QueryOutcome.Error);
        result.Message.Should().Contain("SQLite Error 5").And.Contain("database is locked");
        session.HasUnsavedChanges.Should().BeFalse();
    }

    // ---- Transactions / locks ----

    [Fact]
    public void Execute_ShouldLeaveSessionCleanAndReleaseLock_WhenBatchIsReadOnly()
    {
        var session = Open();

        session.Execute("SELECT * FROM items");

        session.HasUnsavedChanges.Should().BeFalse();
        using var other = Raw(_dbPath);
        Run(other, "INSERT INTO items VALUES (5,'e')");
        CountItems().Should().Be(3);
    }

    [Fact]
    public void Execute_ShouldMarkDirtyAndHoldWriteLock_WhenBatchWrites()
    {
        var session = Open();

        session.Execute("INSERT INTO items VALUES (3,'c')");

        session.HasUnsavedChanges.Should().BeTrue();
        using var other = Raw(_dbPath, timeout: 1);
        var act = () => Run(other, "INSERT INTO items VALUES (4,'d')");
        act.Should().Throw<SqliteException>().Which.SqliteErrorCode.Should().Be(5);
        Scalar(other, "SELECT count(*) FROM items").Should().Be(2);
    }

    [Fact]
    public void Execute_ShouldStayDirty_WhenReadOnlyBatchFollowsWrite()
    {
        var session = Open();
        session.Execute("INSERT INTO items VALUES (3,'c')");

        session.Execute("SELECT * FROM items");

        session.HasUnsavedChanges.Should().BeTrue();
    }

    [Fact]
    public void Execute_ShouldTreatBatchAsWriting_WhenStatementDependsOnObjectCreatedInSameBatch()
    {
        var session = Open();

        var result = session.Execute("CREATE TABLE made(a INTEGER); INSERT INTO made VALUES (1),(2); SELECT count(*) FROM made;");

        result.Outcome.Should().Be(QueryOutcome.ResultSet);
        result.Rows[0][0].Should().Be(2L);
        session.HasUnsavedChanges.Should().BeTrue();
        session.Save().Success.Should().BeTrue();
        using var other = Raw(_dbPath);
        Scalar(other, "SELECT count(*) FROM made").Should().Be(2);
    }

    [Fact]
    public void Execute_ShouldTreatRowReturningWrite_AsDirty()
    {
        var session = Open();

        var result = session.Execute("INSERT INTO items VALUES (3,'c') RETURNING id");

        result.Outcome.Should().Be(QueryOutcome.ResultSet);
        result.Rows[0][0].Should().Be(3L);
        session.HasUnsavedChanges.Should().BeTrue();
    }

    // ---- Save / Discard ----

    [Fact]
    public void Save_ShouldCommitAndReleaseLock_WhenDirty()
    {
        var session = Open();
        session.Execute("INSERT INTO items VALUES (3,'c')");

        var result = session.Save();

        result.Should().Be(new SessionActionResult(true, "Changes saved"));
        session.HasUnsavedChanges.Should().BeFalse();
        using var other = Raw(_dbPath);
        Scalar(other, "SELECT count(*) FROM items").Should().Be(3);
        Run(other, "INSERT INTO items VALUES (4,'d')");
    }

    [Fact]
    public void Save_ShouldReportNothingToSave_WhenClean()
    {
        Open().Save().Should().Be(new SessionActionResult(true, "No changes to save"));
    }

    [Fact]
    public void Save_ShouldFailKeepDirtyAndSucceedLater_WhenReaderBlocksCommit()
    {
        var session = Open(timeout: 1);
        session.Execute("INSERT INTO items VALUES (3,'c')");
        using var reader = Raw(_dbPath);
        Run(reader, "BEGIN");
        Scalar(reader, "SELECT count(*) FROM items").Should().Be(2); // holds a SHARED lock

        var failed = session.Save();

        failed.Success.Should().BeFalse();
        failed.Message.Should().Contain("SQLite Error 5");
        session.HasUnsavedChanges.Should().BeTrue();

        Run(reader, "COMMIT");
        session.Save().Should().Be(new SessionActionResult(true, "Changes saved"));
        session.HasUnsavedChanges.Should().BeFalse();
        CountItems().Should().Be(3);
    }

    [Fact]
    public void Discard_ShouldRollBack_WhenDirty()
    {
        var session = Open();
        session.Execute("INSERT INTO items VALUES (3,'c'); CREATE TABLE gone(a);");

        var result = session.Discard();

        result.Should().Be(new SessionActionResult(true, "Changes discarded"));
        session.HasUnsavedChanges.Should().BeFalse();
        CountItems().Should().Be(2);
        session.GetSchema().Should().NotContain(s => s.Name == "gone");
        using var other = Raw(_dbPath);
        Run(other, "INSERT INTO items VALUES (4,'d')");
    }

    [Fact]
    public void Discard_ShouldReportNothingToDiscard_WhenClean()
    {
        Open().Discard().Should().Be(new SessionActionResult(true, "No changes to discard"));
    }

    [Fact]
    public void Execute_ShouldStartFreshTransaction_AfterSaveAndAfterDiscard()
    {
        var session = Open();
        session.Execute("INSERT INTO items VALUES (3,'c')");
        session.Save();
        session.Execute("INSERT INTO items VALUES (4,'d')");
        session.Discard();

        session.Execute("SELECT count(*) FROM items").Rows[0][0].Should().Be(3L);
        session.HasUnsavedChanges.Should().BeFalse();
    }

    // ---- Regressions: RowsAffected, CREATE TRIGGER, failed COMMIT/ROLLBACK ----

    [Fact]
    public void Execute_ShouldCountOnlyDml_WhenDdlFollowsDmlInBatch()
    {
        var session = Open();
        session.Execute("INSERT INTO items VALUES (3,'c'),(4,'d')");

        var result = session.Execute("INSERT INTO items SELECT id + 10, name FROM items; CREATE TABLE z(y)");

        result.Outcome.Should().Be(QueryOutcome.RowsAffected);
        result.RowsAffected.Should().Be(4);
    }

    [Fact]
    public void Execute_ShouldReportZeroAffected_WhenCreateTableFollowsEarlierDml()
    {
        var session = Open();
        session.Execute("INSERT INTO items VALUES (3,'c')");

        var result = session.Execute("CREATE TABLE zz(a)");

        result.Outcome.Should().Be(QueryOutcome.RowsAffected);
        result.RowsAffected.Should().Be(0);
    }

    [Fact]
    public void Execute_ShouldReportZeroAffected_WhenPragmaFollowsEarlierDml()
    {
        var session = Open();
        session.Execute("INSERT INTO items VALUES (3,'c')");

        var result = session.Execute("PRAGMA user_version=3");

        result.Outcome.Should().Be(QueryOutcome.RowsAffected);
        result.RowsAffected.Should().Be(0);
    }

    [Fact]
    public void Execute_ShouldSumAffected_WhenBatchIsPureMultiStatementDml()
    {
        var session = Open();

        var result = session.Execute("INSERT INTO items VALUES (3,'c'),(4,'d'); UPDATE items SET name='x' WHERE id <= 2; DELETE FROM items WHERE id = 4");

        result.RowsAffected.Should().Be(5);
    }

    [Fact]
    public void Execute_ShouldCreateWorkingTrigger_WhenTriggerDependsOnTablesCreatedInSameBatch()
    {
        var session = Open();

        var result = session.Execute("create table q(a); create table u(x); create trigger tr after insert on q begin insert into u values(1); end;");

        result.Outcome.Should().Be(QueryOutcome.RowsAffected);
        session.HasUnsavedChanges.Should().BeTrue();
        session.Execute("insert into q values(5)").Outcome.Should().Be(QueryOutcome.RowsAffected);
        session.Execute("select count(*) from u").Rows[0][0].Should().Be(1L);
    }

    [Fact]
    public void Execute_ShouldNotEndTriggerEarly_WhenBodyContainsCaseExpression()
    {
        var session = Open();

        var result = session.Execute(
            "create table q(a); create table u(x); " +
            "create trigger tr after insert on q begin insert into u values(case when new.a > 1 then 'big' else 'small' end); end;");

        result.Outcome.Should().Be(QueryOutcome.RowsAffected);
        session.Execute("insert into q values(5)");
        session.Execute("select x from u").Rows[0][0].Should().Be("big");
    }

    [Theory]
    [InlineData("create table q2(a); BEGIN;")]
    [InlineData("create table q3(a); END;")]
    public void Execute_ShouldRefuse_WhenTransactionControlFollowsDependentDdl(string sql)
    {
        var session = Open();

        var result = session.Execute(sql);

        result.Outcome.Should().Be(QueryOutcome.Refused);
        result.Message.Should().Be(RefusedMessage);
        session.HasUnsavedChanges.Should().BeFalse();
    }

    // ---- Dispose ----

    [Fact]
    public void Dispose_ShouldRollBackUncommittedChanges_WhenDirty()
    {
        var session = SqliteSession.Open(_dbPath);
        session.Execute("INSERT INTO items VALUES (3,'c')");

        session.Dispose();

        CountItems().Should().Be(2);
        using var reopened = SqliteSession.Open(_dbPath);
        reopened.Execute("SELECT count(*) FROM items").Rows[0][0].Should().Be(2L);
    }

    [Fact]
    public void Dispose_ShouldBeIdempotent()
    {
        var session = SqliteSession.Open(_dbPath);
        session.Execute("INSERT INTO items VALUES (3,'c')");

        session.Dispose();
        var act = () => session.Dispose();

        act.Should().NotThrow();
    }
}
