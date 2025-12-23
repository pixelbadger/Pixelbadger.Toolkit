using OpenAI.Embeddings;

namespace Pixelbadger.Toolkit.Rag.Components;

public class SemanticChunk
{
    public string Content { get; set; } = string.Empty;
    public int ChunkNumber { get; set; }
}

public static class SemanticChunker
{
    private const string DefaultModel = "text-embedding-3-large";
    private const double DefaultPercentileThreshold = 0.95;

    public static async Task<List<SemanticChunk>> ChunkBySemanticSimilarityAsync(
        string content,
        string? apiKey = null,
        double percentileThreshold = DefaultPercentileThreshold,
        int bufferSize = 1)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return new List<SemanticChunk>();
        }

        // Get API key from environment if not provided
        apiKey ??= Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException("OpenAI API key must be provided or set in OPENAI_API_KEY environment variable");
        }

        // Step 1: Split into sentences
        var sentences = SplitIntoSentences(content);
        if (sentences.Count == 0)
        {
            return new List<SemanticChunk>();
        }

        // Single sentence returns single chunk
        if (sentences.Count == 1)
        {
            return new List<SemanticChunk>
            {
                new SemanticChunk { Content = sentences[0], ChunkNumber = 1 }
            };
        }

        // Step 2: Generate embeddings with buffer context
        var client = new EmbeddingClient(DefaultModel, apiKey);
        var sentencesWithContext = GenerateSentencesWithContext(sentences, bufferSize);
        var embeddings = await GenerateEmbeddingsAsync(client, sentencesWithContext);

        // Step 3: Calculate distances between consecutive embeddings
        var distances = CalculateCosineDistances(embeddings);

        // Step 4: Identify breakpoints using percentile threshold
        var breakpoints = IdentifyBreakpoints(distances, percentileThreshold);

        // Step 5: Create chunks based on breakpoints
        var chunks = CreateChunksFromBreakpoints(sentences, breakpoints);

        return chunks;
    }

    private static List<string> SplitIntoSentences(string content)
    {
        // Split on sentence-ending punctuation followed by whitespace
        // This is a simple implementation; could be enhanced with more sophisticated NLP
        var sentencePattern = @"(?<=[.!?])\s+";
        var sentences = System.Text.RegularExpressions.Regex
            .Split(content, sentencePattern)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        return sentences;
    }

    private static List<string> GenerateSentencesWithContext(List<string> sentences, int bufferSize)
    {
        var sentencesWithContext = new List<string>();

        for (int i = 0; i < sentences.Count; i++)
        {
            var start = Math.Max(0, i - bufferSize);
            var end = Math.Min(sentences.Count - 1, i + bufferSize);

            var contextSentences = new List<string>();
            for (int j = start; j <= end; j++)
            {
                contextSentences.Add(sentences[j]);
            }

            sentencesWithContext.Add(string.Join(" ", contextSentences));
        }

        return sentencesWithContext;
    }

    private static async Task<List<float[]>> GenerateEmbeddingsAsync(
        EmbeddingClient client,
        List<string> texts)
    {
        var embeddings = new List<float[]>();

        foreach (var text in texts)
        {
            var response = await client.GenerateEmbeddingAsync(text);
            var embedding = response.Value.ToFloats().ToArray();
            embeddings.Add(embedding);
        }

        return embeddings;
    }

    private static List<double> CalculateCosineDistances(List<float[]> embeddings)
    {
        var distances = new List<double>();

        for (int i = 0; i < embeddings.Count - 1; i++)
        {
            var distance = 1.0 - CosineSimilarity(embeddings[i], embeddings[i + 1]);
            distances.Add(distance);
        }

        return distances;
    }

    private static double CosineSimilarity(float[] vectorA, float[] vectorB)
    {
        if (vectorA.Length != vectorB.Length)
        {
            throw new ArgumentException("Vectors must have the same dimension");
        }

        double dotProduct = 0;
        double magnitudeA = 0;
        double magnitudeB = 0;

        for (int i = 0; i < vectorA.Length; i++)
        {
            dotProduct += vectorA[i] * vectorB[i];
            magnitudeA += vectorA[i] * vectorA[i];
            magnitudeB += vectorB[i] * vectorB[i];
        }

        magnitudeA = Math.Sqrt(magnitudeA);
        magnitudeB = Math.Sqrt(magnitudeB);

        if (magnitudeA == 0 || magnitudeB == 0)
        {
            return 0;
        }

        return dotProduct / (magnitudeA * magnitudeB);
    }

    private static List<int> IdentifyBreakpoints(List<double> distances, double percentileThreshold)
    {
        if (distances.Count == 0)
        {
            return new List<int>();
        }

        // Calculate the threshold value at the specified percentile
        var sortedDistances = distances.OrderBy(d => d).ToList();
        var thresholdIndex = (int)Math.Ceiling(sortedDistances.Count * percentileThreshold) - 1;
        thresholdIndex = Math.Max(0, Math.Min(thresholdIndex, sortedDistances.Count - 1));
        var threshold = sortedDistances[thresholdIndex];

        // Identify positions where distance exceeds threshold
        var breakpoints = new List<int>();
        for (int i = 0; i < distances.Count; i++)
        {
            if (distances[i] >= threshold)
            {
                breakpoints.Add(i + 1); // Breakpoint is after sentence i
            }
        }

        return breakpoints;
    }

    private static List<SemanticChunk> CreateChunksFromBreakpoints(
        List<string> sentences,
        List<int> breakpoints)
    {
        var chunks = new List<SemanticChunk>();
        var currentChunkSentences = new List<string>();
        var chunkNumber = 1;
        var sentenceIndex = 0;

        foreach (var sentence in sentences)
        {
            currentChunkSentences.Add(sentence);

            // Check if we've reached a breakpoint
            if (breakpoints.Contains(sentenceIndex + 1) || sentenceIndex == sentences.Count - 1)
            {
                chunks.Add(new SemanticChunk
                {
                    Content = string.Join(" ", currentChunkSentences),
                    ChunkNumber = chunkNumber++
                });
                currentChunkSentences.Clear();
            }

            sentenceIndex++;
        }

        // Add any remaining sentences as final chunk
        if (currentChunkSentences.Count > 0)
        {
            chunks.Add(new SemanticChunk
            {
                Content = string.Join(" ", currentChunkSentences),
                ChunkNumber = chunkNumber
            });
        }

        return chunks;
    }
}
