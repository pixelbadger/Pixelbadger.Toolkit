using System.CommandLine;
using System.Text.RegularExpressions;
using FluentAssertions;
using Pixelbadger.Toolkit.CommandLine;
using Pixelbadger.Toolkit.Commands;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Tests;

public class LlmIfCommandTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public LlmIfCommandTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private static string Normalise(string s) => Regex.Replace(s, @"\s+", " ");

    private string CreateFile(string name)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, "x");
        return path;
    }

    [Fact]
    public void Parse_ShouldApplyDefaults_WhenOnlyGameProvided()
    {
        var result = LlmCommand.CreateIfCommand().Parse(new[] { "--game", "g.z8" });

        result.Errors.Should().BeEmpty();
        result.GetValue<string>("--game").Should().Be("g.z8");
        result.GetValue<bool>("--new").Should().BeFalse();
        result.GetValue<int>("--max-steps").Should().Be(30);
        result.GetValue<string?>("--instructions").Should().BeNull();
        result.GetValue<string?>("--interpreter").Should().BeNull();
        result.GetValue<string?>("--model").Should().BeNull();
        result.GetValue<string>("--provider").Should().Be("openai");
        result.GetValue<string?>("--reasoning-effort").Should().BeNull();
    }

    [Fact]
    public void Create_ShouldDeclareEveryOption_WhenBuilt()
    {
        var command = LlmCommand.CreateIfCommand();

        command.Options.Select(o => o.Name).Should().Contain(
            ["--game", "--new", "--max-steps", "--instructions", "--interpreter", "--model", "--provider", "--reasoning-effort"]);
        command.Description.Should().Be("Let an LLM play a Z-machine interactive fiction game through dfrotz in an Inform-style TUI");
        command.Options.Single(o => o.Name == "--game").Required.Should().BeTrue();
        command.Options.Single(o => o.Name == "--new").HasImmutableDefault().Should().BeTrue();
    }

    [Fact]
    public void Create_ShouldRegisterIfAction_OnLlmTopic()
    {
        LlmCommand.Create().Subcommands.Select(c => c.Name).Should().Contain("if");
    }

    [Fact]
    public void Parse_ShouldReportError_WhenGameMissing()
    {
        LlmCommand.CreateIfCommand().Parse(new[] { "--max-steps", "5" }).Errors.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("1")]
    [InlineData("500")]
    public void Parse_ShouldAcceptMaxSteps_WhenWithinRange(string value)
    {
        LlmCommand.CreateIfCommand().Parse(new[] { "--game", "g", "--max-steps", value }).Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("501")]
    public void Parse_ShouldReportError_WhenMaxStepsOutOfRange(string value)
    {
        var result = LlmCommand.CreateIfCommand().Parse(new[] { "--game", "g", "--max-steps", value });

        result.Errors.Should().Contain(e => e.Message.Contains("--max-steps must be between 1 and 500."));
    }

    [Fact]
    public async Task If_ShouldListEveryOption_InHelp()
    {
        var (exitCode, stdout, stderr) = await ToolkitProcess.RunAsync(null, "llm", "if", "--help");

        exitCode.Should().Be(0);
        var output = Normalise(stdout + stderr);
        foreach (var option in new[] { "--game", "--new", "--max-steps", "--instructions", "--interpreter", "--model", "--provider", "--reasoning-effort" })
            output.Should().Contain(option);
    }

    [Fact]
    public async Task If_ShouldFailWithProviderError_BeforeAnyFileChecks_WhenEffortUnsupported()
    {
        var (exitCode, stdout, stderr) = await ToolkitProcess.RunAsync(null, "llm", "if", "--game", "/nonexistent.z8", "--reasoning-effort", "xhigh");

        exitCode.Should().Be(1);
        var output = Normalise(stdout + stderr);
        output.Should().Contain("Invalid reasoning effort 'xhigh' for provider 'openai'. Supported values: low, medium, high");
        output.Should().NotContain("Game file not found");
    }

    [Fact]
    public async Task If_ShouldReportMissingGame_WhenFileDoesNotExist()
    {
        var (exitCode, stdout, stderr) = await ToolkitProcess.RunAsync(null, "llm", "if", "--game", "/nonexistent.z8");

        exitCode.Should().Be(1);
        Normalise(stdout + stderr).Should().Contain("Error: Game file not found: /nonexistent.z8");
    }

    [Fact]
    public async Task If_ShouldReportMissingInterpreter_BeforeInteractivityCheck()
    {
        var game = CreateFile("game.z8");

        var (exitCode, stdout, stderr) = await ToolkitProcess.RunAsync(null, "llm", "if", "--game", game, "--interpreter", Path.Combine(_dir, "no-dfrotz"));

        exitCode.Should().Be(1);
        var output = Normalise(stdout + stderr);
        output.Should().Contain("Error: dfrotz not found. Install frotz (e.g. apt install frotz) or pass --interpreter.");
        output.Should().NotContain("interactive terminal");
    }

    [Fact]
    public async Task If_ShouldReportNonInteractiveTerminal_WhenOutputRedirected()
    {
        var game = CreateFile("game.z8");
        var interpreter = CreateFile("fake-dfrotz");

        var (exitCode, stdout, stderr) = await ToolkitProcess.RunAsync(null, "llm", "if", "--game", game, "--interpreter", interpreter);

        exitCode.Should().Be(1);
        Normalise(stdout + stderr).Should().Contain("Error: llm if requires an interactive terminal");
    }

    [Fact]
    public async Task Invoke_ShouldNotConsultInteractivity_WhenGameMissing()
    {
        var called = false;
        var command = LlmCommand.CreateIfCommand(() => { called = true; return true; });

        var exitCode = await command.Parse(new[] { "--game", Path.Combine(_dir, "missing.z8") }).InvokeAsync();

        exitCode.Should().Be(1);
        called.Should().BeFalse();
    }

    [Fact]
    public async Task Invoke_ShouldExitOneWithoutOpeningStore_WhenNotInteractive()
    {
        var game = CreateFile("game.z8");
        var interpreter = CreateFile("fake-dfrotz");
        var called = false;
        var command = LlmCommand.CreateIfCommand(() => { called = true; return false; });

        var exitCode = await command.Parse(new[] { "--game", game, "--interpreter", interpreter }).InvokeAsync();

        exitCode.Should().Be(1);
        called.Should().BeTrue();
    }

    [Fact]
    public void Create_ShouldExcludeIfFromMcp_WhenResolvingEntries()
    {
        var root = ToolkitCommandTree.Create(new InMemoryConfigStore());

        LlmCommand.CreateIfCommand().IsExcludedFromMcp().Should().BeTrue();
        McpEntryResolver.Resolve(root, "llm/if").Should().BeNull();
        McpEntryResolver.Resolve(root, "llm")!.Select(c => c.Name).Should().Contain("paint").And.NotContain("if");
        McpEntryResolver.Resolve(root, "llm")!.Select(c => McpToolCatalogue.Describe(c).Name).Should().NotContain("llm_if");
    }

    [Fact]
    public void AddConfigTopic_ShouldExposeConfigurableIfOptionsOnly_WhenUsingRealCommands()
    {
        var root = new RootCommand();
        root.Add(LlmCommand.Create());
        ConfigBootstrap.AddConfigTopic(root, new InMemoryConfigStore());

        var ifConfig = root.Subcommands.Single(c => c.Name == "config")
            .Subcommands.Single(c => c.Name == "llm")
            .Subcommands.Single(c => c.Name == "if");

        ifConfig.Options.Select(o => o.Name).Should().Contain(
            ["--max-steps", "--instructions", "--interpreter", "--model", "--provider", "--reasoning-effort"]);
        ifConfig.Options.Select(o => o.Name).Should().NotContain(["--new", "--game"]);
    }
}
