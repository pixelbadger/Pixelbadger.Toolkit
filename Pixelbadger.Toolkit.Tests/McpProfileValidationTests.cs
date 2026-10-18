using FluentAssertions;
using Pixelbadger.Toolkit.Components;

namespace Pixelbadger.Toolkit.Tests;

public class McpProfileValidationTests
{
    [Theory]
    [InlineData("text-tools")]
    [InlineData("default")]
    [InlineData("a")]
    [InlineData("0")]
    [InlineData("abc-123")]
    public void ValidateName_ShouldReturnNull_WhenNameMatchesPattern(string name)
    {
        McpProfileValidation.ValidateName(name).Should().BeNull();
    }

    [Theory]
    [InlineData("Bad_Name", "Invalid profile name 'Bad_Name'. Profile names must match ^[a-z0-9-]+$.")]
    [InlineData("", "Invalid profile name ''. Profile names must match ^[a-z0-9-]+$.")]
    [InlineData("a b", "Invalid profile name 'a b'. Profile names must match ^[a-z0-9-]+$.")]
    [InlineData("Bad", "Invalid profile name 'Bad'. Profile names must match ^[a-z0-9-]+$.")]
    [InlineData("Nope!", "Invalid profile name 'Nope!'. Profile names must match ^[a-z0-9-]+$.")]
    [InlineData("abc\n", "Invalid profile name 'abc\n'. Profile names must match ^[a-z0-9-]+$.")]
    public void ValidateName_ShouldReturnExactMessage_WhenNameDoesNotMatchPattern(string name, string message)
    {
        McpProfileValidation.ValidateName(name).Should().Be(message);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(65535)]
    [InlineData(8080)]
    public void ValidatePort_ShouldReturnNull_WhenPortIsWithinBounds(int port)
    {
        McpProfileValidation.ValidatePort(port).Should().BeNull();
    }

    [Fact]
    public void ValidatePort_ShouldReturnNull_WhenPortIsNotSpecified()
    {
        McpProfileValidation.ValidatePort(null).Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    [InlineData(70000)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void ValidatePort_ShouldReturnExactMessage_WhenPortIsOutOfBounds(int port)
    {
        McpProfileValidation.ValidatePort(port).Should().Be("Port must be between 1 and 65535.");
    }
}
