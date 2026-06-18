using FluentAssertions;
using Pixelbadger.Toolkit.Commands;

namespace Pixelbadger.Toolkit.Tests;

public class LlmCommandReasoningEffortTests
{
    private static readonly IReadOnlyList<string> OpenAiEfforts = ["low", "medium", "high"];

    [Fact]
    public void TryValidateReasoningEffort_ShouldReturnTrue_WhenEffortIsNull()
    {
        var result = LlmCommand.TryValidateReasoningEffort(null, OpenAiEfforts, out var normalised);

        result.Should().BeTrue();
        normalised.Should().BeNull();
    }

    [Theory]
    [InlineData("low")]
    [InlineData("medium")]
    [InlineData("high")]
    public void TryValidateReasoningEffort_ShouldReturnTrue_ForValidEffort(string effort)
    {
        var result = LlmCommand.TryValidateReasoningEffort(effort, OpenAiEfforts, out var normalised);

        result.Should().BeTrue();
        normalised.Should().Be(effort);
    }

    [Theory]
    [InlineData("LOW")]
    [InlineData("Medium")]
    [InlineData("HIGH")]
    public void TryValidateReasoningEffort_ShouldBeCaseInsensitive(string effort)
    {
        var result = LlmCommand.TryValidateReasoningEffort(effort, OpenAiEfforts, out var normalised);

        result.Should().BeTrue();
        normalised.Should().Be(effort.ToLowerInvariant());
    }

    [Theory]
    [InlineData("ultra")]
    [InlineData("extreme")]
    [InlineData("")]
    public void TryValidateReasoningEffort_ShouldReturnFalse_ForInvalidEffort(string effort)
    {
        var result = LlmCommand.TryValidateReasoningEffort(effort, OpenAiEfforts, out var normalised);

        result.Should().BeFalse();
        normalised.Should().BeNull();
    }

    [Fact]
    public void TryValidateReasoningEffort_ShouldNormaliseToLowercase()
    {
        LlmCommand.TryValidateReasoningEffort("HIGH", OpenAiEfforts, out var normalised);

        normalised.Should().Be("high");
    }
}
