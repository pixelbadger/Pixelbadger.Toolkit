using System.CommandLine;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Pixelbadger.Toolkit.Commands;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Tests;

[Collection("EnvironmentVariables")]
public class ClaudeProviderCompositionTests : IDisposable
{
    private readonly string? _originalAnthropicKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
    private readonly string? _originalOpenAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", _originalAnthropicKey);
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", _originalOpenAiKey);
    }

    private static void ClearKeys()
    {
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", null);
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);
    }

    private static string Normalise(string output) => Regex.Replace(output, @"\s+", " ");

    [Fact]
    public void BuildLlmServices_ShouldResolveClaudeCapabilitiesAndValidator_WithoutApiKey()
    {
        ClearKeys();

        using var services = LlmCommand.BuildLlmServices("claude", null);

        var capabilities = services.GetRequiredService<LlmProviderCapabilities>();
        capabilities.ProviderName.Should().Be("claude");
        capabilities.Capabilities.Should().BeEquivalentTo(new[] { LlmCapability.TextChat, LlmCapability.ImageInput });
        capabilities.SupportedReasoningEfforts.Should().Equal("low", "medium", "high", "xhigh", "max");
        services.GetRequiredService<ILlmCompatibilityValidator>().Should().BeOfType<LlmCompatibilityValidator>();
    }

    [Fact]
    public void BuildLlmServices_ShouldResolveClaudeClientWithDefaultModel_WhenKeySet()
    {
        ClearKeys();
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "test-key");

        using var services = LlmCommand.BuildLlmServices("claude", null);

        services.GetRequiredService<ILlmClientService>().Should().BeOfType<ClaudeLlmClientService>();
        services.GetRequiredService<LlmModelOptions>().Model.Should().BeNull();
        ClaudeLlmClientService.DefaultModel.Should().Be("claude-sonnet-5-5");
    }

    [Fact]
    public void BuildLlmServices_ShouldPropagateModelOverride_ForClaude()
    {
        ClearKeys();
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "test-key");

        using var services = LlmCommand.BuildLlmServices("claude", "claude-custom");

        services.GetRequiredService<LlmModelOptions>().Model.Should().Be("claude-custom");
        services.GetRequiredService<ILlmClientService>().Should().BeOfType<ClaudeLlmClientService>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void BuildLlmServices_ShouldThrowOnResolve_WhenAnthropicKeyMissingOrEmpty(string? key)
    {
        ClearKeys();
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", key);

        using var services = LlmCommand.BuildLlmServices("claude", null);

        var act = () => services.GetRequiredService<ILlmClientService>();
        act.Should().Throw<InvalidOperationException>().WithMessage("ANTHROPIC_API_KEY environment variable is not set.");
    }

    [Fact]
    public void BuildLlmServices_ShouldNotRegisterImageGeneration_ForClaude()
    {
        ClearKeys();
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "test-key");

        using var services = LlmCommand.BuildLlmServices("claude", null);

        services.GetService<IImageGenerationService>().Should().BeNull();
    }

    [Theory]
    [InlineData("chat", "--message", "hi")]
    [InlineData("translate", "--text", "hi", "--target-language", "French")]
    [InlineData("ocaaar", "--image-path", "x.png")]
    [InlineData("corpospeak", "--source", "s", "--audience", "csuite")]
    [InlineData("generate-image", "--prompt", "p", "--out-file", "o.png")]
    public void Parse_ShouldAcceptClaudeProvider_OnAllActions(params string[] args)
    {
        var result = LlmCommand.Create().Parse(args.Concat(["--provider", "claude"]).ToArray());

        result.Errors.Should().BeEmpty();
        result.GetValue<string>("--provider").Should().Be("claude");
    }

    [Fact]
    public async Task GenerateImage_ShouldReportCapabilityError_NotApiKeyError_ForClaude()
    {
        ClearKeys();
        var outFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
        try
        {
            var (exitCode, stdout, stderr) = await ToolkitProcess.RunAsync(
                null, "llm", "generate-image", "--provider", "claude", "--prompt", "p", "--out-file", outFile);

            exitCode.Should().Be(1);
            var output = Normalise(stdout + stderr);
            output.Should().Contain("image generation");
            output.Should().Contain("not supported by the 'claude' provider");
            output.Should().NotContain("API_KEY");
            File.Exists(outFile).Should().BeFalse();
        }
        finally
        {
            if (File.Exists(outFile)) File.Delete(outFile);
        }
    }

    [Fact]
    public async Task Chat_ShouldAcceptXhighAndReportAnthropicKeyError_ForClaude()
    {
        ClearKeys();

        var (exitCode, stdout, stderr) = await ToolkitProcess.RunAsync(
            null, "llm", "chat", "--provider", "claude", "--message", "hi", "--reasoning-effort", "xhigh");

        exitCode.Should().Be(1);
        var output = Normalise(stdout + stderr);
        output.Should().Contain("ANTHROPIC_API_KEY environment variable is not set.");
        output.Should().NotContain("Invalid reasoning effort");
    }

    [Fact]
    public async Task Chat_ShouldReportEffortErrorListingClaudeValues_WhenEffortInvalid()
    {
        ClearKeys();

        var (exitCode, stdout, stderr) = await ToolkitProcess.RunAsync(
            null, "llm", "chat", "--provider", "claude", "--message", "hi", "--reasoning-effort", "ultra");

        exitCode.Should().Be(1);
        var output = Normalise(stdout + stderr);
        output.Should().Contain("Invalid reasoning effort 'ultra' for provider 'claude'. Supported values: low, medium, high, xhigh, max");
        output.Should().NotContain("API_KEY");
    }

    [Fact]
    public async Task Chat_ShouldRejectXhighEffort_ForOpenAi()
    {
        ClearKeys();

        var (exitCode, stdout, stderr) = await ToolkitProcess.RunAsync(
            null, "llm", "chat", "--provider", "openai", "--message", "hi", "--reasoning-effort", "xhigh");

        exitCode.Should().Be(1);
        var output = Normalise(stdout + stderr);
        output.Should().Contain("Invalid reasoning effort 'xhigh' for provider 'openai'. Supported values: low, medium, high");
        output.Should().NotContain("API_KEY");
    }
}
