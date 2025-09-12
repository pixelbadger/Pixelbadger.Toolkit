using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;
using Lucene.Net.Search.Similarities;
using Lucene.Net.Store;
using Lucene.Net.Util;

namespace Pixelbadger.Toolkit.Components;

public class SearchResult
{
    public float Score { get; set; }
    public string Content { get; set; } = string.Empty;
    public string SourceFile { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public int ParagraphNumber { get; set; }
    public string DocumentId { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
}

public class SearchIndexer
{
    private const LuceneVersion LUCENE_VERSION = LuceneVersion.LUCENE_48;

    public async Task IngestContentAsync(string indexPath, string contentPath)
    {
        if (!File.Exists(contentPath))
        {
            throw new FileNotFoundException($"Content file not found: {contentPath}");
        }

        var content = await File.ReadAllTextAsync(contentPath);
        var chunks = GetChunksForFile(contentPath, content);

        var indexDirectory = FSDirectory.Open(indexPath);
        var analyzer = new StandardAnalyzer(LUCENE_VERSION);
        var config = new IndexWriterConfig(LUCENE_VERSION, analyzer);

        using var writer = new IndexWriter(indexDirectory, config);

        var sourceId = Path.GetFileNameWithoutExtension(contentPath);
        
        for (int i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            if (string.IsNullOrWhiteSpace(chunk.Content))
                continue;

            var doc = new Document();

            // Add the chunk content as a searchable field
            doc.Add(new TextField("content", chunk.Content, Field.Store.YES));

            // Add metadata fields
            doc.Add(new StringField("source_file", Path.GetFileName(contentPath), Field.Store.YES));
            doc.Add(new StringField("source_path", contentPath, Field.Store.YES));
            doc.Add(new StringField("source_id", sourceId, Field.Store.YES));
            doc.Add(new Int32Field("paragraph_number", chunk.ChunkNumber, Field.Store.YES));
            doc.Add(new StringField("document_id", $"{Path.GetFileName(contentPath)}_{chunk.ChunkNumber}", Field.Store.YES));

            writer.AddDocument(doc);
        }

        writer.Commit();
        writer.Dispose();
        indexDirectory.Dispose();
        analyzer.Dispose();
    }

    public Task<List<SearchResult>> QueryAsync(string indexPath, string queryText, int maxResults = 10, string[]? sourceIds = null)
    {
        if (!System.IO.Directory.Exists(indexPath))
        {
            throw new DirectoryNotFoundException($"Index directory not found: {indexPath}");
        }

        var results = new List<SearchResult>();
        var indexDirectory = FSDirectory.Open(indexPath);
        var analyzer = new StandardAnalyzer(LUCENE_VERSION);

        using var reader = DirectoryReader.Open(indexDirectory);
        var searcher = new IndexSearcher(reader);

        // Use BM25 similarity (default in Lucene.NET 4.8)
        searcher.Similarity = new BM25Similarity();

        var parser = new QueryParser(LUCENE_VERSION, "content", analyzer);
        var contentQuery = parser.Parse(queryText);

        Query finalQuery;
        if (sourceIds != null && sourceIds.Length > 0)
        {
            // Create a boolean query to combine content search with source ID filter
            var boolQuery = new BooleanQuery();
            boolQuery.Add(contentQuery, Occur.MUST);

            // Add source ID filter as OR terms within a nested boolean query
            var sourceIdQuery = new BooleanQuery();
            foreach (var sourceId in sourceIds)
            {
                var termQuery = new TermQuery(new Term("source_id", sourceId));
                sourceIdQuery.Add(termQuery, Occur.SHOULD);
            }
            boolQuery.Add(sourceIdQuery, Occur.MUST);
            finalQuery = boolQuery;
        }
        else
        {
            finalQuery = contentQuery;
        }

        var hits = searcher.Search(finalQuery, maxResults);

        foreach (var scoreDoc in hits.ScoreDocs)
        {
            var doc = searcher.Doc(scoreDoc.Doc);
            var result = new SearchResult
            {
                Score = scoreDoc.Score,
                Content = doc.Get("content") ?? string.Empty,
                SourceFile = doc.Get("source_file") ?? string.Empty,
                SourcePath = doc.Get("source_path") ?? string.Empty,
                SourceId = doc.Get("source_id") ?? string.Empty,
                ParagraphNumber = int.Parse(doc.Get("paragraph_number") ?? "0"),
                DocumentId = doc.Get("document_id") ?? string.Empty
            };
            results.Add(result);
        }

        reader.Dispose();
        indexDirectory.Dispose();
        analyzer.Dispose();

        return Task.FromResult(results);
    }

    private static List<IChunk> GetChunksForFile(string filePath, string content)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        
        ITextChunker chunker = extension switch
        {
            ".md" or ".markdown" => new MarkdownTextChunker(),
            _ => new ParagraphTextChunker()
        };

        return chunker.ChunkText(content);
    }
}