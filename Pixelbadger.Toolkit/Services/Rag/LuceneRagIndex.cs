using System.Globalization;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Analysis.Miscellaneous;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Search.Similarities;
using Lucene.Net.Store;
using Lucene.Net.Util;
using SysDirectory = System.IO.Directory;

namespace Pixelbadger.Toolkit.Services.Rag;

public sealed class LuceneRagIndex : IRagIndex
{
    private const LuceneVersion Version = LuceneVersion.LUCENE_48;
    private const string Header = "header";
    private const string Chunk = "chunk";
    private static readonly TimeSpan DefaultLockTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan LockPollInterval = TimeSpan.FromMilliseconds(100);

    public const int MaxQueryTerms = 200;
    public const string SchemaKey = "rag_schema";
    public const string SchemaValue = "2";

    private readonly TimeSpan _lockTimeout;

    public LuceneRagIndex(string indexPath, TimeSpan? lockTimeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexPath);
        IndexPath = Path.GetFullPath(indexPath);
        _lockTimeout = lockTimeout ?? DefaultLockTimeout;
    }

    public string IndexPath { get; }

    public RagDocumentHeader? GetHeader(string id)
    {
        return WithSearcher<RagDocumentHeader?>(null, searcher =>
        {
            var hit = FindHeader(searcher, id);
            return hit is null ? null : ToHeader(searcher.Doc(hit.Value));
        });
    }

    public RagDocument? Get(string id, int? fromChunk = null, int? toChunk = null)
    {
        return WithSearcher<RagDocument?>(null, searcher =>
        {
            var hit = FindHeader(searcher, id);
            if (hit is null) return null;
            var header = ToHeader(searcher.Doc(hit.Value));

            var from = Math.Max(0, fromChunk ?? 0);
            var to = Math.Min(toChunk ?? int.MaxValue, header.ChunkCount - 1);
            if (header.ChunkCount == 0 || from > to) return new RagDocument(header, Array.Empty<TextChunk>());

            var query = new BooleanQuery
            {
                { new TermQuery(new Term("doc_id", header.Id)), Occur.MUST },
                { new TermQuery(new Term("kind", Chunk)), Occur.MUST },
                { NumericRangeQuery.NewInt32Range("chunk_index", from, to, true, true), Occur.MUST },
            };
            var top = searcher.Search(query, to - from + 1);
            var chunks = top.ScoreDocs
                .Select(sd => ToChunk(searcher.Doc(sd.Doc)))
                .OrderBy(c => c.Index)
                .ToList();
            return new RagDocument(header, chunks);
        });
    }

    public RagPage<RagDocumentHeader> List(int page, int pageSize)
    {
        ValidatePaging(page, pageSize);
        var empty = new RagPage<RagDocumentHeader>(page, pageSize, 0, Array.Empty<RagDocumentHeader>());
        return WithSearcher(empty, searcher =>
        {
            var query = new TermQuery(new Term("kind", Header));
            var sort = new Sort(
                new SortField("created_sort", SortFieldType.STRING),
                new SortField("doc_id_sort", SortFieldType.STRING));
            var top = searcher.Search(query, null, SafeWindow(page, pageSize), sort);
            var items = top.ScoreDocs
                .Skip((page - 1) * pageSize)
                .Select(sd => ToHeader(searcher.Doc(sd.Doc)))
                .ToList();
            return new RagPage<RagDocumentHeader>(page, pageSize, top.TotalHits, items);
        });
    }

    public RagPage<RagSearchHit> Search(string query, int page, int pageSize)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidatePaging(page, pageSize);

        var terms = RagTextAnalyzer.Tokenize(query).Distinct(StringComparer.Ordinal).ToList();
        if (terms.Count == 0) throw new RagQueryException("Query has no searchable terms.");
        if (terms.Count > MaxQueryTerms) throw new RagQueryException($"Query has too many terms (max {MaxQueryTerms}).");

        var empty = new RagPage<RagSearchHit>(page, pageSize, 0, Array.Empty<RagSearchHit>());
        return WithSearcher(empty, searcher =>
        {
            var anyTerm = new BooleanQuery();
            foreach (var term in terms)
            {
                anyTerm.Add(new TermQuery(new Term("content", term)), Occur.SHOULD);
                anyTerm.Add(new TermQuery(new Term("title", term)), Occur.SHOULD);
            }
            var combined = new BooleanQuery
            {
                { anyTerm, Occur.MUST },
                { new TermQuery(new Term("kind", Chunk)), Occur.MUST },
            };
            var top = searcher.Search(combined, SafeWindow(page, pageSize));

            var headers = new Dictionary<string, RagDocumentHeader?>(StringComparer.Ordinal);
            var hits = top.ScoreDocs
                .Skip((page - 1) * pageSize)
                .Select(sd =>
                {
                    var d = searcher.Doc(sd.Doc);
                    var id = d.Get("doc_id");
                    if (!headers.TryGetValue(id, out var header))
                    {
                        var h = FindHeader(searcher, id);
                        headers[id] = header = h is null ? null : ToHeader(searcher.Doc(h.Value));
                    }
                    return new RagSearchHit(
                        id, d.Get("title") ?? string.Empty, GetInt(d, "chunk_index"), header?.ChunkCount ?? 0, sd.Score,
                        GetInt(d, "start"), GetInt(d, "end"), header?.CharCount ?? 0, d.Get("content") ?? string.Empty);
                })
                .ToList();
            return new RagPage<RagSearchHit>(page, pageSize, top.TotalHits, hits);
        });
    }

    public void Upsert(RagDocument document, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        WithWriter<object?>(ct, writer =>
        {
            ReplaceDocuments(writer, document, document.Header, ct);
            return (true, null);
        });
    }

    public RagDocumentHeader? Replace(RagDocument document, bool keepTitle, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!IndexExists()) return null;
        return WithWriter<RagDocumentHeader?>(ct, writer =>
        {
            // Checked under the write lock so a concurrent delete cannot be undone by this update.
            RagDocumentHeader existing;
            using (var reader = DirectoryReader.Open(writer, true))
            {
                var searcher = new IndexSearcher(reader);
                var hit = FindHeader(searcher, document.Header.Id);
                if (hit is null) return (false, null);
                existing = ToHeader(searcher.Doc(hit.Value));
            }
            var stored = document.Header with { CreatedUtc = existing.CreatedUtc };
            if (keepTitle) stored = stored with { Title = existing.Title };
            ReplaceDocuments(writer, document, stored, ct);
            return (true, stored);
        });
    }

    public bool Delete(string id, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (!IndexExists()) return false;

        return WithWriter(ct, writer =>
        {
            // Checked under the write lock so concurrent writers cannot race the existence test.
            using (var reader = DirectoryReader.Open(writer, true))
            {
                var searcher = new IndexSearcher(reader);
                if (FindHeader(searcher, id) is null) return (false, false);
            }
            writer.DeleteDocuments(new Term("doc_id", id));
            ct.ThrowIfCancellationRequested();
            return (true, true);
        });
    }

    private static void ReplaceDocuments(IndexWriter writer, RagDocument document, RagDocumentHeader header, CancellationToken ct)
    {
        writer.DeleteDocuments(new Term("doc_id", header.Id));
        writer.AddDocument(BuildHeaderDocument(header));
        foreach (var chunk in document.Chunks)
        {
            ct.ThrowIfCancellationRequested();
            writer.AddDocument(BuildChunkDocument(header, chunk));
        }
        ct.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// Opens the writer under the write lock, runs <paramref name="work"/> and commits when it asks to. The writer is
    /// closed on every path: a failure before the commit, or while closing, rolls back so the lock is never left held.
    /// </summary>
    private T WithWriter<T>(CancellationToken ct, Func<IndexWriter, (bool Commit, T Result)> work)
    {
        using var dir = FSDirectory.Open(IndexPath);
        using var analyzer = CreateAnalyzer();
        var writer = OpenWriter(dir, analyzer, ct);
        T result;
        try
        {
            CheckSchema(dir);
            var outcome = work(writer);
            if (!outcome.Commit)
            {
                writer.Rollback();
                return outcome.Result;
            }
            FaultHook?.Invoke("beforeCommit");
            writer.SetCommitData(new Dictionary<string, string> { [SchemaKey] = SchemaValue });
            writer.Commit();
            result = outcome.Result;
        }
        catch
        {
            SafeRollback(writer);
            throw;
        }

        try
        {
            FaultHook?.Invoke("beforeClose");
            writer.Dispose();
        }
        catch
        {
            SafeRollback(writer);
            throw;
        }
        return result;
    }

    private static void SafeRollback(IndexWriter writer)
    {
        try { writer.Rollback(); }
        catch { /* the original failure is the one to surface */ }
    }

    /// <summary>Test seam: invoked with "beforeCommit" and "beforeClose" while a writer session runs.</summary>
    internal Action<string>? FaultHook { get; set; }

    // ---- infrastructure ----

    private void CheckSchema(Lucene.Net.Store.Directory dir)
    {
        if (!DirectoryReader.IndexExists(dir)) return;
        using var reader = DirectoryReader.Open(dir);
        CheckSchema(reader);
    }

    private void CheckSchema(DirectoryReader reader)
    {
        if (reader.MaxDoc == 0) return;
        if (reader.IndexCommit.UserData.TryGetValue(SchemaKey, out var value) && value == SchemaValue) return;
        throw new RagIndexSchemaException($"Index '{IndexPath}' was built by an older pbtk version; delete it and re-ingest.");
    }

    private static Analyzer CreateAnalyzer()
    {
        var keyword = new KeywordAnalyzer();
        return new PerFieldAnalyzerWrapper(
            new RagTextAnalyzer(),
            new Dictionary<string, Analyzer>
            {
                ["doc_id"] = keyword,
                ["kind"] = keyword,
                ["file_name"] = keyword,
                ["source_path"] = keyword,
            });
    }

    private IndexWriter OpenWriter(Lucene.Net.Store.Directory dir, Analyzer analyzer, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + _lockTimeout;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var config = new IndexWriterConfig(Version, analyzer)
            {
                OpenMode = OpenMode.CREATE_OR_APPEND,
                Similarity = new BM25Similarity(),
                WriteLockTimeout = 0,
            };
            try
            {
                return new IndexWriter(dir, config);
            }
            catch (LockObtainFailedException)
            {
                if (DateTime.UtcNow >= deadline)
                    throw new RagIndexLockedException($"Index '{IndexPath}' is locked by another writer");
                var remaining = deadline - DateTime.UtcNow;
                var wait = remaining < LockPollInterval ? remaining : LockPollInterval;
                if (wait > TimeSpan.Zero && ct.WaitHandle.WaitOne(wait))
                    ct.ThrowIfCancellationRequested();
            }
        }
    }

    private bool IndexExists()
    {
        if (!SysDirectory.Exists(IndexPath)) return false;
        using var dir = FSDirectory.Open(IndexPath);
        return DirectoryReader.IndexExists(dir);
    }

    private T WithSearcher<T>(T whenMissing, Func<IndexSearcher, T> body)
    {
        if (!IndexExists()) return whenMissing;
        using var dir = FSDirectory.Open(IndexPath);
        using var reader = DirectoryReader.Open(dir);
        CheckSchema(reader);
        var searcher = new IndexSearcher(reader) { Similarity = new BM25Similarity() };
        return body(searcher);
    }

    private static void ValidatePaging(int page, int pageSize)
    {
        if (page < 1) throw new ArgumentOutOfRangeException(nameof(page), page, "page must be at least 1.");
        if (pageSize < 1) throw new ArgumentOutOfRangeException(nameof(pageSize), pageSize, "page-size must be at least 1.");
    }

    private static int SafeWindow(int page, int pageSize)
    {
        var window = (long)page * pageSize;
        return (int)Math.Min(window, int.MaxValue - 1);
    }

    private static int? FindHeader(IndexSearcher searcher, string id)
    {
        var query = new BooleanQuery
        {
            { new TermQuery(new Term("doc_id", id)), Occur.MUST },
            { new TermQuery(new Term("kind", Header)), Occur.MUST },
        };
        var top = searcher.Search(query, 1);
        return top.TotalHits == 0 ? null : top.ScoreDocs[0].Doc;
    }

    // ---- document mapping ----

    private static Document BuildHeaderDocument(RagDocumentHeader h)
    {
        var doc = new Document
        {
            new StringField("kind", Header, Field.Store.YES),
            new StringField("doc_id", h.Id, Field.Store.YES),
            new TextField("title", h.Title, Field.Store.YES),
            new StringField("file_name", h.FileName, Field.Store.YES),
            new StringField("source_path", h.SourcePath, Field.Store.YES),
            new StoredField("created_utc", h.CreatedUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)),
            new StoredField("updated_utc", h.UpdatedUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)),
            new StoredField("chunk_count", h.ChunkCount),
            new StoredField("char_count", h.CharCount),
            new StoredField("sha256", h.Sha256),
            new StoredField("chunk_size", h.ChunkSize),
            new StoredField("chunk_overlap", h.ChunkOverlap),
            new SortedDocValuesField("created_sort", new BytesRef(h.CreatedUtc.UtcTicks.ToString("D19", CultureInfo.InvariantCulture))),
            new SortedDocValuesField("doc_id_sort", new BytesRef(h.Id)),
        };
        return doc;
    }

    private static Document BuildChunkDocument(RagDocumentHeader h, TextChunk c)
    {
        return new Document
        {
            new StringField("kind", Chunk, Field.Store.YES),
            new StringField("doc_id", h.Id, Field.Store.YES),
            new Int32Field("chunk_index", c.Index, Field.Store.YES),
            new TextField("content", c.Content, Field.Store.YES),
            new TextField("title", h.Title, Field.Store.YES),
            new StoredField("start", c.Start),
            new StoredField("end", c.End),
        };
    }

    private static int GetInt(Document d, string name) =>
        int.Parse(d.Get(name), CultureInfo.InvariantCulture);

    private static DateTimeOffset GetDate(Document d, string name) =>
        DateTimeOffset.Parse(d.Get(name), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();

    private static RagDocumentHeader ToHeader(Document d) => new(
        d.Get("doc_id"), d.Get("title") ?? string.Empty, d.Get("file_name") ?? string.Empty, d.Get("source_path") ?? string.Empty,
        GetDate(d, "created_utc"), GetDate(d, "updated_utc"),
        GetInt(d, "chunk_count"), GetInt(d, "char_count"), d.Get("sha256") ?? string.Empty,
        GetInt(d, "chunk_size"), GetInt(d, "chunk_overlap"));

    private static TextChunk ToChunk(Document d) =>
        new(GetInt(d, "chunk_index"), GetInt(d, "start"), GetInt(d, "end"), d.Get("content") ?? string.Empty);
}
