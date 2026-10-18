using FluentAssertions;
using Pixelbadger.Toolkit.Services;
using Spectre.Console;

namespace Pixelbadger.Toolkit.Tests;

[Collection("AnsiConsole")]
public class McpDeleteComponentTests : IDisposable
{
    private readonly string _dir;
    private readonly string _db;
    private readonly IAnsiConsole _originalConsole = AnsiConsole.Console;
    private readonly SqliteConfigStore _store;
    private readonly SqliteMcpProfileStore _profiles;

    public McpDeleteComponentTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_dir);
        _db = Path.Combine(_dir, "config.db");
        _store = new SqliteConfigStore(_db);
        _profiles = new SqliteMcpProfileStore(_db);
    }

    public void Dispose()
    {
        AnsiConsole.Console = _originalConsole;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private Task<(int Code, string Out, string Err)> RunAsync(params string[] args) =>
        McpCommandTestSupport.RunAsync(_store, args);

    private static string[] Lines(string text) => McpCommandTestSupport.Lines(text);

    [Fact]
    public async Task Delete_ShouldRemoveProfile_WhenSaved()
    {
        await RunAsync("mcp", "save", "--name", "p", "--include", "interpreters");

        var (code, output, _) = await RunAsync("mcp", "delete", "--name", "p");

        code.Should().Be(0);
        output.Trim().Should().Be("Deleted MCP profile 'p'");
        _profiles.Get("p").Should().BeNull();
    }

    [Fact]
    public async Task Delete_ShouldFail_WhenProfileNotFound()
    {
        var (code, output, _) = await RunAsync("mcp", "delete", "--name", "ghost");

        code.Should().Be(1);
        output.Trim().Should().Be("Error: MCP profile 'ghost' not found.");
    }

    [Fact]
    public async Task Delete_ShouldFail_WhenNameIsInvalid()
    {
        var (code, output, _) = await RunAsync("mcp", "delete", "--name", "Nope!");

        code.Should().Be(1);
        output.Trim().Should().Be("Error: Invalid profile name 'Nope!'. Profile names must match ^[a-z0-9-]+$.");
    }

    [Fact]
    public async Task Delete_ShouldFail_WhenDefaultIsOnlyBuiltIn()
    {
        var (code, output, _) = await RunAsync("mcp", "delete", "--name", "default");

        code.Should().Be(1);
        output.Trim().Should().Be("Error: MCP profile 'default' not found.");
    }

    [Fact]
    public async Task DefaultOverride_ShouldReplaceBuiltInAndRestore_WhenSavedThenDeleted()
    {
        await RunAsync("mcp", "save", "--name", "default", "--include", "strings/reverse");
        var (_, overridden, _) = await RunAsync("mcp", "list");

        var (code, output, _) = await RunAsync("mcp", "delete", "--name", "default");
        var (_, restored, _) = await RunAsync("mcp", "list");

        overridden.Should().NotContain("(built-in)").And.Contain("include: strings/reverse");
        code.Should().Be(0);
        Lines(output).Should().Equal("Deleted MCP profile 'default'", "Built-in default profile restored.");
        Lines(restored).Should().Equal("default (built-in)", "  include: interpreters", "  host: (default)", "  port: (default)");
    }
}
