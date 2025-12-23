namespace Pixelbadger.Toolkit.Rag.Components;

public class SemanticChunk
{
    public string Content { get; set; } = string.Empty;
    public int ChunkNumber { get; set; }
}

public static class SemanticChunker
{
    private const double DefaultPercentileThreshold = 0.95;

    public static async Task<List<SemanticChunk>> ChunkBySemanticSimilarityAsync(
        string content,
        IEmbeddingService embeddingService,
        double percentileThreshold = DefaultPercentileThreshold,
        int bufferSize = 1)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return new List<SemanticChunk>();
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
        var sentencesWithContext = GenerateSentencesWithContext(sentences, bufferSize);
        var embeddings = await embeddingService.GenerateEmbeddingsAsync(sentencesWithContext);

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
        // LIMITATIONS: This simple regex-based approach may struggle with:
        // - Abbreviations (Dr. Smith, Ph.D., etc.)
        // - Decimal numbers (3.14159)
        // - URLs and email addresses
        // - Quoted sentences
        // For production use with complex text, consider a proper NLP library like NLTk or spaCy
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

        // Edge case safeguard: If no breakpoints found (uniform text), create breakpoints every 50 sentences
        // to prevent unbounded chunk sizes
        if (breakpoints.Count == 0 && distances.Count > 50)
        {
            for (int i = 50; i < distances.Count; i += 50)
            {
                breakpoints.Add(i);
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

        for (int i = 0; i < sentences.Count; i++)
        {
            currentChunkSentences.Add(sentences[i]);

            // Check if we've reached a breakpoint or the end of sentences
            bool isBreakpoint = breakpoints.Contains(i + 1);
            bool isLastSentence = i == sentences.Count - 1;

            if (isBreakpoint || isLastSentence)
            {
                chunks.Add(new SemanticChunk
                {
                    Content = string.Join(" ", currentChunkSentences),
                    ChunkNumber = chunkNumber++
                });
                currentChunkSentences.Clear();
            }
        }

        return chunks;
    }
}
