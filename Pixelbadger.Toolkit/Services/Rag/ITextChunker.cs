namespace Pixelbadger.Toolkit.Services.Rag;

public interface ITextChunker
{
    /// <exception cref="ArgumentOutOfRangeException">chunkSize outside 100-100000, or chunkOverlap outside 0..chunkSize/2.</exception>
    ChunkingResult Chunk(string rawText, int chunkSize, int chunkOverlap);
}
