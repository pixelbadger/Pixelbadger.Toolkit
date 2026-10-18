using Pixelbadger.Toolkit.Services;
using Spectre.Console;

namespace Pixelbadger.Toolkit.Components;

public sealed class McpDeleteComponent(IMcpProfileStore store)
{
    public int Run(string name)
    {
        if (McpProfileValidation.ValidateName(name) is { } nameError) return Fail(nameError);

        bool deleted;
        try
        {
            deleted = store.Delete(name);
        }
        catch (Exception ex) when (ex is ConfigStoreException or ArgumentException)
        {
            return Fail(ex.Message);
        }

        if (!deleted) return Fail($"MCP profile '{name}' not found.");

        AnsiConsole.MarkupLine($"[green]Deleted MCP profile '{Markup.Escape(name)}'[/]");
        if (name == McpProfiles.DefaultName)
            AnsiConsole.MarkupLine("Built-in default profile restored.");
        return 0;
    }

    private static int Fail(string message)
    {
        AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(message)}");
        return 1;
    }
}
