using System.CommandLine;
using FluentAssertions;
using Pixelbadger.Toolkit.CommandLine;
using Pixelbadger.Toolkit.Commands;

namespace Pixelbadger.Toolkit.Tests;

public class McpExposureExtensionsTests
{
    [Fact]
    public void ExcludeFromMcp_ShouldReturnSameCommand_WhenCalled()
    {
        var command = new Command("x");

        command.ExcludeFromMcp().Should().BeSameAs(command);
    }

    [Fact]
    public void IsExcludedFromMcp_ShouldBeTrueForMarkedAndDescendants_AndFalseForSiblings()
    {
        var parent = new Command("parent");
        var child = new Command("child");
        var grandchild = new Command("grandchild");
        var sibling = new Command("sibling");
        child.Add(grandchild);
        parent.Add(child);
        parent.Add(sibling);
        child.ExcludeFromMcp();

        child.IsExcludedFromMcp().Should().BeTrue();
        grandchild.IsExcludedFromMcp().Should().BeTrue();
        sibling.IsExcludedFromMcp().Should().BeFalse();
        parent.IsExcludedFromMcp().Should().BeFalse();
    }

    private static Command Find(RootCommand root, params string[] path)
    {
        Command current = root;
        foreach (var name in path)
            current = current.Subcommands.Single(c => c.Name == name);
        return current;
    }

    [Theory]
    [InlineData("config")]
    [InlineData("config", "llm")]
    [InlineData("demoscene")]
    [InlineData("demoscene", "showcase")]
    [InlineData("web", "serve-html")]
    [InlineData("oauth", "token")]
    [InlineData("oauth", "profile", "add")]
    [InlineData("oauth", "profile", "update")]
    public void IsExcludedFromMcp_ShouldBeTrue_WhenCommandIsNeverExposable(params string[] path)
    {
        var root = ToolkitCommandTree.Create(new InMemoryConfigStore());

        Find(root, path).IsExcludedFromMcp().Should().BeTrue();
    }

    [Theory]
    [InlineData("interpreters")]
    [InlineData("interpreters", "brainfuck")]
    [InlineData("oauth", "profile", "delete")]
    [InlineData("web")]
    [InlineData("strings", "reverse")]
    public void IsExcludedFromMcp_ShouldBeFalse_WhenCommandIsExposable(params string[] path)
    {
        var root = ToolkitCommandTree.Create(new InMemoryConfigStore());

        Find(root, path).IsExcludedFromMcp().Should().BeFalse();
    }
}
