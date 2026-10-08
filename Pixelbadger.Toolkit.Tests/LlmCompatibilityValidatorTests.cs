using FluentAssertions;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Tests;

public class LlmCompatibilityValidatorTests
{
    private static readonly IReadOnlyList<string> Efforts = ["low", "medium", "high"];

    private static LlmCompatibilityValidator Create(string name = "openai", params LlmCapability[] capabilities)
        => new(new LlmProviderCapabilities(name, new HashSet<LlmCapability>(capabilities), Efforts));

    private static LlmActionRequirements Require(string? effort = null, params LlmCapability[] capabilities)
        => new("chat", new HashSet<LlmCapability>(capabilities), effort);

    [Fact]
    public void Validate_ShouldBeCompatible_WhenAllCapabilitiesPresent()
    {
        var validator = Create("openai", LlmCapability.TextChat, LlmCapability.ImageInput, LlmCapability.ImageGeneration);

        var result = validator.Validate(Require(null, LlmCapability.TextChat, LlmCapability.ImageInput));

        result.IsCompatible.Should().BeTrue();
        result.Error.Should().BeNull();
        result.NormalisedReasoningEffort.Should().BeNull();
    }

    [Fact]
    public void Validate_ShouldBeCompatible_WhenNoCapabilitiesRequired()
    {
        Create().Validate(Require()).IsCompatible.Should().BeTrue();
    }

    [Fact]
    public void Validate_ShouldReportMissingCapability_WhenSingleCapabilityMissing()
    {
        var validator = Create("claude", LlmCapability.TextChat);
        var requirements = new LlmActionRequirements("generate-image", new HashSet<LlmCapability> { LlmCapability.ImageGeneration });

        var result = validator.Validate(requirements);

        result.IsCompatible.Should().BeFalse();
        result.Error.Should().Be("The 'generate-image' action requires image generation which is not supported by the 'claude' provider.");
        result.NormalisedReasoningEffort.Should().BeNull();
    }

    [Fact]
    public void Validate_ShouldListAllMissingCapabilities_WhenMultipleMissing()
    {
        var validator = Create("test");

        var result = validator.Validate(Require(null, LlmCapability.ImageInput, LlmCapability.TextChat));

        result.IsCompatible.Should().BeFalse();
        result.Error.Should().Contain("text chat").And.Contain("image input");
        result.Error.Should().Contain("'chat' action").And.Contain("'test' provider");
    }

    [Fact]
    public void Validate_ShouldReportCapabilityBeforeEffort_WhenBothInvalid()
    {
        var result = Create("p").Validate(Require("ultra", LlmCapability.TextChat));

        result.Error.Should().Contain("requires text chat");
    }

    [Fact]
    public void Validate_ShouldReturnNullEffort_WhenEffortIsNull()
    {
        var result = Create().Validate(Require(null));

        result.IsCompatible.Should().BeTrue();
        result.NormalisedReasoningEffort.Should().BeNull();
    }

    [Theory]
    [InlineData("low")]
    [InlineData("medium")]
    [InlineData("high")]
    public void Validate_ShouldAcceptEffort_WhenValid(string effort)
    {
        var result = Create().Validate(Require(effort));

        result.IsCompatible.Should().BeTrue();
        result.NormalisedReasoningEffort.Should().Be(effort);
    }

    [Theory]
    [InlineData("LOW", "low")]
    [InlineData("Medium", "medium")]
    [InlineData("HIGH", "high")]
    public void Validate_ShouldNormaliseEffortToCanonicalValue_WhenCaseDiffers(string effort, string expected)
    {
        var result = Create().Validate(Require(effort));

        result.IsCompatible.Should().BeTrue();
        result.NormalisedReasoningEffort.Should().Be(expected);
    }

    [Theory]
    [InlineData("ultra")]
    [InlineData("extreme")]
    [InlineData("")]
    public void Validate_ShouldRejectEffort_WhenInvalid(string effort)
    {
        var result = Create("openai").Validate(Require(effort));

        result.IsCompatible.Should().BeFalse();
        result.NormalisedReasoningEffort.Should().BeNull();
        result.Error.Should().Be($"Invalid reasoning effort '{effort}' for provider 'openai'. Supported values: low, medium, high");
    }

    [Fact]
    public void Validate_ShouldIncludeProviderName_WhenEffortInvalid()
    {
        var result = Create("someprovider").Validate(Require("ultra"));

        result.Error.Should().Contain("'someprovider'");
    }
}
