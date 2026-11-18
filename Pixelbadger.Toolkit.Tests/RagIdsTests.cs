using FluentAssertions;
using Pixelbadger.Toolkit.Services.Rag;
using Xunit;

namespace Pixelbadger.Toolkit.Tests;

public class RagIdsTests
{
    [Fact]
    public void NewId_ShouldReturn32LowercaseHexCharacters_WhenCalled()
    {
        var id = RagIds.NewId();
        id.Should().MatchRegex("^[0-9a-f]{32}$");
        RagIds.NewId().Should().NotBe(id);
    }

    [Fact]
    public void TryNormalize_ShouldLowercase_WhenUppercaseHexProvided()
    {
        RagIds.TryNormalize("ABCDEF0123456789ABCDEF0123456789", out var id).Should().BeTrue();
        id.Should().Be("abcdef0123456789abcdef0123456789");
    }

    [Fact]
    public void TryNormalize_ShouldAcceptGeneratedId_WhenRoundTripped()
    {
        var generated = RagIds.NewId();
        RagIds.TryNormalize(generated, out var id).Should().BeTrue();
        id.Should().Be(generated);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("abcdef0123456789abcdef012345678")]
    [InlineData("abcdef0123456789abcdef01234567890")]
    [InlineData("abcdef0123456789abcdef012345678g")]
    [InlineData("abcdef01-2345-6789-abcd-ef0123456789")]
    [InlineData(" bcdef0123456789abcdef0123456789")]
    public void TryNormalize_ShouldReturnFalse_WhenMalformed(string? input)
    {
        RagIds.TryNormalize(input, out var id).Should().BeFalse();
        id.Should().BeEmpty();
    }
}
