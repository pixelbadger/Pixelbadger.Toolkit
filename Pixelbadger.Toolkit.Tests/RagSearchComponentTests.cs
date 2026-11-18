using System.Text.Json;
using FluentAssertions;
using Moq;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Services.Rag;
using static Pixelbadger.Toolkit.Tests.RagTestSupport;

namespace Pixelbadger.Toolkit.Tests;

public class RagSearchComponentTests : IDisposable
{
    private readonly string _dir = NewTempDir();
    private readonly LuceneRagIndex _index;

    public RagSearchComponentTests() => _index = new LuceneRagIndex(Path.Combine(_dir, "i"));

    public void Dispose() => Directory.Delete(_dir, true);

    private string Seed(string title, string text)
    {
        var c = new ParagraphTextChunker().Chunk(text, 1000, 0);
        var now = DateTimeOffset.UtcNow;
        var id = RagIds.NewId();
        _index.Upsert(new RagDocument(new RagDocumentHeader(id, title, "f", "/f", now, now, c.Chunks.Count, c.NormalizedText.Length, "ab", 1000, 0), c.Chunks));
        return id;
    }

    private (int Code, string Out, string Err) Run(string query, int page = 1, int size = 10, RagOutputFormat format = RagOutputFormat.json) =>
        Capture(() => new RagSearchComponent(_index).Run(new RagSearchOptions(query, page, size, format)));

    [Fact]
    public void Run_ShouldWriteJsonWithExactShape_WhenHit()
    {
        var id = Seed("Doc", "the quick brown fox");

        var (code, output, _) = Run("quick");

        code.Should().Be(0);
        using var json = JsonDocument.Parse(output);
        var root = json.RootElement;
        root.EnumerateObject().Select(p => p.Name).Should().Equal("query", "page", "pageSize", "total", "totalPages", "results");
        root.GetProperty("query").GetString().Should().Be("quick");
        root.GetProperty("total").GetInt32().Should().Be(1);
        var hit = root.GetProperty("results")[0];
        hit.EnumerateObject().Select(p => p.Name).Should().Equal("id", "title", "chunkIndex", "chunkCount", "score", "start", "end", "charCount", "content");
        hit.GetProperty("id").GetString().Should().Be(id);
        hit.GetProperty("title").GetString().Should().Be("Doc");
        hit.GetProperty("chunkCount").GetInt32().Should().Be(1);
        hit.GetProperty("charCount").GetInt32().Should().Be("the quick brown fox".Length);
        hit.GetProperty("content").GetString().Should().Be("the quick brown fox");
        hit.GetProperty("score").GetSingle().Should().BeGreaterThan(0);
    }

    [Fact]
    public void Run_ShouldPrintRankedHitAndFooter_WhenText()
    {
        var id = Seed("Doc", "the quick brown fox");

        var (_, output, _) = Run("quick", format: RagOutputFormat.text);

        output.Should().MatchRegex($@"1\. {id} chunk 1/1 \(score \d\.\d{{3}}\) Doc");
        output.Should().Contain("the quick brown fox").And.Contain("Page 1 of 1 (1 results)");
    }

    [Fact]
    public void Run_ShouldPrintContentLiterally_WhenContentLooksLikeMarkup()
    {
        Seed("[blue]t[/]", "payload [red]x[/] end");

        var (code, output, _) = Run("payload", format: RagOutputFormat.text);

        code.Should().Be(0);
        output.Should().Contain("[red]x[/]").And.Contain("[blue]t[/]");
    }

    [Fact]
    public void Run_ShouldNumberRanksAcrossPages_WhenPageTwo()
    {
        for (var n = 0; n < 3; n++) Seed("T", $"common word {n}");

        var (_, output, _) = Run("common", page: 2, size: 2, format: RagOutputFormat.text);

        output.Should().Contain("3. ").And.NotContain("2. ").And.Contain("Page 2 of 2 (3 results)");
    }

    [Fact]
    public void Run_ShouldPrintNoResults_WhenNothingMatches()
    {
        Seed("T", "alpha");

        Run("zzz", format: RagOutputFormat.text).Out.Should().Contain("No results.");
    }

    [Fact]
    public void Run_ShouldSucceedWithEmptyResultsAndLeaveNoDirectory_WhenIndexMissing()
    {
        var (code, output, _) = Run("anything");

        code.Should().Be(0);
        JsonDocument.Parse(output).RootElement.GetProperty("total").GetInt32().Should().Be(0);
        Directory.Exists(_index.IndexPath).Should().BeFalse();
    }

    [Fact]
    public void Run_ShouldFailWithNoSearchableTerms_WhenQueryIsOnlyStopWords()
    {
        Seed("T", "alpha");

        var (code, output, err) = Run("how do I");

        code.Should().Be(1);
        output.Should().BeEmpty();
        err.Should().Be($"Error: Query has no searchable terms.{Environment.NewLine}");
    }

    [Theory]
    [InlineData("what's the default for --port?")]
    [InlineData("mcp: serve/save")]
    [InlineData("C++ (and [brackets])?")]
    [InlineData("foo AND")]
    [InlineData("\"unbalanced")]
    public void Run_ShouldExit0_WhenQueryHasSpecialCharacters(string query)
    {
        Seed("T", "alpha");

        Run(query).Code.Should().Be(0);
    }

    [Fact]
    public void Run_ShouldFindSameChunks_WhenQuestionMarkAppended()
    {
        Seed("T", "notes on steganography in images\n\nunrelated");

        var plain = JsonDocument.Parse(Run("steganography").Out).RootElement.GetProperty("results");
        var marked = JsonDocument.Parse(Run("steganography?").Out).RootElement.GetProperty("results");

        plain.GetArrayLength().Should().Be(1);
        marked.GetRawText().Should().Be(plain.GetRawText());
    }

    [Fact]
    public void Run_ShouldFailWithTooManyTerms_WhenOver200DistinctTerms()
    {
        Seed("T", "alpha");

        var (code, _, err) = Run(string.Join(" ", Enumerable.Range(0, 201).Select(i => "t" + i)));

        code.Should().Be(1);
        err.Should().Be($"Error: Query has too many terms (max 200).{Environment.NewLine}");
    }

    [Fact]
    public void Run_ShouldMapQueryExceptionMessage_WhenIndexThrows()
    {
        var mock = new Mock<IRagIndex>();
        mock.Setup(i => i.Search("q", 1, 10)).Throws(new RagQueryException("Query has no searchable terms."));

        var (code, _, err) = Capture(() => new RagSearchComponent(mock.Object).Run(new RagSearchOptions("q", 1, 10, RagOutputFormat.text)));

        code.Should().Be(1);
        err.Should().Be($"Error: Query has no searchable terms.{Environment.NewLine}");
    }

    [Theory]
    [InlineData(0, 10, "--page must be at least 1.")]
    [InlineData(1, 0, "--page-size must be between 1 and 100.")]
    [InlineData(1, 101, "--page-size must be between 1 and 100.")]
    public void Run_ShouldFail_WhenPagingOutOfRange(int page, int size, string message)
    {
        var mock = new Mock<IRagIndex>(MockBehavior.Strict);

        var (code, _, err) = Capture(() => new RagSearchComponent(mock.Object).Run(new RagSearchOptions("q", page, size, RagOutputFormat.text)));

        code.Should().Be(1);
        err.Should().Be($"Error: {message}{Environment.NewLine}");
    }
}
