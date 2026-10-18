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

    private Task<(int Code, string Out, string Err)> RunAsync(params string[] args) =>
        McpCommandTestSupport.RunAsync(_store, args);

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
