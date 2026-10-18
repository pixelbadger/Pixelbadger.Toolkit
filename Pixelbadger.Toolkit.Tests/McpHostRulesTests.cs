using FluentAssertions;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Tests;

public class McpHostRulesTests
{
    [Theory]
    [InlineData("localhost", true)]
    [InlineData("LOCALHOST", true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("127.0.0.2", true)]
    [InlineData("::1", true)]
    [InlineData("[::1]", true)]
    [InlineData("0.0.0.0", false)]
    [InlineData("192.168.1.10", false)]
    [InlineData("example.com", false)]
    public void IsLoopback_ShouldClassifyBindHost_WhenGivenHostOrAddress(string host, bool expected)
    {
        McpHostRules.IsLoopback(host).Should().Be(expected);
    }

    [Theory]
    [InlineData("localhost", true)]
    [InlineData("LocalHost", true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("[::1]", true)]
    [InlineData("::1", true)]
    [InlineData("evil.example", false)]
    [InlineData("127.0.0.2", false)]
    [InlineData(null, false)]
    public void IsLoopbackHostName_ShouldClassifyRequestHost_WhenGivenHostHeaderName(string? hostName, bool expected)
    {
        McpHostRules.IsLoopbackHostName(hostName).Should().Be(expected);
    }

    [Theory]
    [InlineData("::1", "[::1]")]
    [InlineData("[::1]", "[::1]")]
    [InlineData("fe80::1", "[fe80::1]")]
    [InlineData("127.0.0.1", "127.0.0.1")]
    [InlineData("localhost", "localhost")]
    public void UrlHost_ShouldBracketIpv6Literals_WhenBuildingUrl(string host, string expected)
    {
        McpHostRules.UrlHost(host).Should().Be(expected);
    }

    [Fact]
    public void DefaultTimeout_ShouldBeThreeHundredSeconds_WhenReadFromExecutor()
    {
        McpToolExecutor.DefaultTimeout.Should().Be(TimeSpan.FromSeconds(300));
    }
}
