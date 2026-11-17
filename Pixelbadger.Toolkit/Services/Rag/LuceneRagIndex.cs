using System.Globalization;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Analysis.Miscellaneous;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers.Classic;
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

    public RagDocument? Get(string id)
    {
        return WithSearcher<RagDocument?>(null, searcher =>
        {
            var hit = FindHeader(searcher, id);
            if (hit is null) return null;
            var header = ToHeader(searcher.Doc(hit.Value));

            var query = new BooleanQuery
            {
                { new TermQuery(new Term("doc_id", header.Id)), Occur.MUST },
                { new TermQuery(new Term("kind", Chunk)), Occur.MUST },
            };
            var top = searcher.Search(query, Math.Max(1, header.ChunkCount));
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

        Query parsed;
        try
        {
            var parser = new QueryParser(Version, "content", CreateAnalyzer()) { AllowLeadingWildcard = false };
            parsed = parser.Parse(query);
        }
        catch (ParseException ex)
        {
            throw new RagQueryException($"Invalid query: {FirstLine(ex.Message)}", ex);
        }
        catch (BooleanQuery.TooManyClausesException ex)
        {
            throw new RagQueryException("Invalid query: too many clauses.", ex);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException)
        {
            throw new RagQueryException($"Invalid query: {FirstLine(ex.Message)}", ex);
        }

        var empty = new RagPage<RagSearchHit>(page, pageSize, 0, Array.Empty<RagSearchHit>());
        return WithSearcher(empty, searcher =>
        {
            var combined = new BooleanQuery
            {
                { parsed, Occur.MUST },
                { new TermQuery(new Term("kind", Chunk)), Occur.MUST },
            };
            TopDocs top;
            try
            {
                top = searcher.Search(combined, SafeWindow(page, pageSize));
            }
            catch (BooleanQuery.TooManyClausesException ex)
            {
                throw new RagQueryException("Invalid query: too many clauses.", ex);
            }

            var hits = top.ScoreDocs
                .Skip((page - 1) * pageSize)
                .Select(sd =>
                {
                    var d = searcher.Doc(sd.Doc);
                    return new RagSearchHit(
                        d.Get("doc_id"), d.Get("title") ?? string.Empty, GetInt(d, "chunk_index"), sd.Score,
                        GetInt(d, "start"), GetInt(d, "end"), d.Get("content") ?? string.Empty);
                })
                .ToList();
            return new RagPage<RagSearchHit>(page, pageSize, top.TotalHits, hits);
        });
    }

    public void Upsert(RagDocument document, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        using var dir = FSDirectory.Open(IndexPath);
        using var analyzer = CreateAnalyzer();
        var writer = OpenWriter(dir, analyzer, ct);
        try
        {
            var id = document.Header.Id;
            writer.DeleteDocuments(new Term("doc_id", id));
            writer.AddDocument(BuildHeaderDocument(document.Header));
            foreach (var chunk in document.Chunks)
            {
                ct.ThrowIfCancellationRequested();
                writer.AddDocument(BuildChunkDocument(document.Header, chunk));
            }
            ct.ThrowIfCancellationRequested();
            writer.Commit();
        }
        catch
        {
            writer.Rollback();
            throw;
        }
        writer.Dispose();
    }

    public bool Delete(string id, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (!IndexExists()) return false;

        using var dir = FSDirectory.Open(IndexPath);
        using var analyzer = CreateAnalyzer();
        var writer = OpenWriter(dir, analyzer, ct);
        try
        {
            // Checked under the write lock so concurrent writers cannot race the existence test.
            using (var reader = DirectoryReader.Open(writer, true))
            {
                var searcher = new IndexSearcher(reader);
                if (FindHeader(searcher, id) is null)
                {
                    writer.Rollback();
                    return false;
                }
            }
            writer.DeleteDocuments(new Term("doc_id", id));
            ct.ThrowIfCancellationRequested();
            writer.Commit();
        }
        catch
        {
            writer.Rollback();
            throw;
        }
        writer.Dispose();
        return true;
    }

    // ---- infrastructure ----

    private static Analyzer CreateAnalyzer()
    {
        var keyword = new KeywordAnalyzer();
        return new PerFieldAnalyzerWrapper(
            new StandardAnalyzer(Version),
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

    private static string FirstLine(string message)
    {
        var line = message.Split('\n', 2)[0].Trim();
        return line.TrimEnd('.') + ".";
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
            new StoredField("chunk_index", c.Index),
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
