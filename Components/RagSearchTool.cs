using System.Text.Json;
using ModelContextProtocol;

namespace Pixelbadger.Toolkit.Components;

public class McpRagServer
{
    private readonly string _indexPath;
    private readonly SearchIndexer _searchIndexer;

    public McpRagServer(string indexPath)
    {
        _indexPath = indexPath;
        _searchIndexer = new SearchIndexer();
    }

    public async Task RunAsync()
    {
        Console.WriteLine($"MCP RAG Server started with index: {_indexPath}");
        Console.WriteLine("Communicating via stdio...");

        while (true)
        {
            try
            {
                var line = Console.ReadLine();
                if (string.IsNullOrEmpty(line)) break;

                var request = JsonSerializer.Deserialize<JsonElement>(line);
                var response = await HandleRequest(request);
                
                if (response != null)
                {
                    Console.WriteLine(JsonSerializer.Serialize(response));
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error processing request: {ex.Message}");
            }
        }
    }

    private async Task<object?> HandleRequest(JsonElement request)
    {
        if (!request.TryGetProperty("method", out var methodElement))
            return null;

        var method = methodElement.GetString();
        switch (method)
        {
            case "tools/list":
                return new
                {
                    tools = new object[]
                    {
                        new
                        {
                            name = "search",
                            description = "Performs BM25 similarity search against a Lucene.NET index",
                            inputSchema = new
                            {
                                type = "object",
                                properties = new
                                {
                                    query = new { type = "string", description = "The search query" },
                                    maxResults = new { type = "integer", description = "Maximum results (default: 5)" }
                                },
                                required = new[] { "query" }
                            }
                        },
                        new
                        {
                            name = "index_info",
                            description = "Get information about the search index",
                            inputSchema = new { type = "object", properties = new { } }
                        }
                    }
                };

            case "tools/call":
                if (request.TryGetProperty("params", out var paramsElement))
                {
                    return await HandleToolCall(paramsElement);
                }
                break;
        }

        return null;
    }

    private async Task<object> HandleToolCall(JsonElement parameters)
    {
        if (!parameters.TryGetProperty("name", out var nameElement))
            return new { error = "Tool name not specified" };

        var toolName = nameElement.GetString();
        var arguments = parameters.TryGetProperty("arguments", out var argsElement) ? argsElement : new JsonElement();

        switch (toolName)
        {
            case "search":
                var query = arguments.TryGetProperty("query", out var queryElement) ? queryElement.GetString() : "";
                var maxResults = arguments.TryGetProperty("maxResults", out var maxElement) ? maxElement.GetInt32() : 5;
                
                if (string.IsNullOrEmpty(query))
                    return new { error = "Query is required" };

                try
                {
                    if (!Directory.Exists(_indexPath))
                        return new { error = $"Index directory '{_indexPath}' not found." };

                    var results = await _searchIndexer.QueryAsync(_indexPath, query, maxResults);
                    return new { content = FormatSearchResults(results) };
                }
                catch (Exception ex)
                {
                    return new { error = $"Search failed: {ex.Message}" };
                }

            case "index_info":
                try
                {
                    if (!Directory.Exists(_indexPath))
                        return new { error = $"Index directory '{_indexPath}' not found." };

                    var indexFiles = Directory.GetFiles(_indexPath);
                    var totalSize = indexFiles.Sum(f => new FileInfo(f).Length);
                    
                    return new 
                    { 
                        content = $"Index Path: {_indexPath}\nFiles: {indexFiles.Length}\nTotal Size: {totalSize:N0} bytes\nStatus: Available" 
                    };
                }
                catch (Exception ex)
                {
                    return new { error = $"Failed to get index info: {ex.Message}" };
                }

            default:
                return new { error = $"Unknown tool: {toolName}" };
        }
    }

    private string FormatSearchResults(List<SearchResult> results)
    {
        if (results.Count == 0)
            return "No relevant documents found for the query.";

        var response = $"Found {results.Count} relevant document(s):\n\n";
        
        for (int i = 0; i < results.Count; i++)
        {
            var result = results[i];
            response += $"Document {i + 1} (Score: {result.Score:F4})\n";
            response += $"Source: {result.SourceFile} (Paragraph {result.ParagraphNumber})\n";
            response += $"Content: {result.Content}\n";
            
            if (i < results.Count - 1)
                response += "\n" + new string('-', 60) + "\n\n";
        }

        return response;
    }
}