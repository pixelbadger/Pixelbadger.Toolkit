using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;
using Lucene.Net.Search.Similarities;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Directory = Lucene.Net.Store.Directory;

namespace Pixelbadger.Toolkit.Components;

public class SearchResult
{
    public float Score { get; set; }
    public string Content { get; set; } = string.Empty;
    public string SourceFile { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public int ParagraphNumber { get; set; }
    public string DocumentId { get; set; } = string.Empty;
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
        var paragraphs = SplitIntoParagraphs(content);

        var indexDirectory = FSDirectory.Open(indexPath);
        var analyzer = new StandardAnalyzer(LUCENE_VERSION);
        var config = new IndexWriterConfig(LUCENE_VERSION, analyzer);

        using var writer = new IndexWriter(indexDirectory, config);
        
        for (int i = 0; i < paragraphs.Count; i++)
        {
            var paragraph = paragraphs[i];
            if (string.IsNullOrWhiteSpace(paragraph))
                continue;

            var doc = new Document();
            
            // Add the paragraph content as a searchable field
            doc.Add(new TextField("content", paragraph, Field.Store.YES));
            
            // Add metadata fields
            doc.Add(new StringField("source_file", Path.GetFileName(contentPath), Field.Store.YES));
            doc.Add(new StringField("source_path", contentPath, Field.Store.YES));
            doc.Add(new Int32Field("paragraph_number", i + 1, Field.Store.YES));
            doc.Add(new StringField("document_id", $"{Path.GetFileName(contentPath)}_{i + 1}", Field.Store.YES));

            writer.AddDocument(doc);
        }

        writer.Commit();
        writer.Dispose();
        indexDirectory.Dispose();
        analyzer.Dispose();
    }

    public Task<List<SearchResult>> QueryAsync(string indexPath, string queryText, int maxResults = 10)
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
        var query = parser.Parse(queryText);
        
        var hits = searcher.Search(query, maxResults);
        
        foreach (var scoreDoc in hits.ScoreDocs)
        {
            var doc = searcher.Doc(scoreDoc.Doc);
            var result = new SearchResult
            {
                Score = scoreDoc.Score,
                Content = doc.Get("content") ?? string.Empty,
                SourceFile = doc.Get("source_file") ?? string.Empty,
                SourcePath = doc.Get("source_path") ?? string.Empty,
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

    private static List<string> SplitIntoParagraphs(string content)
    {
        // Split by double newlines (typical paragraph separator)
        var paragraphs = content
            .Split(new[] { "\r\n\r\n", "\n\n", "\r\r" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();

        // If no double newlines found, split by single newlines
        if (paragraphs.Count == 1)
        {
            paragraphs = content
                .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList();
        }

        return paragraphs;
    }
}