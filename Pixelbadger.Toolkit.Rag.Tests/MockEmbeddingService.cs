using Pixelbadger.Toolkit.Rag.Components;

namespace Pixelbadger.Toolkit.Rag.Tests;

/// <summary>
/// Mock embedding service for testing that generates deterministic embeddings based on text content
/// </summary>
public class MockEmbeddingService : IEmbeddingService
{
    public Task<List<float[]>> GenerateEmbeddingsAsync(List<string> texts)
    {
        var embeddings = new List<float[]>();

        foreach (var text in texts)
        {
            // Generate a deterministic embedding based on text content
            // Use simple heuristics to create semantic similarity:
            // - Similar word counts produce similar embeddings
            // - Similar starting characters produce similar embeddings
            var embedding = GenerateDeterministicEmbedding(text);
            embeddings.Add(embedding);
        }

        return Task.FromResult(embeddings);
    }

    private static float[] GenerateDeterministicEmbedding(string text)
    {
        // Create a 10-dimensional embedding (simplified vs real 3072-dimensional embeddings)
        var embedding = new float[10];

        if (string.IsNullOrWhiteSpace(text))
        {
            return embedding;
        }

        // Dimension 0-2: Based on word count (for semantic similarity)
        var wordCount = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        embedding[0] = (float)wordCount / 10f;
        embedding[1] = (float)Math.Sin(wordCount);
        embedding[2] = (float)Math.Cos(wordCount);

        // Dimension 3-5: Based on character length
        embedding[3] = (float)text.Length / 100f;
        embedding[4] = (float)Math.Sin(text.Length);
        embedding[5] = (float)Math.Cos(text.Length);

        // Dimension 6-7: Based on first character (for sentence variation)
        var firstChar = char.ToLowerInvariant(text.Trim()[0]);
        embedding[6] = (float)firstChar / 127f;
        embedding[7] = (float)Math.Sin(firstChar);

        // Dimension 8-9: Based on last character
        var lastChar = char.ToLowerInvariant(text.Trim()[^1]);
        embedding[8] = (float)lastChar / 127f;
        embedding[9] = (float)Math.Cos(lastChar);

        // Normalize the embedding
        var magnitude = (float)Math.Sqrt(embedding.Sum(x => x * x));
        if (magnitude > 0)
        {
            for (int i = 0; i < embedding.Length; i++)
            {
                embedding[i] /= magnitude;
            }
        }

        return embedding;
    }
}
