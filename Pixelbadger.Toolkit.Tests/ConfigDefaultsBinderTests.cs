using System.CommandLine;
using FluentAssertions;
using Pixelbadger.Toolkit.CommandLine;
using Pixelbadger.Toolkit.Commands;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Tests;

public class ConfigDefaultsBinderTests
{
    private readonly DemoTree _demo = new();
    private readonly InMemoryConfigStore _store = new();
    private readonly StringWriter _warnings = new();

    private ParseResult Parse(params string[] args) =>
        ConfigBootstrap.ParseWithConfig(_demo.Root, args, _store, _warnings);

    [Fact]
    public void ParseWithConfig_ShouldUseBuiltInDefault_WhenNothingConfigured()
    {
        var parse = Parse("demo", "run", "--name", "n");

        parse.GetValue(_demo.RunCount).Should().Be(3);
        parse.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ParseWithConfig_ShouldApplyTopicDefault_WhenOnlyTopicRowExists()
    {
        _store.Seed("demo", "", "count", "10");

        Parse("demo", "run", "--name", "n").GetValue(_demo.RunCount).Should().Be(10);
        Parse("demo", "walk").GetValue(_demo.WalkCount).Should().Be(10);
    }

    [Fact]
    public void ParseWithConfig_ShouldPreferActionOverTopic_WhenBothConfigured()
    {
        _store.Seed("demo", "", "count", "10");
        _store.Seed("demo", "run", "count", "20");

        Parse("demo", "run", "--name", "n").GetValue(_demo.RunCount).Should().Be(20);
        Parse("demo", "walk").GetValue(_demo.WalkCount).Should().Be(10);
    }

    [Fact]
    public void ParseWithConfig_ShouldPreferActionOverGroupOverTopic_WhenAllThreeLevelsConfigured()
    {
        _store.Seed("demo", "", "depth", "1");
        _store.Seed("demo", "grp", "depth", "2");
        _store.Seed("demo", "grp one", "depth", "3");

        Parse("demo", "grp", "one").GetValue(_demo.OneDepth).Should().Be(3);
        Parse("demo", "grp", "two").GetValue(_demo.TwoDepth).Should().Be(2);
    }

    [Fact]
    public void ParseWithConfig_ShouldFallBackToTopic_WhenGroupHasNoRow()
    {
        _store.Seed("demo", "", "depth", "1");

        Parse("demo", "grp", "two").GetValue(_demo.TwoDepth).Should().Be(1);
    }

    [Fact]
    public void ParseWithConfig_ShouldIgnoreAmbiguousRowWithWarning_WhenTypesConflictAtLevel()
    {
        _store.Seed("demo", "", "level", "1");
        _store.Seed("demo", "grp one", "level", "2");

        Parse("demo", "grp", "one").GetValue(_demo.OneLevel).Should().Be(2);
        _warnings.ToString().Should().Contain("Warning: ignoring unknown config entry demo --level");
    }

    [Fact]
    public void ParseWithConfig_ShouldPreferCommandLine_WhenOptionSuppliedExplicitly()
    {
        _store.Seed("demo", "run", "count", "20");

        Parse("demo", "run", "--name", "n", "--count", "7").GetValue(_demo.RunCount).Should().Be(7);
    }

    [Fact]
    public void ParseWithConfig_ShouldApplyAllConfiguredOptions_WhenTwoConfiguredOnOneAction()
    {
        _store.Seed("demo", "run", "count", "20");
        _store.Seed("demo", "run", "mode", "b");
        _store.Seed("demo", "run", "tags", "x", "y");
        _store.Seed("demo", "run", "flag");

        var parse = Parse("demo", "run", "--name", "n");

        parse.Errors.Should().BeEmpty();
        parse.GetValue(_demo.RunCount).Should().Be(20);
        parse.GetValue(_demo.RunMode).Should().Be("b");
        parse.GetValue(_demo.RunTags).Should().Equal("x", "y");
        parse.GetValue(_demo.RunFlag).Should().BeTrue();
    }

    [Fact]
    public void ParseWithConfig_ShouldFailOnlyThatAction_WhenTokensCorrupt()
    {
        _store.SeedRaw("demo", "run", "count", "not json");
        _store.Seed("demo", "", "count", "9");

        var bad = Parse("demo", "run", "--name", "n");
        var good = Parse("demo", "walk");

        bad.Errors.Select(e => e.Message).Should().ContainSingle()
            .Which.Should().Be("Configured default for demo run --count is invalid: not json");
        good.Errors.Should().BeEmpty();
        good.GetValue(_demo.WalkCount).Should().Be(9);
    }

    [Fact]
    public void ParseWithConfig_ShouldFailOnlyThatAction_WhenValueNoLongerParses()
    {
        _store.Seed("demo", "run", "count", "abc");
        _store.Seed("demo", "run", "mode", "zzz");

        var bad = Parse("demo", "run", "--name", "n");
        var sibling = Parse("demo", "walk");

        bad.Errors.Select(e => e.Message).Should().HaveCount(2)
            .And.OnlyContain(m => m.StartsWith("Configured default for demo run --"));
        sibling.Errors.Should().BeEmpty();
        sibling.GetValue(_demo.WalkCount).Should().Be(5);
    }

    [Fact]
    public void ParseWithConfig_ShouldWarnAndIgnore_WhenRowsUnknown()
    {
        _store.Seed("demo", "run", "nonsense", "1");
        _store.Seed("demo", "nowhere", "count", "1");
        _store.Seed("demo", "run", "secret", "s");
        _store.Seed("demo", "run", "name", "n");

        var parse = Parse("demo", "run", "--name", "n");

        parse.Errors.Should().BeEmpty();
        _warnings.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).Should().BeEquivalentTo(
            "Warning: ignoring unknown config entry demo run --nonsense",
            "Warning: ignoring unknown config entry demo nowhere --count",
            "Warning: ignoring unknown config entry demo run --secret",
            "Warning: ignoring unknown config entry demo run --name");
    }

    [Fact]
    public void ParseWithConfig_ShouldOnlyTouchInvokedTopic_WhenOtherTopicsExist()
    {
        _store.Seed("other", "act", "x", "z");
        _store.Seed("demo", "run", "count", "4");

        Parse("demo", "run", "--name", "n");

        _store.TopicsAccessed.Should().Equal("demo");
    }

    [Theory]
    [InlineData("config")]
    [InlineData("")]
    public void ParseWithConfig_ShouldNotTouchStore_WhenNoTopicOrConfigTopicInvoked(string topic)
    {
        ConfigBootstrap.AddConfigTopic(_demo.Root, _store);

        var args = topic.Length == 0 ? Array.Empty<string>() : new[] { topic, "demo" };
        ConfigBootstrap.ParseWithConfig(_demo.Root, args, _store, _warnings);

        _store.TopicsAccessed.Should().BeEmpty();
    }

    [Fact]
    public void ParseWithConfig_ShouldPropagateStoreException_WhenStoreFails()
    {
        _store.FailWith = new ConfigStoreException("boom");

        var act = () => Parse("demo", "run", "--name", "n");

        act.Should().Throw<ConfigStoreException>().WithMessage("boom");
    }

    [Fact]
    public async Task InvokeAsync_ShouldReturnOneWithMessage_WhenStoreFails()
    {
        _store.FailWith = new ConfigStoreException("boom");
        using var console = new ConsoleCapture();

        var code = await ConfigBootstrap.InvokeAsync(_demo.Root, ["demo", "run", "--name", "n"], _store);

        code.Should().Be(1);
        console.Output.Should().Contain("Error: boom");
    }

    [Fact]
    public void ParseWithConfig_ShouldKeepHelpIdentical_WhenNothingConfigured()
    {
        var baseline = HelpText("demo", "run");
        _store.Seed("demo", "walk", "count", "9"); // different action: run untouched

        HelpText("demo", "run").Should().Be(baseline);
        baseline.Split('\n').Single(l => l.Contains("--mode")).Should().NotContain("[default:", "mode has no built-in default");
    }

    [Fact]
    public void ParseWithConfig_ShouldShowConfiguredDefaultInHelp_WhenConfigured()
    {
        _store.Seed("demo", "run", "mode", "b");

        HelpText("demo", "run").Should().Contain("[default: b]");
    }

    [Fact]
    public void ParseWithConfig_ShouldBeIdempotent_WhenCalledRepeatedlyOnOneTree()
    {
        _store.Seed("demo", "run", "count", "20");
        Parse("demo", "run", "--name", "n").GetValue(_demo.RunCount).Should().Be(20);

        var emptyStore = new InMemoryConfigStore();
        var second = ConfigBootstrap.ParseWithConfig(_demo.Root, ["demo", "run", "--name", "n"], emptyStore, _warnings);
        second.GetValue(_demo.RunCount).Should().Be(3);
        _demo.RunCount.DefaultValueFactory.Should().NotBeNull();
        _demo.RunMode.Invoking(o => ((Option<string>)o).DefaultValueFactory).Should().NotThrow()
            .Which.Should().BeNull();

        var third = Parse("demo", "run", "--name", "n");
        third.GetValue(_demo.RunCount).Should().Be(20);
    }

    [Fact]
    public void ParseWithConfig_ShouldWorkAgainstRealLlmTree_WhenModelConfiguredAtTopic()
    {
        var root = new RootCommand();
        root.Add(LlmCommand.Create());
        _store.Seed("llm", "", "model", "my-model");
        _store.Seed("llm", "translate", "provider", "claude");
        var chat = root.Subcommands.Single().Subcommands.Single(c => c.Name == "chat");
        var translate = root.Subcommands.Single().Subcommands.Single(c => c.Name == "translate");
        var model = (Option<string?>)chat.Options.Single(o => o.Name == "--model");
        var provider = (Option<string>)translate.Options.Single(o => o.Name == "--provider");

        var chatParse = ConfigBootstrap.ParseWithConfig(root, ["llm", "chat", "--message", "hi"], _store, _warnings);
        var translateParse = ConfigBootstrap.ParseWithConfig(root, ["llm", "translate", "--text", "a", "--target-language", "fr"], _store, _warnings);

        chatParse.Errors.Should().BeEmpty();
        chatParse.GetValue(model).Should().Be("my-model");
        translateParse.GetValue(provider).Should().Be("claude");
    }

    [Fact]
    public void ParseWithConfig_ShouldRejectBogusProvider_WhenRealOptionHasAcceptOnlyFromAmong()
    {
        var root = new RootCommand();
        root.Add(LlmCommand.Create());
        _store.Seed("llm", "", "provider", "bogus");

        var parse = ConfigBootstrap.ParseWithConfig(root, ["llm", "chat", "--message", "hi"], _store, _warnings);

        parse.Errors.Should().ContainSingle().Which.Message
            .Should().StartWith("Configured default for llm --provider is invalid:");
    }

    private string HelpText(params string[] path)
    {
        var parse = Parse([.. path, "--help"]);
        var writer = new StringWriter();
        parse.InvocationConfiguration.Output = writer;
        parse.Invoke();
        return writer.ToString();
    }
}
