using System.CommandLine;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Pixelbadger.Toolkit.CommandLine;
using Pixelbadger.Toolkit.Commands;
using Spectre.Console;
using SqliteTopic = Pixelbadger.Toolkit.Commands.SqliteCommand;

namespace Pixelbadger.Toolkit.Tests;

[Collection("AnsiConsole")]
public class SqliteCommandTests : IDisposable
{
    private readonly string _dir;
    private readonly IAnsiConsole _originalConsole = AnsiConsole.Console;

    public SqliteCommandTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        AnsiConsole.Console = _originalConsole;
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private static RootCommand NewRoot() => new("test") { SqliteTopic.Create(() => false) };

    private static async Task<(int Code, string Out, string Err)> InvokeAsync(RootCommand root, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        ToolkitConsole.InstallRouting();
        using (ToolkitConsole.Capture(ToolkitConsole.CreateCaptureConsole(output), output, error))
        {
            var code = await root.Parse(args).InvokeAsync();
            return (code, output.ToString(), error.ToString());
        }
    }

    private string CreateDatabase(string name)
    {
        var path = Path.Combine(_dir, name);
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var create = connection.CreateCommand();
        create.CommandText = "CREATE TABLE t (x INTEGER); INSERT INTO t VALUES (1);";
        create.ExecuteNonQuery();
        return path;
    }

    [Fact]
    public async Task Invoke_ShouldReturnOneAndPrintExactError_WhenFileIsNotSqlite()
    {
        var path = Path.Combine(_dir, "garbage.db");
        await File.WriteAllTextAsync(path, "not a database");

        var (code, output, _) = await InvokeAsync(NewRoot(), "sqlite", "tui", "--db", path);

        code.Should().Be(1);
        var line = output.Trim();
        line.Should().StartWith($"Error: '{path}' is not a valid SQLite database: ");
        line.Should().Contain("file is not a database");
    }

    [Fact]
    public async Task Invoke_ShouldReturnOneAndPrintExactError_WhenNotInteractive()
    {
        var path = CreateDatabase("valid.db");

        var (code, output, _) = await InvokeAsync(NewRoot(), "sqlite", "tui", "--db", path);

        code.Should().Be(1);
        output.Trim().Should().Be("Error: sqlite tui requires an interactive terminal");
    }

    [Fact]
    public async Task Invoke_ShouldReleaseDatabaseLock_WhenNotInteractive()
    {
        var path = CreateDatabase("unlocked.db");

        await InvokeAsync(NewRoot(), "sqlite", "tui", "--db", path);

        using var connection = new SqliteConnection($"Data Source={path};Default Timeout=0");
        connection.Open();
        using var begin = connection.CreateCommand();
        begin.CommandText = "BEGIN IMMEDIATE; COMMIT;";
        var action = () => begin.ExecuteNonQuery();
        action.Should().NotThrow();
    }

    [Fact]
    public async Task Invoke_ShouldReturnOneAndPrintExactError_WhenFileIsMissing()
    {
        var path = Path.Combine(_dir, "missing.db");

        var (code, output, _) = await InvokeAsync(NewRoot(), "sqlite", "tui", "--db", path);

        code.Should().Be(1);
        output.Trim().Should().Be($"Error: Database file '{path}' not found");
    }

    [Fact]
    public async Task Invoke_ShouldNotCreateFile_WhenFileIsMissing()
    {
        var path = Path.Combine(_dir, "never-created.db");

        await InvokeAsync(NewRoot(), "sqlite", "tui", "--db", path);

        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public async Task Invoke_ShouldReturnNonZero_WhenDbOptionIsMissing()
    {
        var (code, _, _) = await InvokeAsync(NewRoot(), "sqlite", "tui");

        code.Should().NotBe(0);
    }

    [Fact]
    public void Create_ShouldDescribeTopicAndAction_WhenBuilt()
    {
        var topic = SqliteTopic.Create();

        topic.Name.Should().Be("sqlite");
        topic.Description.Should().Be("Interactive SQLite tools");
        var tui = topic.Subcommands.Should().ContainSingle().Subject;
        tui.Name.Should().Be("tui");
        tui.Description.Should().Be("Interactive terminal UI for browsing and querying a SQLite database");
    }

    [Fact]
    public void Create_ShouldMarkDbOptionRequired_WhenBuilt()
    {
        var db = SqliteTopic.Create().Subcommands.Single().Options.Single(o => o.Name == "--db");

        db.Required.Should().BeTrue();
    }

    [Fact]
    public void IsExcludedFromMcp_ShouldBeTrue_WhenTopicIsSqlite()
    {
        SqliteTopic.Create().IsExcludedFromMcp().Should().BeTrue();
    }

    [Fact]
    public void ConfigTree_ShouldExposeNoSettableOptions_WhenSqliteTuiIsRegistered()
    {
        var root = ToolkitCommandTree.Create(new InMemoryConfigStore());

        var tree = ConfigTree.Build(root);

        tree.Find("sqlite", "tui").Should().BeNull();
        tree.Topics.Select(t => t.Topic).Should().NotContain("sqlite");
    }
}
