using FluentAssertions;
using Pixelbadger.Toolkit.Services;
using Spectre.Console;

namespace Pixelbadger.Toolkit.Tests;

[Collection("AnsiConsole")]
public class McpSaveComponentTests : IDisposable
{
    private readonly string _dir;
    private readonly string _db;
    private readonly IAnsiConsole _originalConsole = AnsiConsole.Console;
    private readonly SqliteConfigStore _store;
    private readonly SqliteMcpProfileStore _profiles;

    public McpSaveComponentTests()
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

    [Fact]
    public async Task Save_ShouldPersistProfileAndReportToolCount_WhenValid()
    {
        var (code, output, _) = await RunAsync("mcp", "save", "--name", "tools", "--include", "interpreters/ook", "strings", "--host", "0.0.0.0", "--port", "6000");

        code.Should().Be(0);
        output.Trim().Should().Be("Saved MCP profile 'tools' (6 tools)");
        var saved = _profiles.Get("tools")!;
        saved.Include.Should().Equal("interpreters/ook", "strings");
        saved.Host.Should().Be("0.0.0.0");
        saved.Port.Should().Be(6000);
    }

    [Fact]
    public async Task Save_ShouldOverwriteSilently_WhenNameExists()
    {
        await RunAsync("mcp", "save", "--name", "p", "--include", "interpreters", "--port", "6000");

        var (code, output, _) = await RunAsync("mcp", "save", "--name", "p", "--include", "interpreters/ook");

        code.Should().Be(0);
        output.Trim().Should().Be("Saved MCP profile 'p' (1 tools)");
        var saved = _profiles.Get("p")!;
        saved.Include.Should().Equal("interpreters/ook");
        saved.Port.Should().BeNull();
        saved.Host.Should().BeNull();
    }

    [Theory]
    [InlineData("Bad", "Invalid profile name 'Bad'. Profile names must match ^[a-z0-9-]+$.")]
    [InlineData("has space", "Invalid profile name 'has space'. Profile names must match ^[a-z0-9-]+$.")]
    [InlineData("under_score", "Invalid profile name 'under_score'. Profile names must match ^[a-z0-9-]+$.")]
    public async Task Save_ShouldFail_WhenNameIsInvalid(string name, string message)
    {
        var (code, output, _) = await RunAsync("mcp", "save", "--name", name, "--include", "interpreters");

        code.Should().Be(1);
        output.Trim().Should().Be("Error: " + message);
        _profiles.GetAll().Should().BeEmpty();
    }

    [Theory]
    [InlineData("config")]
    [InlineData("demoscene")]
    [InlineData("web/serve-html")]
    [InlineData("sqlite")]
    [InlineData("oauth/token")]
    [InlineData("mcp")]
    [InlineData("nosuch")]
    public async Task Save_ShouldFailNamingFirstOffendingEntry_WhenEntryIsUnknownOrExcluded(string bad)
    {
        var (code, output, _) = await RunAsync("mcp", "save", "--name", "p", "--include", "interpreters", bad, "also-bad");

        code.Should().Be(1);
        output.Trim().Should().Be($"Error: Unknown or non-exposable entry '{bad}'.");
        _profiles.GetAll().Should().BeEmpty();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("-1")]
    public async Task Save_ShouldFail_WhenPortIsOutOfRange(string port)
    {
        var (code, output, _) = await RunAsync("mcp", "save", "--name", "p", "--include", "interpreters", "--port", port);

        code.Should().Be(1);
        output.Trim().Should().Be("Error: Port must be between 1 and 65535.");
        _profiles.GetAll().Should().BeEmpty();
    }

    [Fact]
    public async Task Save_ShouldFail_WhenHostIsEmpty()
    {
        var (code, output, _) = await RunAsync("mcp", "save", "--name", "p", "--include", "interpreters", "--host", "");

        code.Should().Be(1);
        output.Trim().Should().Be("Error: Host must not be empty.");
        _profiles.GetAll().Should().BeEmpty();
    }
}
