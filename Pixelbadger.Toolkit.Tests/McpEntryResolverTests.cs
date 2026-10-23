using FluentAssertions;
using Pixelbadger.Toolkit.Commands;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Tests;

public class McpEntryResolverTests
{
    private readonly System.CommandLine.RootCommand _root = ToolkitCommandTree.Create(new InMemoryConfigStore());

    private static string[] Names(IEnumerable<System.CommandLine.Command> commands) =>
        commands.Select(c => McpToolCatalogue.Describe(c).Name).ToArray();

    [Fact]
    public void Resolve_ShouldReturnAllActions_WhenEntryIsTopic()
    {
        var tools = McpEntryResolver.Resolve(_root, "interpreters");

        Names(tools!).Should().Equal("interpreters_brainfuck", "interpreters_ook", "interpreters_bf-to-ook");
    }

    [Fact]
    public void Resolve_ShouldReturnSingleAction_WhenEntryIsTopicAction()
    {
        var tools = McpEntryResolver.Resolve(_root, "strings/reverse");

        Names(tools!).Should().Equal("strings_reverse");
    }

    [Fact]
    public void Resolve_ShouldReturnNestedAction_WhenEntryIsThreeSegments()
    {
        Names(McpEntryResolver.Resolve(_root, "llm/history/list")!).Should().Equal("llm_history_list");
    }

    [Fact]
    public void Resolve_ShouldExpandSubtree_WhenEntryIsGroup()
    {
        Names(McpEntryResolver.Resolve(_root, "llm/history")!).Should().Equal("llm_history_list", "llm_history_delete");
    }

    [Fact]
    public void Resolve_ShouldSkipExcludedDescendants_WhenTopicContainsThem()
    {
        var oauth = Names(McpEntryResolver.Resolve(_root, "oauth")!);

        oauth.Should().Equal("oauth_profile_delete");
        McpEntryResolver.Resolve(_root, "web").Should().BeNull();
    }

    [Theory]
    [InlineData("nosuch")]
    [InlineData("strings/nosuch")]
    [InlineData("Strings")]
    [InlineData("")]
    [InlineData("strings/")]
    [InlineData("config")]
    [InlineData("config/llm")]
    [InlineData("demoscene")]
    [InlineData("demoscene/showcase")]
    [InlineData("web")]
    [InlineData("web/serve-html")]
    [InlineData("sqlite")]
    [InlineData("sqlite/tui")]
    [InlineData("oauth/token")]
    [InlineData("oauth/profile/add")]
    [InlineData("oauth/profile/update")]
    [InlineData("mcp")]
    [InlineData("mcp/serve")]
    [InlineData("mcp/list")]
    public void Resolve_ShouldReturnNull_WhenEntryIsUnknownOrExcluded(string entry)
    {
        McpEntryResolver.Resolve(_root, entry).Should().BeNull();
    }

    [Fact]
    public void ResolveAll_ShouldDeduplicateKeepingTreeOrder_WhenEntriesOverlap()
    {
        var resolution = McpEntryResolver.ResolveAll(_root, ["interpreters/ook", "interpreters", "interpreters/brainfuck"]);

        Names(resolution.Tools).Should().Equal("interpreters_ook", "interpreters_brainfuck", "interpreters_bf-to-ook");
        resolution.Unresolved.Should().BeEmpty();
    }

    [Fact]
    public void ResolveAll_ShouldReportUnresolvedEntries_WhenSomeAreInvalid()
    {
        var resolution = McpEntryResolver.ResolveAll(_root, ["interpreters/ook", "gone", "config"]);

        Names(resolution.Tools).Should().Equal("interpreters_ook");
        resolution.Unresolved.Should().Equal("gone", "config");
    }
}
