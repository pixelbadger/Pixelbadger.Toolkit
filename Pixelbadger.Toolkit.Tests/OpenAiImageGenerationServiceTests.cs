using FluentAssertions;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Tests;

[Collection("EnvironmentVariables")]
public class OpenAiImageGenerationServiceTests : IDisposable
{
    private readonly string? _originalKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");

    public void Dispose() => Environment.SetEnvironmentVariable("OPENAI_API_KEY", _originalKey);

    [Fact]
    public void Constructor_ShouldThrow_WhenApiKeyMissing()
    {
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);

        var act = () => new OpenAiImageGenerationService(new LlmModelOptions(null));

        act.Should().Throw<InvalidOperationException>().WithMessage("*OPENAI_API_KEY*");
    }

    [Fact]
    public void Constructor_ShouldSucceed_WhenApiKeyPresent()
    {
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");

        var act = () => new OpenAiImageGenerationService(new LlmModelOptions("custom-model"));

        act.Should().NotThrow();
    }

    [Fact]
    public void DefaultModel_ShouldBeGptImage1()
    {
        OpenAiImageGenerationService.DefaultModel.Should().Be("gpt-image-1");
    }
}
