using System.CommandLine;
using Pixelbadger.Toolkit.Commands;
using Pixelbadger.Toolkit.Services;
using Spectre.Console;

namespace Pixelbadger.Toolkit.CommandLine;

public static class ConfigBootstrap
{
    /// <summary>Builds the `config` topic from the fully-registered tree and adds it to root. Call after all other topics are added.</summary>
    public static void AddConfigTopic(RootCommand root, IConfigStore store)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(store);
        root.Add(ConfigCommand.Create(root, store));
    }

    /// <summary>Parses args applying the invoked topic's configured defaults, then invokes. Returns the exit code.</summary>
    public static async Task<int> InvokeAsync(RootCommand root, string[] args, IConfigStore store)
    {
        ParseResult parse;
        try
        {
            parse = ParseWithConfig(root, args, store, Console.Error);
        }
        catch (ConfigStoreException ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }

        return await parse.InvokeAsync();
    }

    internal static ParseResult ParseWithConfig(RootCommand root, string[] args, IConfigStore store, TextWriter warnings) =>
        ConfigDefaultsBinder.ParseWithConfig(root, args, store, warnings);
}
