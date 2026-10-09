using System.CommandLine;
using Pixelbadger.Toolkit.CommandLine;
using Pixelbadger.Toolkit.Services;
using Spectre.Console;

namespace Pixelbadger.Toolkit.Tests;

/// <summary>In-memory IConfigStore that records which topics were accessed.</summary>
internal sealed class InMemoryConfigStore : IConfigStore
{
    public Dictionary<string, List<ConfigEntry>> Tables { get; } = new();
    public List<string> TopicsAccessed { get; } = new();
    public Exception? FailWith { get; set; }

    public string DatabasePath => "memory";

    public ITopicConfig ForTopic(string topic)
    {
        TopicsAccessed.Add(topic);
        return new Topic(this, topic);
    }

    public void Seed(string topic, string actionPath, string option, params string[] tokens) =>
        Rows(topic).Add(new ConfigEntry(actionPath, option, tokens, System.Text.Json.JsonSerializer.Serialize(tokens)));

    public void SeedRaw(string topic, string actionPath, string option, string raw) =>
        Rows(topic).Add(new ConfigEntry(actionPath, option, null, raw));

    private List<ConfigEntry> Rows(string topic)
    {
        if (!Tables.TryGetValue(topic, out var rows))
            Tables[topic] = rows = new List<ConfigEntry>();
        return rows;
    }

    private sealed class Topic(InMemoryConfigStore store, string topic) : ITopicConfig
    {
        string ITopicConfig.Topic => topic;

        public IReadOnlyList<ConfigEntry> GetAll()
        {
            if (store.FailWith is { } ex) throw ex;
            return store.Tables.TryGetValue(topic, out var rows) ? rows.ToList() : [];
        }

        public void Set(string actionPath, string option, IReadOnlyList<string> tokens)
        {
            if (store.FailWith is { } ex) throw ex;
            var rows = store.Rows(topic);
            rows.RemoveAll(r => r.ActionPath == actionPath && r.Option == option);
            rows.Add(new ConfigEntry(actionPath, option, tokens.ToList(), System.Text.Json.JsonSerializer.Serialize(tokens)));
        }

        public bool Unset(string actionPath, string option)
        {
            if (store.FailWith is { } ex) throw ex;
            return store.Tables.TryGetValue(topic, out var rows)
                   && rows.RemoveAll(r => r.ActionPath == actionPath && r.Option == option) > 0;
        }
    }
}

/// <summary>A synthetic command tree covering every shape the config system must handle.</summary>
internal sealed class DemoTree
{
    public RootCommand Root { get; } = new("test root");
    public Option<string> TopicColor { get; } = new("--color") { Description = "Colour", DefaultValueFactory = _ => "red" };
    public Option<int> RunCount { get; } = new("--count") { Description = "Count", DefaultValueFactory = _ => 3 };
    public Option<string> RunMode { get; } = new("--mode") { Description = "Mode" };
    public Option<string> RunName { get; } = new("--name") { Description = "Name", Required = true };
    public Option<bool> RunFlag { get; } = new("--flag") { Description = "Flag" };
    public Option<string[]> RunTags { get; } = new("--tags") { Description = "Tags", AllowMultipleArgumentsPerToken = true };
    public Option<string> RunSecret { get; } = new Option<string>("--secret") { Description = "Secret" }.WithImmutableDefault();
    public Option<int> WalkCount { get; } = new("--count") { Description = "Count", DefaultValueFactory = _ => 5 };
    public Option<int> OneLevel { get; } = new("--level") { Description = "Level" };
    public Option<int> OneDepth { get; } = new("--depth") { Description = "Depth" };
    public Option<int> TwoDepth { get; } = new("--depth") { Description = "Depth" };
    public Option<string> TwoLevel { get; } = new("--level") { Description = "Level" };
    public Option<string> OtherX { get; } = new("--x") { Description = "X", DefaultValueFactory = _ => "x0" };

    public DemoTree()
    {
        RunMode.AcceptOnlyFromAmong("a", "b");

        var demo = new Command("demo", "Demo topic") { TopicColor };
        var run = new Command("run", "Run it") { RunCount, RunMode, RunName, RunFlag, RunTags, RunSecret };
        var walk = new Command("walk", "Walk it") { WalkCount };
        var grp = new Command("grp", "A group");
        grp.Add(new Command("one", "One") { OneLevel, OneDepth });
        grp.Add(new Command("two", "Two") { TwoLevel, TwoDepth });
        var empty = new Command("empty", "No options");
        foreach (var c in new[] { run, walk, empty }) demo.Add(c);
        demo.Add(grp);
        Root.Add(demo);

        var other = new Command("other", "Other topic");
        other.Add(new Command("act", "Act") { OtherX });
        Root.Add(other);
    }
}

[CollectionDefinition("AnsiConsole", DisableParallelization = true)]
public sealed class AnsiConsoleCollection;

/// <summary>Redirects the static AnsiConsole to a buffer for the lifetime of the instance.</summary>
internal sealed class ConsoleCapture : IDisposable
{
    private readonly IAnsiConsole _previous = AnsiConsole.Console;
    private readonly StringWriter _writer = new();

    public ConsoleCapture()
    {
        AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(_writer),
            Interactive = InteractionSupport.No
        });
        AnsiConsole.Console.Profile.Width = 500;
    }

    public string Output => _writer.ToString();

    public void Dispose() => AnsiConsole.Console = _previous;
}
