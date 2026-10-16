using System.CommandLine;
using System.Text.RegularExpressions;
using FluentAssertions;
using Pixelbadger.Toolkit.Commands;

namespace Pixelbadger.Toolkit.Tests;

[Collection("EnvironmentVariables")]
public class LlmPaintCommandTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public LlmPaintCommandTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private static string Normalise(string s) => Regex.Replace(s, @"\s+", " ");

    [Fact]
    public void Parse_ShouldApplyDefaults_WhenOnlyPromptProvided()
    {
        var result = LlmCommand.CreatePaintCommand().Parse(new[] { "--prompt", "A pixelbadger" });

        result.Errors.Should().BeEmpty();
        result.GetValue<int>("--max-steps").Should().Be(40);
        result.GetValue<string>("--provider").Should().Be("openai");
        result.GetValue<string?>("--out-file").Should().BeNull();
        result.GetValue<bool>("--overwrite").Should().BeFalse();
    }

    [Theory]
    [InlineData("1")]
    [InlineData("200")]
    public void Parse_ShouldAcceptMaxSteps_WhenWithinRange(string value)
    {
        LlmCommand.CreatePaintCommand().Parse(new[] { "--prompt", "p", "--max-steps", value }).Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("201")]
    public void Parse_ShouldReportError_WhenMaxStepsOutOfRange(string value)
    {
        var result = LlmCommand.CreatePaintCommand().Parse(new[] { "--prompt", "p", "--max-steps", value });

        result.Errors.Should().Contain(e => e.Message.Contains("--max-steps must be between 1 and 200"));
    }

    [Fact]
    public void Parse_ShouldReportError_WhenPromptMissing()
    {
        LlmCommand.CreatePaintCommand().Parse(new[] { "--max-steps", "5" }).Errors.Should().NotBeEmpty();
    }

    [Fact]
    public void Create_ShouldRegisterPaintAction_OnLlmTopic()
    {
        LlmCommand.Create().Subcommands.Select(c => c.Name).Should().Contain("paint");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("201")]
    public async Task Paint_ShouldFailWithMessageAndNoKeyLookup_WhenMaxStepsOutOfRange(string value)
    {
        var (exitCode, stdout, stderr) = await ToolkitProcess.RunAsync(null, "llm", "paint", "--prompt", "p", "--max-steps", value);

        exitCode.Should().Be(1);
        var output = Normalise(stdout + stderr);
        output.Should().Contain("--max-steps must be between 1 and 200");
        output.Should().NotContain("API_KEY");
    }

    [Fact]
    public async Task Paint_ShouldFail_WhenPromptMissing()
    {
        var (exitCode, stdout, stderr) = await ToolkitProcess.RunAsync(null, "llm", "paint");

        exitCode.Should().NotBe(0);
        Normalise(stdout + stderr).Should().Contain("--prompt");
    }

    [Fact]
    public async Task Paint_ShouldReportMissingOpenAiKey_WhenKeyUnset()
    {
        var (exitCode, stdout, stderr) = await RunWithoutKeys("llm", "paint", "--prompt", "p");

        exitCode.Should().Be(1);
        Normalise(stdout + stderr).Should().Contain("Error: OPENAI_API_KEY environment variable is not set.");
    }

    [Fact]
    public async Task Paint_ShouldReportMissingAnthropicKey_WhenKeyUnsetForClaude()
    {
        var (exitCode, stdout, stderr) = await RunWithoutKeys("llm", "paint", "--prompt", "p", "--provider", "claude");

        exitCode.Should().Be(1);
        Normalise(stdout + stderr).Should().Contain("Error: ANTHROPIC_API_KEY environment variable is not set.");
    }

    [Fact]
    public async Task Paint_ShouldRefuseExistingOutFile_WhenOverwriteNotSet()
    {
        var outFile = Path.Combine(_dir, "exists.png");
        File.WriteAllText(outFile, "keep");

        var (exitCode, stdout, stderr) = await RunWithKeys("llm", "paint", "--prompt", "p", "--out-file", outFile);

        exitCode.Should().Be(1);
        Normalise(stdout + stderr).Should().Contain("Output file already exists:").And.Contain("Use --overwrite to replace it.");
        File.ReadAllText(outFile).Should().Be("keep");
    }

    private static Task<(int, string, string)> RunWithoutKeys(params string[] args)
        => RunWithEnv(null, null, args);

    private static Task<(int, string, string)> RunWithKeys(params string[] args)
        => RunWithEnv("dummy-key", "dummy-key", args);

    private static async Task<(int, string, string)> RunWithEnv(string? openAi, string? anthropic, string[] args)
    {
        var old1 = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var old2 = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", openAi);
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", anthropic);
            return await ToolkitProcess.RunAsync(null, args);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", old1);
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", old2);
        }
    }
}
