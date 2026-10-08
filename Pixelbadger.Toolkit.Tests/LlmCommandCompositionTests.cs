using System.CommandLine;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Pixelbadger.Toolkit.Commands;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Tests;

[Collection("EnvironmentVariables")]
public class LlmCommandCompositionTests : IDisposable
{
    private readonly string? _originalKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");

    public void Dispose() => Environment.SetEnvironmentVariable("OPENAI_API_KEY", _originalKey);

    [Fact]
    public void BuildLlmServices_ShouldResolveCapabilitiesAndValidator_WithoutApiKey()
    {
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);

        using var services = LlmCommand.BuildLlmServices("openai", null);

        var capabilities = services.GetRequiredService<LlmProviderCapabilities>();
        capabilities.ProviderName.Should().Be("openai");
        capabilities.Capabilities.Should().BeEquivalentTo(new[] { LlmCapability.TextChat, LlmCapability.ImageInput, LlmCapability.ImageGeneration });
        capabilities.SupportedReasoningEfforts.Should().Equal("low", "medium", "high");
        services.GetRequiredService<ILlmCompatibilityValidator>().Should().BeOfType<LlmCompatibilityValidator>();
    }

    [Fact]
    public void BuildLlmServices_ShouldResolveOpenAiServices_WhenApiKeySet()
    {
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");

        using var services = LlmCommand.BuildLlmServices("openai", null);

        services.GetRequiredService<ILlmClientService>().Should().BeOfType<OpenAiLlmClientService>();
        services.GetRequiredService<IImageGenerationService>().Should().BeOfType<OpenAiImageGenerationService>();
    }

    [Fact]
    public void BuildLlmServices_ShouldThrowOnResolve_WhenApiKeyMissing()
    {
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);

        using var services = LlmCommand.BuildLlmServices("openai", null);

        var act = () => services.GetRequiredService<ILlmClientService>();
        act.Should().Throw<InvalidOperationException>().WithMessage("OPENAI_API_KEY environment variable is not set.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("custom-model")]
    public void BuildLlmServices_ShouldPropagateModelOption(string? model)
    {
        using var services = LlmCommand.BuildLlmServices("openai", model);

        services.GetRequiredService<LlmModelOptions>().Model.Should().Be(model);
    }

    [Fact]
    public void BuildLlmServices_ShouldResolveComponents_WhenApiKeySet()
    {
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");

        using var services = LlmCommand.BuildLlmServices("openai", null);

        services.GetRequiredService<ChatComponent>().Should().NotBeNull();
        services.GetRequiredService<TranslateComponent>().Should().NotBeNull();
        services.GetRequiredService<OcaaarComponent>().Should().NotBeNull();
        services.GetRequiredService<CorpospeakComponent>().Should().NotBeNull();
        services.GetRequiredService<GenerateImageComponent>().Should().NotBeNull();
    }

    [Theory]
    [InlineData("bogus")]
    [InlineData("")]
    [InlineData("OpenAI")]
    public void BuildLlmServices_ShouldThrow_WhenProviderUnknown(string provider)
    {
        var act = () => LlmCommand.BuildLlmServices(provider, null);

        act.Should().Throw<ArgumentException>().WithMessage("*Unknown provider*");
    }

    [Theory]
    [InlineData("chat", "--message", "hi")]
    [InlineData("translate", "--text", "hi", "--target-language", "French")]
    [InlineData("ocaaar", "--image-path", "x.png")]
    [InlineData("corpospeak", "--source", "s", "--audience", "csuite")]
    [InlineData("generate-image", "--prompt", "p", "--out-file", "o.png")]
    public void Parse_ShouldDefaultProviderToOpenAiAndModelToNull(params string[] args)
    {
        var result = LlmCommand.Create().Parse(args);

        result.Errors.Should().BeEmpty();
        result.GetValue<string>("--provider").Should().Be("openai");
        result.GetValue<string?>("--model").Should().BeNull();
    }

    [Theory]
    [InlineData("chat", "--message", "hi")]
    [InlineData("translate", "--text", "hi", "--target-language", "French")]
    [InlineData("ocaaar", "--image-path", "x.png")]
    [InlineData("corpospeak", "--source", "s", "--audience", "csuite")]
    [InlineData("generate-image", "--prompt", "p", "--out-file", "o.png")]
    public void Parse_ShouldReportError_WhenProviderUnknown(params string[] args)
    {
        var result = LlmCommand.Create().Parse(args.Concat(["--provider", "bogus"]).ToArray());

        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Chat_ShouldReportReasoningEffortError_NotApiKeyError_WhenBothInvalid()
    {
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);

        var (exitCode, stdout, stderr) = await ToolkitProcess.RunAsync(
            null, "llm", "chat", "--message", "hi", "--reasoning-effort", "ultra");

        exitCode.Should().Be(1);
        // Spectre.Console wraps long lines, so compare with whitespace collapsed.
        var output = System.Text.RegularExpressions.Regex.Replace(stdout + stderr, @"\s+", " ");
        output.Should().Contain("Invalid reasoning effort 'ultra' for provider 'openai'. Supported values: low, medium, high");
        output.Should().NotContain("OPENAI_API_KEY");
    }

    [Fact]
    public async Task Chat_ShouldReportApiKeyError_WhenEffortValidAndKeyMissing()
    {
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);

        var (exitCode, stdout, stderr) = await ToolkitProcess.RunAsync(
            null, "llm", "chat", "--message", "hi", "--reasoning-effort", "HIGH");

        exitCode.Should().Be(1);
        (stdout + stderr).Should().Contain("OPENAI_API_KEY");
    }

    [Fact]
    public async Task Chat_ShouldFailParse_WhenProviderUnknown()
    {
        var (exitCode, _, _) = await ToolkitProcess.RunAsync(null, "llm", "chat", "--message", "hi", "--provider", "bogus");

        exitCode.Should().NotBe(0);
    }
}
