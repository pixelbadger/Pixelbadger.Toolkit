namespace Pixelbadger.Toolkit.Services.Rag;

public interface IRagIndex
{
    string IndexPath { get; }
    RagDocumentHeader? GetHeader(string id);
    /// <summary>Gets a document with the chunks in the inclusive 0-based range (defaults: all), clamped to the last chunk.</summary>
    RagDocument? Get(string id, int? fromChunk = null, int? toChunk = null);
    RagPage<RagDocumentHeader> List(int page, int pageSize);
    RagPage<RagSearchHit> Search(string query, int page, int pageSize);
    void Upsert(RagDocument document, CancellationToken ct = default);
    /// <summary>Replaces an existing document under the write lock, keeping its CreatedUtc (and its title when keepTitle is set). Returns the stored header, or null when the ID is not in the index.</summary>
    RagDocumentHeader? Replace(RagDocument document, bool keepTitle, CancellationToken ct = default);
    bool Delete(string id, CancellationToken ct = default);
}
