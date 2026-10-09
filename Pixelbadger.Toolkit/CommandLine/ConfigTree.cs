using System.CommandLine;
using System.CommandLine.Parsing;
using System.CommandLine.Help;

namespace Pixelbadger.Toolkit.CommandLine;

/// <summary>An action's real option that contributes to a config option. <see cref="Path"/> is the path below the topic.</summary>
internal sealed record ConfigOwner(string[] Path, Command Command, Option Option);

/// <summary>A configurable option name at one node, with every real option it covers.</summary>
internal sealed class ConfigOptionInfo(string name, Type valueType, Option prototype, IReadOnlyList<ConfigOwner> owners)
{
    /// <summary>Option name without leading dashes.</summary>
    public string Name { get; } = name;
    public Type ValueType { get; } = valueType;
    public Option Prototype { get; } = prototype;
    public IReadOnlyList<ConfigOwner> Owners { get; } = owners;
}

/// <summary>One topic, group or action in the config tree.</summary>
internal sealed class ConfigNode(
    string topic,
    string[] path,
    Command command,
    IReadOnlyDictionary<string, ConfigOptionInfo> options,
    IReadOnlyList<ConfigNode> children)
{
    public string Topic { get; } = topic;
    /// <summary>Path below the topic; empty for the topic node.</summary>
    public string[] Path { get; } = path;
    public Command Command { get; } = command;
    public IReadOnlyDictionary<string, ConfigOptionInfo> Options { get; } = options;
    public IReadOnlyList<ConfigNode> Children { get; } = children;

    /// <summary>Space-joined path below the topic, "" for the topic node (the store's actionPath).</summary>
    public string ActionPath => string.Join(' ', Path);

    /// <summary>Human-readable level, e.g. "llm" or "llm history delete".</summary>
    public string LevelPath => LevelOf(Topic, ActionPath);

    internal static string LevelOf(string topic, string actionPath) => actionPath.Length == 0 ? topic : $"{topic} {actionPath}";
}

/// <summary>The configurable-option view of a finished command tree, built by walking it generically.</summary>
internal sealed class ConfigTree
{
    internal const string ConfigTopicName = "config";
    internal const string ReservedOptionName = "unset";

    private readonly Dictionary<(string Topic, string ActionPath), ConfigNode> _index = new();

    public IReadOnlyList<ConfigNode> Topics { get; }

    private ConfigTree(IReadOnlyList<ConfigNode> topics)
    {
        Topics = topics;
        foreach (var topic in topics)
            Index(topic);
    }

    private void Index(ConfigNode node)
    {
        _index[(node.Topic, node.ActionPath)] = node;
        foreach (var child in node.Children)
            Index(child);
    }

    public ConfigNode? Find(string topic, string actionPath) => _index.GetValueOrDefault((topic, actionPath));

    public static bool IsConfigurable(Option option) =>
        !option.Required
        && option is not HelpOption
        && option is not VersionOption
        && !option.HasImmutableDefault();

    public static string KeyOf(Option option) => option.Name.TrimStart('-');

    /// <exception cref="InvalidOperationException">A configurable option is named --unset.</exception>
    public static ConfigTree Build(RootCommand root)
    {
        var topics = new List<ConfigNode>();
        foreach (var topic in root.Subcommands)
        {
            if (topic.Name == ConfigTopicName)
                continue;

            var (node, _) = BuildNode(topic.Name, [], topic);
            if (node is not null)
                topics.Add(node);
        }

        return new ConfigTree(topics);
    }

    private static (ConfigNode? Node, List<ConfigOwner> Owners) BuildNode(string topic, string[] path, Command command)
    {
        var owners = new List<ConfigOwner>();
        foreach (var option in command.Options.Where(IsConfigurable))
        {
            if (KeyOf(option) == ReservedOptionName)
                throw new InvalidOperationException(
                    $"'{ConfigTree.ReservedOptionName}' is reserved by the config command and cannot be a configurable option name ({NodeName(topic, path)} {option.Name}).");
            owners.Add(new ConfigOwner(path, command, option));
        }

        var children = new List<ConfigNode>();
        foreach (var sub in command.Subcommands)
        {
            var (child, childOwners) = BuildNode(topic, [.. path, sub.Name], sub);
            if (child is not null)
                children.Add(child);
            owners.AddRange(childOwners);
        }

        if (owners.Count == 0)
            return (null, owners);

        var options = new Dictionary<string, ConfigOptionInfo>();
        foreach (var group in owners.GroupBy(o => KeyOf(o.Option)))
        {
            var list = group.ToList();
            if (list.Select(o => o.Option.ValueType).Distinct().Count() > 1)
                continue; // ambiguous at this level; still available below where unambiguous

            options[group.Key] = new ConfigOptionInfo(group.Key, list[0].Option.ValueType, list[0].Option, list);
        }

        return (new ConfigNode(topic, path, command, options, children), owners);
    }

    private static string NodeName(string topic, string[] path) => ConfigNode.LevelOf(topic, string.Join(' ', path));
}
