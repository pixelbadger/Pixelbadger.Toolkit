using System.CommandLine;
using Pixelbadger.Toolkit.Components;

namespace Pixelbadger.Toolkit.Commands;

public static class SearchCommand
{
    public static Command Create()
    {
        var command = new Command("search", "Search indexing and querying utilities");

        command.AddCommand(CreateIngestCommand());
        command.AddCommand(CreateQueryCommand());

        return command;
    }

    private static Command CreateIngestCommand()
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

    private static Command CreateQueryCommand()
    {
        var command = new Command("query", "Perform BM25 similarity search against a Lucene.NET index");

        var indexPathOption = new Option<string>(
            aliases: ["--index-path"],
            description: "Path to the Lucene.NET index directory")
        {
            IsRequired = true
        };

        var queryOption = new Option<string>(
            aliases: ["--query"],
            description: "Search query text")
        {
            IsRequired = true
        };

        var maxResultsOption = new Option<int>(
            aliases: ["--max-results"],
            description: "Maximum number of results to return")
        {
            IsRequired = false
        };
        maxResultsOption.SetDefaultValue(10);

        command.AddOption(indexPathOption);
        command.AddOption(queryOption);
        command.AddOption(maxResultsOption);

        command.SetHandler(async (string indexPath, string query, int maxResults) =>
        {
            try
            {
                var indexer = new SearchIndexer();
                var results = await indexer.QueryAsync(indexPath, query, maxResults);
                
                if (results.Count == 0)
                {
                    Console.WriteLine("No results found.");
                    return;
                }

                Console.WriteLine($"Found {results.Count} result(s):");
                Console.WriteLine();

                for (int i = 0; i < results.Count; i++)
                {
                    var result = results[i];
                    Console.WriteLine($"Result {i + 1} (Score: {result.Score:F4})");
                    Console.WriteLine($"Source: {result.SourceFile} (Paragraph {result.ParagraphNumber})");
                    Console.WriteLine($"Content: {result.Content}");
                    
                    if (i < results.Count - 1)
                    {
                        Console.WriteLine(new string('-', 60));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        }, indexPathOption, queryOption, maxResultsOption);

        return command;
    }
}