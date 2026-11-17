namespace Pixelbadger.Toolkit.Services.Rag;

public interface IRagIndex
{
    string IndexPath { get; }
    RagDocumentHeader? GetHeader(string id);
    RagDocument? Get(string id);
    RagPage<RagDocumentHeader> List(int page, int pageSize);
    RagPage<RagSearchHit> Search(string query, int page, int pageSize);
    void Upsert(RagDocument document, CancellationToken ct = default);
    bool Delete(string id, CancellationToken ct = default);
}
