using System.CommandLine;
using FluentAssertions;
using Pixelbadger.Toolkit.CommandLine;
using Pixelbadger.Toolkit.Commands;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Tests;

[Collection("AnsiConsole")]
public class ConfigCommandTests : IDisposable
{
    private readonly DemoTree _demo = new();
    private readonly InMemoryConfigStore _store = new();
    private readonly ConsoleCapture _console = new();

    public ConfigCommandTests() => ConfigBootstrap.AddConfigTopic(_demo.Root, _store);

    public void Dispose() => _console.Dispose();

    private int Run(params string[] args) => _demo.Root.Parse(args).Invoke();

    private string[] Lines => _console.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r', ' ')).ToArray();

    private List<ConfigEntry> Rows(string topic) => _store.Tables.GetValueOrDefault(topic) ?? [];

    [Fact]
    public void Config_ShouldSetTopicLevelDefault_WhenOptionSupplied()
    {
        Run("config", "demo", "--count", "7").Should().Be(0);

        Lines.Should().Equal("Set demo --count = 7");
        Rows("demo").Should().ContainSingle().Which.Should().BeEquivalentTo(new ConfigEntry("", "count", ["7"], "[\"7\"]"),
            o => o.Excluding(e => e.RawValue));
    }

    [Fact]
    public void Config_ShouldSetActionLevelDefault_WhenActionNodeUsed()
    {
        Run("config", "demo", "run", "--count", "7").Should().Be(0);

        Rows("demo").Should().ContainSingle().Which.ActionPath.Should().Be("run");
    }

    [Fact]
    public void Config_ShouldSetDeepPath_WhenGroupAndActionUsed()
    {
        Run("config", "demo", "grp", "one", "--level", "4").Should().Be(0);

        var row = Rows("demo").Should().ContainSingle().Subject;
        row.ActionPath.Should().Be("grp one");
        row.Option.Should().Be("level");
    }

    [Fact]
    public void Config_ShouldStoreAllTokensVerbatim_WhenMultiValue()
    {
        Run("config", "demo", "run", "--tags", "a", "b", "c").Should().Be(0);

        Rows("demo").Single().Tokens.Should().Equal("a", "b", "c");
        Lines.Should().Equal("Set demo run --tags = a b c");
    }

    [Fact]
    public void Config_ShouldStoreEmptyTokens_WhenBareBoolFlag()
    {
        Run("config", "demo", "run", "--flag").Should().Be(0);

        Rows("demo").Single().Tokens.Should().BeEmpty();
    }

    [Fact]
    public void Config_ShouldWriteNothing_WhenIntegerInvalid()
    {
        var parse = _demo.Root.Parse(["config", "demo", "--count", "abc", "--color", "blue"]);

        parse.Errors.Should().NotBeEmpty();
        parse.Invoke(new InvocationConfiguration { Error = TextWriter.Null }).Should().Be(1);
        Rows("demo").Should().BeEmpty();
    }

    [Fact]
    public void Config_ShouldWriteNothing_WhenAcceptOnlyFromAmongViolated()
    {
        Run("config", "demo", "run", "--mode", "zzz", "--count", "5").Should().Be(1);

        Rows("demo").Should().BeEmpty();
        _console.Output.Should().Contain("Error:").And.Contain("zzz");
    }

    [Fact]
    public void Config_ShouldRejectBogusProvider_WhenRealLlmTreeUsed()
    {
        var root = new RootCommand();
        root.Add(LlmCommand.Create());
        ConfigBootstrap.AddConfigTopic(root, _store);

        root.Parse(["config", "llm", "--provider", "bogus"]).Invoke().Should().Be(1);

        Rows("llm").Should().BeEmpty();
        _console.Output.Should().Contain("Error:");
    }

    [Fact]
    public void Config_ShouldAcceptSupportedProvider_WhenRealLlmTreeUsed()
    {
        var root = new RootCommand();
        root.Add(LlmCommand.Create());
        ConfigBootstrap.AddConfigTopic(root, _store);

        root.Parse(["config", "llm", "--provider", "claude", "--model", "m"]).Invoke().Should().Be(0);

        Rows("llm").Select(r => r.Option).Should().BeEquivalentTo("provider", "model");
    }

    [Fact]
    public void Config_ShouldNotOfferImmutableOptions_WhenRealTreeUsed()
    {
        var root = new RootCommand();
        root.Add(LlmCommand.Create());
        root.Add(GptCommand.Create());
        ConfigBootstrap.AddConfigTopic(root, _store);

        root.Parse(["config", "llm", "--session-id", "3"]).Invoke().Should().NotBe(0);
        root.Parse(["config", "gpt", "train", "--resume"]).Invoke().Should().NotBe(0);
        Rows("llm").Should().BeEmpty();
        Rows("gpt").Should().BeEmpty();
    }

    [Fact]
    public void Config_ShouldReportDeleted_WhenUnsetRemovesRow()
    {
        _store.Seed("demo", "", "count", "7");

        Run("config", "demo", "--unset", "count").Should().Be(0);

        Lines.Should().Equal("Unset demo --count");
        Rows("demo").Should().BeEmpty();
    }

    [Fact]
    public void Config_ShouldReportNotSet_WhenUnsetFindsNoRow()
    {
        Run("config", "demo", "--unset", "--count").Should().Be(0);

        Lines.Should().Equal("demo --count was not set");
    }

    [Fact]
    public void Config_ShouldAcceptRepeatedAndMultipleUnsets_WhenGiven()
    {
        _store.Seed("demo", "run", "count", "7");
        _store.Seed("demo", "run", "mode", "a");

        Run("config", "demo", "run", "--unset", "count", "mode").Should().Be(0);

        Rows("demo").Should().BeEmpty();
    }

    [Fact]
    public void Config_ShouldFailAndChangeNothing_WhenUnsetNameUnknown()
    {
        _store.Seed("demo", "", "count", "7");

        Run("config", "demo", "--unset", "count", "bogus", "--color", "blue").Should().Be(1);

        Lines.Should().Equal("Error: 'bogus' is not a configurable option of demo");
        Rows("demo").Should().ContainSingle().Which.Option.Should().Be("count");
    }

    [Fact]
    public void Config_ShouldNotAllowUnsettingNameOmittedAtNode_WhenAmbiguous()
    {
        Run("config", "demo", "grp", "--unset", "level").Should().Be(1);

        Lines.Single().Should().Be("Error: 'level' is not a configurable option of demo grp");
    }

    [Fact]
    public void Config_ShouldApplyUnsetBeforeSet_WhenBothSuppliedForSameOption()
    {
        _store.Seed("demo", "", "count", "7");

        Run("config", "demo", "--unset", "count", "--count", "9").Should().Be(0);

        Lines.Should().Equal("Unset demo --count", "Set demo --count = 9");
        Rows("demo").Single().Tokens.Should().Equal("9");
    }

    [Fact]
    public void Config_ShouldShowInheritedValuesWithOrigin_WhenNoOptionsSupplied()
    {
        _store.Seed("demo", "", "count", "10");
        _store.Seed("demo", "", "color", "blue");
        _store.Seed("demo", "run", "count", "20");
        _store.Seed("demo", "run", "tags", "a", "b");
        _store.Seed("other", "", "x", "leak");

        Run("config", "demo", "run").Should().Be(0);

        Lines.Should().Equal(
            "--count = 20 (from: demo run)",
            "--tags = a b (from: demo run)");
    }

    [Fact]
    public void Config_ShouldShowTopicRowOnly_WhenAtTopicNode()
    {
        _store.Seed("demo", "", "count", "10");
        _store.Seed("demo", "run", "mode", "a");

        Run("config", "demo").Should().Be(0);

        Lines.Should().Equal("--count = 10 (from: demo)");
    }

    [Fact]
    public void Config_ShouldInheritFromAncestors_WhenShowingNestedNode()
    {
        _store.Seed("demo", "", "level", "1"); // ambiguous at topic; shown only where the option exists
        _store.Seed("demo", "grp", "level", "2");
        _store.Seed("demo", "grp one", "level", "3");

        Run("config", "demo", "grp", "one").Should().Be(0);

        Lines.Should().Equal("--level = 3 (from: demo grp one)");
    }

    [Fact]
    public void Config_ShouldShowNothingConfiguredMessage_WhenNoRows()
    {
        Run("config", "demo", "walk").Should().Be(0);

        Lines.Should().Equal("No configured defaults for demo walk");
    }

    [Fact]
    public void Config_ShouldTouchOnlyOwnTopic_WhenShowingOrSetting()
    {
        Run("config", "demo", "--count", "1");
        Run("config", "demo");

        _store.TopicsAccessed.Distinct().Should().Equal("demo");
    }

    [Fact]
    public void Config_ShouldPrintHelpAndNotTouchStore_WhenNoTopicGiven()
    {
        var parse = _demo.Root.Parse(["config"]);
        var output = new StringWriter();
        parse.Invoke(new InvocationConfiguration { Output = output, Error = TextWriter.Null });

        output.ToString().Should().Contain("demo").And.Contain("other").And.Contain("Usage:");
        _store.TopicsAccessed.Should().BeEmpty();
    }

    [Fact]
    public void Config_ShouldReturnOneWithMessage_WhenStoreFailsOnSet()
    {
        _store.FailWith = new ConfigStoreException("disk on fire");

        Run("config", "demo", "--count", "1").Should().Be(1);

        Lines.Should().Equal("Error: disk on fire");
    }

    [Fact]
    public void Config_ShouldReturnOneWithMessage_WhenStoreFailsOnShow()
    {
        _store.FailWith = new ConfigStoreException("unreadable");

        Run("config", "demo").Should().Be(1);

        Lines.Should().Equal("Error: unreadable");
    }

    [Fact]
    public void Config_ShouldEscapeMarkup_WhenErrorMessageContainsBrackets()
    {
        _store.FailWith = new ConfigStoreException("bad [thing]");

        Run("config", "demo").Should().Be(1);

        Lines.Should().Equal("Error: bad [thing]");
    }

    [Fact]
    public void Config_ShouldOfferHelp_WhenHelpRequestedForNode()
    {
        var parse = _demo.Root.Parse(["config", "demo", "run", "--help"]);
        var writer = new StringWriter();
        parse.InvocationConfiguration.Output = writer;
        parse.Invoke().Should().Be(0);

        writer.ToString().Should().Contain("--count").And.Contain("(default for: run)").And.Contain("--unset")
            .And.NotContain("--secret").And.NotContain("--name");
    }

    [Fact]
    public async Task Config_ShouldPersistThenApplyDefault_WhenSetThenInvokedThroughBootstrap()
    {
        (await ConfigBootstrap.InvokeAsync(_demo.Root, ["config", "demo", "run", "--count", "42"], _store)).Should().Be(0);

        var parse = ConfigBootstrap.ParseWithConfig(_demo.Root, ["demo", "run", "--name", "n"], _store, TextWriter.Null);

        parse.GetValue(_demo.RunCount).Should().Be(42);
    }
}
