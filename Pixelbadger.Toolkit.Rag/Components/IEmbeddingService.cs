namespace Pixelbadger.Toolkit.Rag.Components;

/// <summary>
/// Interface for generating text embeddings
/// </summary>
public interface IEmbeddingService
{
    /// <summary>
    /// Generate embeddings for a batch of texts
    /// </summary>
    Task<List<float[]>> GenerateEmbeddingsAsync(List<string> texts);
}
