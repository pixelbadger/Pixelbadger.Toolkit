using FluentAssertions;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Tests;

public class McpProfilesTests
{
    [Fact]
    public void BuiltInDefault_ShouldBeInterpretersTopic_WhenRead()
    {
        var p = McpProfiles.BuiltInDefault;
        p.Name.Should().Be("default");
        p.Include.Should().Equal("interpreters");
        p.Host.Should().BeNull();
        p.Port.Should().BeNull();
        McpProfiles.DefaultName.Should().Be("default");
    }

    [Theory]
    [InlineData("a", true)]
    [InlineData("my-profile-2", true)]
    [InlineData("default", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("Upper", false)]
    [InlineData("has space", false)]
    [InlineData("under_score", false)]
    [InlineData("a\n", false)]
    [InlineData("a/b", false)]
    public void IsValidName_ShouldMatchPattern_WhenGivenName(string? name, bool expected)
    {
        McpProfiles.IsValidName(name).Should().Be(expected);
    }
}
