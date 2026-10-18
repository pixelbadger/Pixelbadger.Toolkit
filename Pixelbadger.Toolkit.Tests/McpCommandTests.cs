using System.CommandLine;
using FluentAssertions;
using Pixelbadger.Toolkit.CommandLine;
using Pixelbadger.Toolkit.Commands;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Services;
using Spectre.Console;

namespace Pixelbadger.Toolkit.Tests;

[Collection("AnsiConsole")]
public class McpCommandTests : IDisposable
{
    private readonly string _dir;
    private readonly string _db;
    private readonly IAnsiConsole _originalConsole = AnsiConsole.Console;
    private readonly SqliteConfigStore _store;
    private readonly SqliteMcpProfileStore _profiles;

    public McpCommandTests()
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

    private async Task<(int Code, string Out, string Err)> RunAsync(params string[] args)
    {
        var root = ToolkitCommandTree.Create(_store);
        var output = new StringWriter();
        var error = new StringWriter();
        ToolkitConsole.InstallRouting();
        using (ToolkitConsole.Capture(ToolkitConsole.CreateCaptureConsole(output), output, error))
        {
            var code = await ConfigBootstrap.InvokeAsync(root, args, _store);
            return (code, output.ToString(), error.ToString());
        }
    }

    private static string[] Lines(string text) => text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');

    [Fact]
    public void Create_ShouldReturnMcpTopicExcludedFromMcp_WhenBuilt()
    {
        var command = McpCommand.Create(_store);

        command.Name.Should().Be("mcp");
        command.Description.Should().Be("Serve toolkit commands as MCP tools and manage MCP profiles");
        command.IsExcludedFromMcp().Should().BeTrue();
        command.Subcommands.Select(c => c.Name).Should().BeEquivalentTo("serve", "save", "delete", "list");
    }

    [Fact]
    public void Create_ShouldMarkOnlyProfileOptionConfigurable_WhenBuilt()
    {
        var serve = McpCommand.Create(_store).Subcommands.Single(c => c.Name == "serve");

        serve.Options.Single(o => o.Name == "--profile").HasImmutableDefault().Should().BeFalse();
        serve.Options.Single(o => o.Name == "--host").HasImmutableDefault().Should().BeTrue();
        serve.Options.Single(o => o.Name == "--port").HasImmutableDefault().Should().BeTrue();
        foreach (var name in new[] { "save", "delete" })
            McpCommand.Create(_store).Subcommands.Single(c => c.Name == name).Options
                .Where(o => o.Name is "--name" or "--include" or "--host" or "--port")
                .Should().OnlyContain(o => o.HasImmutableDefault());
    }

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

    [Fact]
    public async Task Save_ShouldFail_WhenIncludeIsMissing()
    {
        var root = ToolkitCommandTree.Create(_store);
        var parseOutput = new StringWriter();
        var parseError = new StringWriter();
        var configuration = new InvocationConfiguration { Output = parseOutput, Error = parseError };

        var code = await ConfigBootstrap.InvokeAsync(root, ["mcp", "save", "--name", "p"], _store, configuration);

        code.Should().NotBe(0);
        (parseOutput.ToString() + parseError.ToString()).Should().Contain("Option '--include' is required.");
        _profiles.GetAll().Should().BeEmpty();
    }

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

    [Fact]
    public async Task Serve_ShouldFailWithExitOne_WhenProfileIsUnknown()
    {
        var (code, _, output) = await RunAsync("mcp", "serve", "--profile", "ghost");

        code.Should().Be(1);
        output.Trim().Should().Be("Error: MCP profile 'ghost' not found.");
    }

    [Fact]
    public async Task Serve_ShouldFailWithExitOneAndWarn_WhenProfileResolvesToZeroTools()
    {
        _profiles.Save(new McpProfile("stale", ["gone/away"], null, null));

        var (code, _, err) = await RunAsync("mcp", "serve", "--profile", "stale");

        code.Should().Be(1);
        err.Should().Contain("Warning: skipping unresolved entry 'gone/away' in MCP profile 'stale'");
        err.Should().Contain("Error: MCP profile 'stale' exposes no tools.");
    }

    [Fact]
    public async Task Serve_ShouldFail_WhenPortOverrideIsOutOfRange()
    {
        var (code, _, output) = await RunAsync("mcp", "serve", "--port", "70000");

        code.Should().Be(1);
        output.Trim().Should().Be("Error: Port must be between 1 and 65535.");
    }

    [Fact]
    public async Task Config_ShouldAcceptProfileDefaultAndRejectHost_WhenConfiguringServe()
    {
        var (okCode, _, _) = await RunAsync("config", "mcp", "serve", "--profile", "ghost");
        var (code, _, output) = await RunAsync("mcp", "serve");
        var (hostCode, _, hostErr) = await RunAsync("config", "mcp", "serve", "--host", "0.0.0.0");

        okCode.Should().Be(0);
        code.Should().Be(1);
        output.Trim().Should().Be("Error: MCP profile 'ghost' not found.");
        hostCode.Should().NotBe(0, hostErr);
    }
}
