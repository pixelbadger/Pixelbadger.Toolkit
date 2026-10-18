using FluentAssertions;
using Moq;
using Pixelbadger.Toolkit.CommandLine;
using Pixelbadger.Toolkit.Commands;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Services;
using Spectre.Console;

namespace Pixelbadger.Toolkit.Tests;

[Collection("AnsiConsole")]
public class McpServeComponentTests : IDisposable
{
    private readonly IAnsiConsole _originalConsole = AnsiConsole.Console;
    private readonly Mock<IMcpProfileStore> _profiles = new();
    private readonly Mock<IMcpServerHost> _host = new();
    private readonly InMemoryConfigStore _config = new();

    public void Dispose()
    {
        AnsiConsole.Console = _originalConsole;
        _config.Dispose();
    }

    private McpServeComponent Component() =>
        new(_profiles.Object, () => ToolkitCommandTree.Create(_config), _host.Object);

    [Fact]
    public async Task RunAsync_ShouldUseBuiltInDefaultAndBuiltInAddress_WhenNothingGiven()
    {
        McpServerSettings? settings = null;
        _host.Setup(h => h.RunAsync(It.IsAny<McpServerSettings>(), It.IsAny<Action>(), It.IsAny<CancellationToken>()))
            .Callback<McpServerSettings, Action, CancellationToken>((s, _, _) => settings = s).Returns(Task.CompletedTask);

        var code = await Component().RunAsync(null, null, null, CancellationToken.None);

        code.Should().Be(0);
        settings!.ProfileName.Should().Be("default");
        settings.Host.Should().Be("127.0.0.1");
        settings.Port.Should().Be(5100);
        settings.Tools.Select(t => t.Name).Should().Equal("interpreters_brainfuck", "interpreters_ook", "interpreters_bf-to-ook");
    }

    [Theory]
    [InlineData(null, null, "10.0.0.1", 7000, "10.0.0.1", 7000)]
    [InlineData("1.2.3.4", 9, "10.0.0.1", 7000, "1.2.3.4", 9)]
    [InlineData(null, 9, "10.0.0.1", null, "10.0.0.1", 9)]
    [InlineData("1.2.3.4", null, null, null, "1.2.3.4", 5100)]
    public async Task RunAsync_ShouldApplyFlagThenProfileThenBuiltInPrecedence_WhenResolvingAddress(
        string? flagHost, int? flagPort, string? profileHost, int? profilePort, string expectedHost, int expectedPort)
    {
        _profiles.Setup(p => p.Get("p")).Returns(new McpProfile("p", ["interpreters/ook"], profileHost, profilePort));
        McpServerSettings? settings = null;
        _host.Setup(h => h.RunAsync(It.IsAny<McpServerSettings>(), It.IsAny<Action>(), It.IsAny<CancellationToken>()))
            .Callback<McpServerSettings, Action, CancellationToken>((s, _, _) => settings = s).Returns(Task.CompletedTask);

        await Component().RunAsync("p", flagHost, flagPort, CancellationToken.None);

        settings!.Host.Should().Be(expectedHost);
        settings.Port.Should().Be(expectedPort);
    }

    [Fact]
    public async Task RunAsync_ShouldPrintServingAndToolsLines_WhenServerStarts()
    {
        _host.Setup(h => h.RunAsync(It.IsAny<McpServerSettings>(), It.IsAny<Action>(), It.IsAny<CancellationToken>()))
            .Callback<McpServerSettings, Action, CancellationToken>((_, started, _) => started()).Returns(Task.CompletedTask);
        var output = new StringWriter();
        var error = new StringWriter();
        ToolkitConsole.InstallRouting();

        int code;
        using (ToolkitConsole.Capture(ToolkitConsole.CreateCaptureConsole(output), output, error))
            code = await Component().RunAsync(null, "::1", 5111, CancellationToken.None);

        code.Should().Be(0);
        output.ToString().Replace("\r\n", "\n").Should().Be(
            "Serving MCP profile 'default' on http://[::1]:5111/mcp\n" +
            "Tools: interpreters_brainfuck, interpreters_ook, interpreters_bf-to-ook\n");
        error.ToString().Should().BeEmpty();
    }

    [Theory]
    [InlineData("0.0.0.0", true)]
    [InlineData("192.168.1.5", true)]
    [InlineData("example.com", true)]
    [InlineData("127.0.0.1", false)]
    [InlineData("localhost", false)]
    [InlineData("::1", false)]
    public async Task RunAsync_ShouldWarnOnStderr_WhenHostIsNotLoopback(string host, bool warns)
    {
        _host.Setup(h => h.RunAsync(It.IsAny<McpServerSettings>(), It.IsAny<Action>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var output = new StringWriter();
        var error = new StringWriter();
        ToolkitConsole.InstallRouting();

        using (ToolkitConsole.Capture(ToolkitConsole.CreateCaptureConsole(output), output, error))
            await Component().RunAsync(null, host, 5111, CancellationToken.None);

        if (warns)
            error.ToString().Trim().Should().Be($"Warning: binding to non-loopback address {host}; the MCP server has no authentication.");
        else
            error.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_ShouldReturnOneWithMessage_WhenServerFailsToBind()
    {
        _host.Setup(h => h.RunAsync(It.IsAny<McpServerSettings>(), It.IsAny<Action>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Failed to bind to address http://127.0.0.1:5100: address already in use."));
        var output = new StringWriter();
        var error = new StringWriter();
        ToolkitConsole.InstallRouting();

        int code;
        using (ToolkitConsole.Capture(ToolkitConsole.CreateCaptureConsole(output), output, error))
            code = await Component().RunAsync(null, null, null, CancellationToken.None);

        code.Should().Be(1);
        output.ToString().Should().Contain("Error: Failed to bind to address");
    }

    [Fact]
    public async Task RunAsync_ShouldReturnZero_WhenCancelledWhileServing()
    {
        _host.Setup(h => h.RunAsync(It.IsAny<McpServerSettings>(), It.IsAny<Action>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var code = await Component().RunAsync(null, null, null, new CancellationToken(true));

        code.Should().Be(0);
    }
}
