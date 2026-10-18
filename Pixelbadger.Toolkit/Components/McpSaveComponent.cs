using System.CommandLine;
using Pixelbadger.Toolkit.Services;
using Spectre.Console;

namespace Pixelbadger.Toolkit.Components;

public sealed class McpSaveComponent(IMcpProfileStore store, Func<RootCommand> treeFactory)
{
    public int Run(string name, IReadOnlyList<string> include, string? host, int? port)
    {
        if (McpProfileValidation.ValidateName(name) is { } nameError) return Fail(nameError);

        var root = treeFactory();
        var tools = new List<Command>();
        foreach (var entry in include)
        {
            var resolved = McpEntryResolver.Resolve(root, entry);
            if (resolved is null) return Fail($"Unknown or non-exposable entry '{entry}'.");
            foreach (var tool in resolved)
                if (!tools.Contains(tool)) tools.Add(tool);
        }

        if (McpProfileValidation.ValidatePort(port) is { } portError) return Fail(portError);
        if (host is not null && host.Length == 0) return Fail("Host must not be empty.");

        try
        {
            store.Save(new McpProfile(name, include.ToArray(), host, port));
        }
        catch (Exception ex) when (ex is ConfigStoreException or ArgumentException)
        {
            return Fail(ex.Message);
        }

        AnsiConsole.MarkupLine($"[green]Saved MCP profile '{Markup.Escape(name)}'[/] ({tools.Count} tools)");
        return 0;
    }

    private static int Fail(string message)
    {
        AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(message)}");
        return 1;
    }
}
