using System.CommandLine;
using System.Runtime.CompilerServices;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.CommandLine;

/// <summary>Applies the invoked topic's configured defaults to the invoked action by installing default-value factories.</summary>
internal static class ConfigDefaultsBinder
{
    // Per-root undo actions for the factories installed by the previous ParseWithConfig call.
    private static readonly ConditionalWeakTable<RootCommand, List<Action>> Installed = new();

    public static ParseResult ParseWithConfig(RootCommand root, string[] args, IConfigStore store, TextWriter warnings)
    {
        Reset(root);

        var first = root.Parse(args);
        var topic = FindTopic(root, first.CommandResult.Command);
        if (topic is null || topic.Name == ConfigTree.ConfigTopicName)
            return first;

        var rows = store.ForTopic(topic.Name).GetAll();
        if (rows.Count == 0)
            return first;

        var tree = ConfigTree.Build(root);

        // Rows that do not match a configurable option at their level are warned about and ignored.
        var valid = new Dictionary<(string, string), ConfigEntry>();
        foreach (var row in rows)
        {
            var node = tree.Find(topic.Name, row.ActionPath);
            if (node is null || !node.Options.ContainsKey(row.Option))
            {
                warnings.WriteLine($"Warning: ignoring unknown config entry {ConfigNode.LevelOf(topic.Name, row.ActionPath)} --{row.Option}");
                continue;
            }

            valid[(row.ActionPath, row.Option)] = row;
        }

        var action = first.CommandResult.Command;
        var path = PathBelow(topic, action);

        // Validate everything first (no factories installed yet), then install.
        var installs = new List<Action>();
        foreach (var option in action.Options.Where(ConfigTree.IsConfigurable))
        {
            var key = ConfigTree.KeyOf(option);
            ConfigEntry? row = null;
            for (var depth = path.Length; depth >= 0 && row is null; depth--)
                valid.TryGetValue((string.Join(' ', path.Take(depth)), key), out row);

            if (row is null)
                continue;

            var level = ConfigNode.LevelOf(topic.Name, row.ActionPath);
            var captured = option;

            if (row.Tokens is null)
            {
                var message = $"Configured default for {level} --{key} is invalid: {row.RawValue}";
                installs.Add(() => Install(root, captured, () => OptionSandbox.SetError(captured, message)));
                continue;
            }

            var sandbox = OptionSandbox.Validate(root, topic.Name, path, option, row.Tokens);
            if (sandbox.Errors.Count > 0)
            {
                var message = $"Configured default for {level} --{key} is invalid: {sandbox.Errors[0]}";
                installs.Add(() => Install(root, captured, () => OptionSandbox.SetError(captured, message)));
            }
            else
            {
                var value = OptionSandbox.GetValue(sandbox.Parse, option);
                installs.Add(() => Install(root, captured, () => OptionSandbox.SetConstant(captured, value)));
            }
        }

        if (installs.Count == 0)
            return first;

        foreach (var install in installs)
            install();

        return root.Parse(args);
    }

    private static void Install(RootCommand root, Option option, Action set)
    {
        var original = OptionSandbox.GetFactory(option);
        Installed.GetOrCreateValue(root).Add(() => OptionSandbox.SetFactory(option, original));
        set();
    }

    private static void Reset(RootCommand root)
    {
        if (!Installed.TryGetValue(root, out var undo))
            return;

        // Undo in reverse so the earliest captured original wins if an option were installed twice.
        for (var i = undo.Count - 1; i >= 0; i--)
            undo[i]();
        undo.Clear();
    }

    private static Command? FindTopic(RootCommand root, Command command)
    {
        Command? current = command;
        while (current is not null)
        {
            var parent = current.Parents.OfType<Command>().FirstOrDefault();
            if (ReferenceEquals(parent, root))
                return current;
            current = parent;
        }

        return null;
    }

    private static string[] PathBelow(Command topic, Command action)
    {
        var path = new List<string>();
        Command? current = action;
        while (current is not null && !ReferenceEquals(current, topic))
        {
            path.Insert(0, current.Name);
            current = current.Parents.OfType<Command>().FirstOrDefault();
        }

        return path.ToArray();
    }
}
