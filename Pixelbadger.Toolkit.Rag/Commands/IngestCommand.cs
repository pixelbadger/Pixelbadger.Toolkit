using System.CommandLine;
using Pixelbadger.Toolkit.Rag.Components;

namespace Pixelbadger.Toolkit.Rag.Commands;

public static class IngestCommand
{
    public static Command Create()
    {
        var command = new Command("ingest", "Ingest content into a search index by paragraph chunks");

        var indexPathOption = new Option<string>(
            aliases: ["--index-path"],
            description: "Path to the Lucene.NET index directory")
        {
            IsRequired = true
        };

        var contentPathOption = new Option<string>(
            aliases: ["--content-path"],
            description: "Path to the content file to ingest")
        {
            IsRequired = true
        };

        command.AddOption(indexPathOption);
        command.AddOption(contentPathOption);

        command.SetHandler(async (string indexPath, string contentPath) =>
        {
            try
            {
                var indexer = new SearchIndexer();
                await indexer.IngestContentAsync(indexPath, contentPath);

                Console.WriteLine($"Successfully ingested content from '{contentPath}' into index at '{indexPath}'");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        }, indexPathOption, contentPathOption);

        return command;
    }
}
