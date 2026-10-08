using System.CommandLine;
using System.CommandLine.Parsing;
using System.Reflection;
using Pixelbadger.Toolkit.CommandLine;
using Pixelbadger.Toolkit.Services;
using Spectre.Console;

namespace Pixelbadger.Toolkit.Commands;

/// <summary>
/// The `config` topic. Its subcommand tree mirrors every other topic, group and action that has configurable options,
/// built generically from the finished command tree.
/// </summary>
public static class ConfigCommand
{
    internal static Command Create(RootCommand root, IConfigStore store)
    {
        var tree = ConfigTree.Build(root);
        var command = new Command(ConfigTree.ConfigTopicName, "Show or set per-user default values for command options");

        foreach (var topic in tree.Topics)
            command.Add(CreateNode(root, store, topic));

        return command;
    }

    private static Command CreateNode(RootCommand root, IConfigStore store, ConfigNode node)
    {
        var name = node.Path.Length == 0 ? node.Topic : node.Path[^1];
        var command = new Command(name, $"Show or set default option values for '{node.LevelPath}'");

        var clones = new Dictionary<string, Option>();
        foreach (var info in node.Options.Values.OrderBy(o => o.Name, StringComparer.Ordinal))
        {
            var clone = CreateClone(node, info);
            clones[info.Name] = clone;
            command.Add(clone);
        }

        var unsetOption = new Option<string[]>("--unset")
        {
            Description = "Remove the configured default of the named option (repeatable)",
            AllowMultipleArgumentsPerToken = true
        };
        command.Add(unsetOption);

        foreach (var child in node.Children)
            command.Add(CreateNode(root, store, child));

        command.SetAction(parseResult => Run(root, store, node, clones, unsetOption, parseResult));
        return command;
    }

    private static Option CreateClone(ConfigNode node, ConfigOptionInfo info)
    {
        var affected = info.Owners
            .Select(o =>
            {
                var relative = o.Path.Skip(node.Path.Length).ToArray();
                return relative.Length == 0 ? (node.Path.Length == 0 ? node.Topic : node.Path[^1]) : string.Join(' ', relative);
            })
            .Distinct();

        var prototype = info.Prototype;
        var description = $"{prototype.Description} (default for: {string.Join(", ", affected)})".TrimStart();

        var option = (Option)typeof(ConfigCommand)
            .GetMethod(nameof(NewOption), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(info.ValueType)
            .Invoke(null, ["--" + info.Name])!;
        option.Description = description;
        option.Arity = prototype.Arity;
        option.AllowMultipleArgumentsPerToken = prototype.AllowMultipleArgumentsPerToken;
        return option;
    }

    private static Option NewOption<T>(string name) => new Option<T>(name);

    private static int Run(
        RootCommand root,
        IConfigStore store,
        ConfigNode node,
        Dictionary<string, Option> clones,
        Option<string[]> unsetOption,
        ParseResult parseResult)
    {
        try
        {
            var unsets = (parseResult.GetValue(unsetOption) ?? []).Select(n => n.TrimStart('-')).ToList();
            var sets = new List<(ConfigOptionInfo Info, List<string> Tokens)>();
            foreach (var (key, clone) in clones)
            {
                var result = parseResult.GetResult(clone);
                if (result is null || result.Implicit)
                    continue;
                sets.Add((node.Options[key], result.Tokens.Select(t => t.Value).ToList()));
            }

            var topicConfig = store.ForTopic(node.Topic);

            if (unsets.Count == 0 && sets.Count == 0)
            {
                Show(node, topicConfig);
                return 0;
            }

            // Validate everything before writing anything.
            var errors = new List<string>();
            foreach (var name in unsets)
            {
                if (!node.Options.ContainsKey(name))
                    errors.Add($"'{name}' is not a configurable option of {node.LevelPath}");
            }

            foreach (var (info, tokens) in sets)
            {
                foreach (var owner in info.Owners)
                {
                    var sandbox = OptionSandbox.Validate(root, node.Topic, owner.Path, owner.Option, tokens);
                    foreach (var message in sandbox.Errors)
                    {
                        if (!errors.Contains(message))
                            errors.Add(message);
                    }
                }
            }

            if (errors.Count > 0)
            {
                foreach (var message in errors)
                    AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(message)}");
                return 1;
            }

            foreach (var name in unsets.Distinct())
            {
                AnsiConsole.WriteLine(topicConfig.Unset(node.ActionPath, name)
                    ? $"Unset {node.LevelPath} --{name}"
                    : $"{node.LevelPath} --{name} was not set");
            }

            foreach (var (info, tokens) in sets)
            {
                topicConfig.Set(node.ActionPath, info.Name, tokens);
                AnsiConsole.WriteLine($"Set {node.LevelPath} --{info.Name} = {string.Join(' ', tokens)}".TrimEnd());
            }

            return 0;
        }
        catch (ConfigStoreException ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
    }

    private static void Show(ConfigNode node, ITopicConfig topicConfig)
    {
        var rows = topicConfig.GetAll();
        var lines = new List<string>();

        foreach (var name in node.Options.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            // Nearest level wins: this node first, then each ancestor up to the topic.
            for (var depth = node.Path.Length; depth >= 0; depth--)
            {
                var actionPath = string.Join(' ', node.Path.Take(depth));
                var row = rows.FirstOrDefault(r => r.ActionPath == actionPath && r.Option == name);
                if (row is null)
                    continue;

                var value = row.Tokens is null ? row.RawValue : string.Join(' ', row.Tokens);
                lines.Add($"--{name} = {value} (from: {ConfigNode.LevelOf(node.Topic, actionPath)})");
                break;
            }
        }

        if (lines.Count == 0)
        {
            AnsiConsole.WriteLine($"No configured defaults for {node.LevelPath}");
            return;
        }

        foreach (var line in lines)
            AnsiConsole.WriteLine(line);
    }
}
