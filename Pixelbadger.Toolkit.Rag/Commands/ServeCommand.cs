using System.CommandLine;
using Pixelbadger.Toolkit.Rag.Components;

namespace Pixelbadger.Toolkit.Rag.Commands;

public static class ServeCommand
{
    public static Command Create()
    {
        var command = new Command("serve", "Host an MCP server that performs BM25 queries against a Lucene.NET index");

        var indexPathOption = new Option<string?>(
            aliases: ["--index-path"],
            description: "Path to the Lucene.NET index directory (uses config default if not specified)")
        {
            IsRequired = false
        };

        command.AddOption(indexPathOption);

        command.SetHandler(async (string? indexPath) =>
        {
            try
            {
                var configManager = new ConfigurationManager();
                var config = configManager.LoadConfig();

                // Use provided value or fall back to config
                var effectiveIndexPath = indexPath ?? config.DefaultIndexPath;

                if (string.IsNullOrWhiteSpace(effectiveIndexPath))
                {
                    Console.WriteLine("Error: --index-path is required (or set a default with 'pbrag config set index-path <path>')");
                    Environment.Exit(1);
                    return;
                }

                if (!Directory.Exists(effectiveIndexPath))
                {
                    Console.WriteLine($"Error: Index directory '{effectiveIndexPath}' not found.");
                    Environment.Exit(1);
                    return;
                }

                var server = new McpRagServer(effectiveIndexPath);
                await server.RunAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        }, indexPathOption);

        return command;
    }
}
