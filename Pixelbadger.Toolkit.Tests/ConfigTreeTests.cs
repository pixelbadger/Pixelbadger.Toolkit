using System.CommandLine;
using FluentAssertions;
using Pixelbadger.Toolkit.CommandLine;
using Pixelbadger.Toolkit.Commands;

namespace Pixelbadger.Toolkit.Tests;

public class ConfigTreeTests
{
    [Fact]
    public void Build_ShouldExcludeRequiredHelpImmutableOptions_WhenWalkingTree()
    {
        var demo = new DemoTree();

        var tree = ConfigTree.Build(demo.Root);

        var run = tree.Find("demo", "run")!;
        run.Options.Keys.Should().BeEquivalentTo("count", "mode", "flag", "tags");
        run.Options.Keys.Should().NotContain(["name", "secret", "help", "version"]);
    }

    [Fact]
    public void Build_ShouldExcludeConfigTopic_WhenConfigAlreadyAdded()
    {
        var demo = new DemoTree();
        var config = new Command("config", "c") { new Option<string>("--zzz") };
        demo.Root.Add(config);

        var tree = ConfigTree.Build(demo.Root);

        tree.Topics.Select(t => t.Topic).Should().BeEquivalentTo("demo", "other");
    }

    [Fact]
    public void Build_ShouldSkipNodesWithoutConfigurableOptions_WhenNothingBeneath()
    {
        var demo = new DemoTree();

        var tree = ConfigTree.Build(demo.Root);

        tree.Find("demo", "empty").Should().BeNull();
        tree.Find("demo", "grp").Should().NotBeNull();
        tree.Find("demo", "grp one").Should().NotBeNull();
    }

    [Fact]
    public void Build_ShouldUnionOptionsByNameAcrossActions_WhenTopicLevel()
    {
        var demo = new DemoTree();

        var topic = ConfigTree.Build(demo.Root).Find("demo", "")!;

        topic.Options.Keys.Should().Contain(["color", "count", "mode", "flag", "tags"]);
        topic.Options["count"].Owners.Select(o => string.Join(' ', o.Path)).Should().BeEquivalentTo("run", "walk");
    }

    [Fact]
    public void Build_ShouldOmitConflictingValueTypesAtGroupButKeepBelow_WhenTypesDiffer()
    {
        var demo = new DemoTree();

        var tree = ConfigTree.Build(demo.Root);

        tree.Find("demo", "grp")!.Options.Should().NotContainKey("level");
        tree.Find("demo", "")!.Options.Should().NotContainKey("level");
        tree.Find("demo", "grp one")!.Options.Should().ContainKey("level");
        tree.Find("demo", "grp two")!.Options.Should().ContainKey("level");
    }

    [Fact]
    public void Build_ShouldThrow_WhenConfigurableOptionNamedUnset()
    {
        var root = new RootCommand();
        var topic = new Command("t", "t");
        var action = new Command("a", "a") { new Option<string>("--unset") };
        topic.Add(action);
        root.Add(topic);

        var act = () => ConfigTree.Build(root);

        act.Should().Throw<InvalidOperationException>().WithMessage("*unset*");
    }

    [Fact]
    public void Build_ShouldNotThrow_WhenUnsetOptionIsRequired()
    {
        var root = new RootCommand();
        var topic = new Command("t", "t") { new Option<string>("--unset") { Required = true } };
        root.Add(topic);

        var act = () => ConfigTree.Build(root);

        act.Should().NotThrow();
    }

    [Fact]
    public void AddConfigTopic_ShouldMirrorTreeAndClonesWithoutDefaults_WhenBuilt()
    {
        var demo = new DemoTree();
        ConfigBootstrap.AddConfigTopic(demo.Root, new InMemoryConfigStore());

        var config = demo.Root.Subcommands.Single(c => c.Name == "config");
        var demoNode = config.Subcommands.Single(c => c.Name == "demo");
        var grp = demoNode.Subcommands.Single(c => c.Name == "grp");

        config.Subcommands.Select(c => c.Name).Should().BeEquivalentTo("demo", "other");
        demoNode.Subcommands.Select(c => c.Name).Should().BeEquivalentTo("run", "walk", "grp");
        grp.Subcommands.Select(c => c.Name).Should().BeEquivalentTo("one", "two");

        var count = demoNode.Options.Single(o => o.Name == "--count");
        count.ValueType.Should().Be(typeof(int));
        count.Description.Should().Be("Count (default for: run, walk)");
        ((Option<int>)count).DefaultValueFactory.Should().BeNull();
        demoNode.Options.Single(o => o.Name == "--tags").AllowMultipleArgumentsPerToken.Should().BeTrue();
        demoNode.Options.Should().Contain(o => o.Name == "--unset");
        grp.Options.Select(o => o.Name).Should().BeEquivalentTo(["--depth", "--unset"]);
    }

    [Fact]
    public void AddConfigTopic_ShouldThrow_WhenUnsetOptionExists()
    {
        var root = new RootCommand();
        var topic = new Command("t", "t");
        topic.Add(new Command("a", "a") { new Option<string>("--unset") });
        root.Add(topic);

        var act = () => ConfigBootstrap.AddConfigTopic(root, new InMemoryConfigStore());

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddConfigTopic_ShouldHideMarkedRealOptions_WhenUsingRealCommands()
    {
        var root = new RootCommand();
        root.Add(LlmCommand.Create());
        root.Add(GptCommand.Create());
        ConfigBootstrap.AddConfigTopic(root, new InMemoryConfigStore());

        var config = root.Subcommands.Single(c => c.Name == "config");
        var llm = config.Subcommands.Single(c => c.Name == "llm");
        var gptTrain = config.Subcommands.Single(c => c.Name == "gpt").Subcommands.Single(c => c.Name == "train");
        var chat = llm.Subcommands.Single(c => c.Name == "chat");
        var genImage = llm.Subcommands.Single(c => c.Name == "generate-image");

        llm.Options.Select(o => o.Name).Should().Contain(["--model", "--provider", "--reasoning-effort"]);
        llm.Options.Select(o => o.Name).Should().NotContain(["--session-id", "--overwrite", "--message"]);
        chat.Options.Select(o => o.Name).Should().NotContain("--session-id");
        genImage.Options.Select(o => o.Name).Should().NotContain("--overwrite");
        gptTrain.Options.Select(o => o.Name).Should().NotContain("--resume");
        gptTrain.Options.Select(o => o.Name).Should().Contain(["--steps", "--tokenizer"]);
        llm.Subcommands.Select(c => c.Name).Should().Contain("chat");
    }
}
