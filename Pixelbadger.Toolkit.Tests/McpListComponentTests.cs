using FluentAssertions;
using Pixelbadger.Toolkit.CommandLine;
using Pixelbadger.Toolkit.Commands;
using Pixelbadger.Toolkit.Services;
using Spectre.Console;

namespace Pixelbadger.Toolkit.Tests;

[Collection("AnsiConsole")]
public class McpListComponentTests : IDisposable
{
    private readonly string _dir;
    private readonly string _db;
    private readonly IAnsiConsole _originalConsole = AnsiConsole.Console;
    private readonly SqliteConfigStore _store;
    private readonly SqliteMcpProfileStore _profiles;

    public McpListComponentTests()
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
    public async Task List_ShouldNotCreateDatabaseFile_WhenStoreIsInMemory()
    {
        using var memory = new InMemoryConfigStore();
        var root = ToolkitCommandTree.Create(memory);
        var output = new StringWriter();
        ToolkitConsole.InstallRouting();

        int code;
        using (ToolkitConsole.Capture(ToolkitConsole.CreateCaptureConsole(output), output, new StringWriter()))
            code = await ConfigBootstrap.InvokeAsync(root, ["mcp", "list"], memory);

        code.Should().Be(0);
        Path.IsPathRooted(memory.DatabasePath).Should().BeTrue();
        File.Exists(memory.DatabasePath).Should().BeFalse();
        Directory.Exists(Path.GetDirectoryName(memory.DatabasePath)).Should().BeFalse();
        File.Exists("memory").Should().BeFalse();
    }

    [Fact]
    public async Task List_ShouldShowBuiltInDefaultOnly_WhenNothingSaved()
    {
        var (code, output, _) = await RunAsync("mcp", "list");

        code.Should().Be(0);
        Lines(output).Should().Equal("default (built-in)", "  include: interpreters", "  host: (default)", "  port: (default)");
    }

    [Fact]
    public async Task List_ShouldShowProfilesInNameOrderWithBlankLines_WhenSeveralSaved()
    {
        await RunAsync("mcp", "save", "--name", "zeta", "--include", "strings/reverse", "interpreters/ook", "--host", "localhost", "--port", "7000");
        await RunAsync("mcp", "save", "--name", "alpha", "--include", "crypto");

        var (_, output, _) = await RunAsync("mcp", "list");

        Lines(output).Should().Equal(
            "alpha", "  include: crypto", "  host: (default)", "  port: (default)",
            "",
            "default (built-in)", "  include: interpreters", "  host: (default)", "  port: (default)",
            "",
            "zeta", "  include: strings/reverse, interpreters/ook", "  host: localhost", "  port: 7000");
    }

    [Fact]
    public async Task List_ShouldMarkStaleEntriesUnresolved_WhenEntryNoLongerResolves()
    {
        _profiles.Save(new McpProfile("stale", ["strings/reverse", "gone/away", "config"], null, null));

        var (_, output, _) = await RunAsync("mcp", "list");

        output.Should().Contain("  include: strings/reverse, gone/away (unresolved), config (unresolved)");
    }
}
