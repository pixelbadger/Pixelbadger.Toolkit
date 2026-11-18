namespace Pixelbadger.Toolkit.Services.Rag;

public sealed record TextChunk(int Index, int Start, int End, string Content);

public sealed record ChunkingResult(string NormalizedText, IReadOnlyList<TextChunk> Chunks);
