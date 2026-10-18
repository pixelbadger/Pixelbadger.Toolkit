using System.CommandLine;
using Pixelbadger.Toolkit.CommandLine;

namespace Pixelbadger.Toolkit.Services;

/// <summary>The tool commands an include list resolves to, plus the entries that did not resolve.</summary>
public sealed record McpResolution(IReadOnlyList<Command> Tools, IReadOnlyList<string> Unresolved);

/// <summary>
/// Resolves profile include entries ("topic" or "topic/action[/sub...]") against a command tree. An entry resolves to
/// every tool command (a command with an action that is not excluded from MCP) at or under the named node.
/// </summary>
public static class McpEntryResolver
{
    /// <summary>The tool commands for <paramref name="entry"/> in tree order, or null when the entry is invalid.</summary>
    public static IReadOnlyList<Command>? Resolve(RootCommand root, string entry)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (string.IsNullOrEmpty(entry)) return null;

        Command node = root;
        foreach (var segment in entry.Split('/'))
        {
            var next = node.Subcommands.FirstOrDefault(c => string.Equals(c.Name, segment, StringComparison.Ordinal));
            if (next is null) return null;
            node = next;
        }

        if (node.IsExcludedFromMcp()) return null;

        var tools = new List<Command>();
        Collect(node, tools);
        return tools.Count == 0 ? null : tools;
    }

    /// <summary>Resolves every entry; tools are de-duplicated keeping first-seen (tree) order per entry.</summary>
    public static McpResolution ResolveAll(RootCommand root, IEnumerable<string> entries)
    {
        var tools = new List<Command>();
        var seen = new HashSet<Command>();
        var unresolved = new List<string>();
        foreach (var entry in entries)
        {
            var resolved = Resolve(root, entry);
            if (resolved is null)
            {
                unresolved.Add(entry);
                continue;
            }
            foreach (var tool in resolved)
                if (seen.Add(tool)) tools.Add(tool);
        }
        return new McpResolution(tools, unresolved);
    }

    private static void Collect(Command node, List<Command> tools)
    {
        if (node.Action is not null && !node.IsExcludedFromMcp()) tools.Add(node);
        foreach (var child in node.Subcommands) Collect(child, tools);
    }
}
