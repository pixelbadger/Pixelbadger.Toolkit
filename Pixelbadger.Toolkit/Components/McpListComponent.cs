using System.CommandLine;
using Pixelbadger.Toolkit.Services;
using Spectre.Console;

namespace Pixelbadger.Toolkit.Components;

public sealed class McpListComponent(IMcpProfileStore store, Func<RootCommand> treeFactory)
{
    public int Run()
    {
        IReadOnlyList<McpProfile> saved;
        try
        {
            saved = store.GetAll();
        }
        catch (ConfigStoreException ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }

        var rows = saved.Select(p => (Profile: p, BuiltIn: false)).ToList();
        if (!saved.Any(p => p.Name == McpProfiles.DefaultName))
            rows.Add((McpProfiles.BuiltInDefault, true));
        rows.Sort((a, b) => string.CompareOrdinal(a.Profile.Name, b.Profile.Name));

        var root = treeFactory();
        for (var i = 0; i < rows.Count; i++)
        {
            var (profile, builtIn) = rows[i];
            if (i > 0) AnsiConsole.WriteLine();

            var entries = profile.Include.Select(e =>
                McpEntryResolver.Resolve(root, e) is null ? $"{e} (unresolved)" : e);
            AnsiConsole.MarkupLine(Markup.Escape(profile.Name + (builtIn ? " (built-in)" : "")));
            AnsiConsole.MarkupLine("  include: " + Markup.Escape(string.Join(", ", entries)));
            AnsiConsole.MarkupLine("  host: " + Markup.Escape(profile.Host ?? "(default)"));
            AnsiConsole.MarkupLine("  port: " + Markup.Escape(profile.Port?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "(default)"));
        }
        return 0;
    }
}
