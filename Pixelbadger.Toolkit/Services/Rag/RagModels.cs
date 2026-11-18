namespace Pixelbadger.Toolkit.Services.Rag;

public sealed record RagDocumentHeader(
    string Id, string Title, string FileName, string SourcePath,
    DateTimeOffset CreatedUtc, DateTimeOffset UpdatedUtc,
    int ChunkCount, int CharCount, string Sha256, int ChunkSize, int ChunkOverlap);

public sealed record RagDocument(RagDocumentHeader Header, IReadOnlyList<TextChunk> Chunks);

public sealed record RagSearchHit(string Id, string Title, int ChunkIndex, float Score, int Start, int End, string Content);

public sealed record RagPage<T>(int Page, int PageSize, int Total, IReadOnlyList<T> Items)
{
    public int TotalPages => Total == 0 ? 0 : (int)(((long)Total + PageSize - 1) / PageSize);
}

public sealed class RagQueryException : Exception
{
    public RagQueryException(string message) : base(message) { }
    public RagQueryException(string message, Exception inner) : base(message, inner) { }
}

public sealed class RagIndexLockedException : Exception
{
    public RagIndexLockedException(string message) : base(message) { }
}

/// <summary>The index path is unusable, or no home directory exists to place a named index in.</summary>
public sealed class RagIndexPathException : Exception
{
    public RagIndexPathException(string message) : base(message) { }
    public RagIndexPathException(string message, Exception inner) : base(message, inner) { }
}
