using System.CommandLine;
using Pixelbadger.Toolkit.Rag.Components;

namespace Pixelbadger.Toolkit.Rag.Commands;

public static class IngestCommand
{
    public static Command Create()
    {
        var command = new Command("ingest", "Ingest content into a search index with intelligent chunking based on file type");

        var indexPathOption = new Option<string?>(
            aliases: ["--index-path"],
            description: "Path to the Lucene.NET index directory (uses config default if not specified)")
        {
            IsRequired = false
        };

        var contentPathOption = new Option<string>(
            aliases: ["--content-path"],
            description: "Path to the content file to ingest")
        {
            IsRequired = true
        };

        var chunkingStrategyOption = new Option<string?>(
            aliases: ["--chunking-strategy"],
            description: "Chunking strategy: 'semantic', 'markdown', or 'paragraph' (uses config default or auto-detect if not specified)")
        {
            IsRequired = false
        };

        command.AddOption(indexPathOption);
        command.AddOption(contentPathOption);
        command.AddOption(chunkingStrategyOption);

        command.SetHandler(async (string? indexPath, string contentPath, string? chunkingStrategy) =>
        {
            try
            {
                var configManager = new ConfigurationManager();
                var config = configManager.LoadConfig();

                // Use provided value or fall back to config
                var effectiveIndexPath = indexPath ?? config.DefaultIndexPath;
                var effectiveChunkingStrategy = chunkingStrategy ?? config.DefaultChunkingStrategy;

                if (string.IsNullOrWhiteSpace(effectiveIndexPath))
                {
                    Console.WriteLine("Error: --index-path is required (or set a default with 'pbrag config set index-path <path>')");
                    Environment.Exit(1);
                    return;
                }

                var indexer = new SearchIndexer();
                await indexer.IngestContentAsync(effectiveIndexPath, contentPath, effectiveChunkingStrategy);

                var strategyUsed = effectiveChunkingStrategy ?? "auto-detected";
                Console.WriteLine($"Successfully ingested content from '{contentPath}' into index at '{effectiveIndexPath}' using {strategyUsed} chunking");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        }, indexPathOption, contentPathOption, chunkingStrategyOption);

        return command;
    }
}
