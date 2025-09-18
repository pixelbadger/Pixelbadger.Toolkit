using System.CommandLine;
using Pixelbadger.Toolkit.Components;

namespace Pixelbadger.Toolkit.Commands;

public static class McpCommand
{
    public static Command Create()
    {
        var command = new Command("mcp", "Model Context Protocol server utilities");

        command.AddCommand(CreateRagServerCommand());

        return command;
    }

    private static Command CreateRagServerCommand()
    {
        var command = new Command("rag-server", "Host an MCP server that performs BM25 queries against a Lucene.NET index");

        var indexPathOption = new Option<string>(
            aliases: ["--index-path"],
            description: "Path to the Lucene.NET index directory")
        {
            IsRequired = true
        };

        command.AddOption(indexPathOption);

        command.SetHandler(async (string indexPath) =>
        {
            try
            {
                if (!Directory.Exists(indexPath))
                {
                    Console.WriteLine($"Error: Index directory '{indexPath}' not found.");
                    Environment.Exit(1);
                    return;
                }

                var server = new McpRagServer(indexPath);
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