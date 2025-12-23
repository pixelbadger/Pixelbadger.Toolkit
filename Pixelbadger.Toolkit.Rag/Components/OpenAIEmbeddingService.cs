using OpenAI.Embeddings;

namespace Pixelbadger.Toolkit.Rag.Components;

/// <summary>
/// OpenAI implementation of embedding service using text-embedding-3-large
/// </summary>
public class OpenAIEmbeddingService : IEmbeddingService
{
    private const string DefaultModel = "text-embedding-3-large";
    private const int BatchSize = 100; // Process 100 sentences per batch for optimal performance

    private readonly EmbeddingClient _client;

    public OpenAIEmbeddingService(string? apiKey = null)
    {
        // Get API key from environment if not provided
        apiKey ??= Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException("OpenAI API key must be provided or set in OPENAI_API_KEY environment variable");
        }

        _client = new EmbeddingClient(DefaultModel, apiKey);
    }

    public async Task<List<float[]>> GenerateEmbeddingsAsync(List<string> texts)
    {
        var allEmbeddings = new List<float[]>();

        // Process in batches to avoid rate limits and improve performance
        for (int i = 0; i < texts.Count; i += BatchSize)
        {
            var batch = texts.Skip(i).Take(BatchSize).ToList();

            // Generate embeddings for entire batch at once
            var response = await _client.GenerateEmbeddingsAsync(batch);

            foreach (var embedding in response.Value)
            {
                allEmbeddings.Add(embedding.ToFloats().ToArray());
            }
        }

        return allEmbeddings;
    }
}
