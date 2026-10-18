using FluentAssertions;
using Pixelbadger.Toolkit.Commands;

namespace Pixelbadger.Toolkit.Tests;

public class ToolkitCommandTreeTests
{
    [Fact]
    public void Create_ShouldReturnDistinctTrees_WhenCalledTwice()
    {
        var store = new InMemoryConfigStore();

        var a = ToolkitCommandTree.Create(store);
        var b = ToolkitCommandTree.Create(store);

        a.Should().NotBeSameAs(b);
        a.Subcommands.Single(c => c.Name == "interpreters")
            .Should().NotBeSameAs(b.Subcommands.Single(c => c.Name == "interpreters"));
    }

    [Fact]
    public void Create_ShouldRegisterTopicsInOrderWithConfigLast_WhenBuilt()
    {
        var root = ToolkitCommandTree.Create(new InMemoryConfigStore());

        root.Subcommands.Select(c => c.Name).Should().Equal(
            "strings", "interpreters", "images", "web", "llm", "oauth", "crypto", "markov", "demoscene", "gpt", "config");
        root.Description.Should().Be("CLI toolkit exposing varied functionality organized by topic");
    }
}
