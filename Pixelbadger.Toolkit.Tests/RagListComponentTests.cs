using System.Text.Json;
using FluentAssertions;
using Moq;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Services.Rag;
using static Pixelbadger.Toolkit.Tests.RagTestSupport;

namespace Pixelbadger.Toolkit.Tests;

public class RagListComponentTests : IDisposable
{
    private readonly string _dir = NewTempDir();
    private readonly LuceneRagIndex _index;

    public RagListComponentTests() => _index = new LuceneRagIndex(Path.Combine(_dir, "i"));

    public void Dispose() => Directory.Delete(_dir, true);

    private void Seed(int count)
    {
        var chunker = new ParagraphTextChunker();
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        for (var n = 0; n < count; n++)
        {
            var c = chunker.Chunk($"text {n}", 1000, 0);
            var h = new RagDocumentHeader(RagIds.NewId(), $"T{n:00}", "f.txt", "/f.txt", start.AddMinutes(n), start.AddMinutes(n), 1, c.NormalizedText.Length, "ab", 1000, 0);
            _index.Upsert(new RagDocument(h, c.Chunks));
        }
    }

    private (int Code, string Out, string Err) Run(int page, int size, RagOutputFormat format = RagOutputFormat.json) =>
        Capture(() => new RagListComponent(_index).Run(new RagListOptions(page, size, format)));

    [Theory]
    [InlineData(1, 10, 10)]
    [InlineData(2, 10, 10)]
    [InlineData(3, 10, 5)]
    [InlineData(4, 10, 0)]
    public void Run_ShouldPage_WhenTwentyFiveDocuments(int page, int size, int expected)
    {
        Seed(25);

        var (code, output, _) = Run(page, size);

        code.Should().Be(0);
        using var json = JsonDocument.Parse(output);
        var root = json.RootElement;
        root.EnumerateObject().Select(p => p.Name).Should().Equal("index", "page", "pageSize", "total", "totalPages", "documents");
        root.GetProperty("index").GetString().Should().Be(_index.IndexPath);
        root.GetProperty("total").GetInt32().Should().Be(25);
        root.GetProperty("totalPages").GetInt32().Should().Be(3);
        root.GetProperty("page").GetInt32().Should().Be(page);
        root.GetProperty("documents").GetArrayLength().Should().Be(expected);
    }

    [Fact]
    public void Run_ShouldWriteDocumentFieldsInOrderAndOrderByCreated_WhenJson()
    {
        Seed(3);

        var (_, output, _) = Run(1, 20);

        using var json = JsonDocument.Parse(output);
        var docs = json.RootElement.GetProperty("documents").EnumerateArray().ToList();
        docs[0].EnumerateObject().Select(p => p.Name).Should().Equal(
            "id", "title", "fileName", "sourcePath", "createdUtc", "updatedUtc", "chunkCount", "charCount", "sha256");
        docs.Select(d => d.GetProperty("title").GetString()).Should().Equal("T00", "T01", "T02");
        docs[0].GetProperty("createdUtc").GetString().Should().Be("2026-01-01T00:00:00.0000000Z");
    }

    [Fact]
    public void Run_ShouldPrintLinesAndFooter_WhenText()
    {
        Seed(2);

        var (code, output, _) = Run(1, 1, RagOutputFormat.text);

        code.Should().Be(0);
        output.Should().Contain("T00").And.Contain("1 chunks").And.Contain("Page 1 of 2 (2 documents)");
        output.Should().NotContain("T01");
    }

    [Fact]
    public void Run_ShouldPrintNoDocuments_WhenIndexEmptyAndLeaveNoDirectory()
    {
        var (code, output, _) = Run(1, 20, RagOutputFormat.text);

        code.Should().Be(0);
        output.Should().Contain("No documents.");
        Directory.Exists(_index.IndexPath).Should().BeFalse();
    }

    [Fact]
    public void Run_ShouldEscapeMarkupInTitle_WhenText()
    {
        var c = new ParagraphTextChunker().Chunk("x", 1000, 0);
        var now = DateTimeOffset.UtcNow;
        _index.Upsert(new RagDocument(new RagDocumentHeader(RagIds.NewId(), "[red]x[/]", "f", "/f", now, now, 1, 1, "ab", 1000, 0), c.Chunks));

        Run(1, 20, RagOutputFormat.text).Out.Should().Contain("[red]x[/]");
    }

    [Theory]
    [InlineData(0, 10, "--page must be at least 1.")]
    [InlineData(-3, 10, "--page must be at least 1.")]
    [InlineData(1, 0, "--page-size must be between 1 and 100.")]
    [InlineData(1, 101, "--page-size must be between 1 and 100.")]
    public void Run_ShouldFail_WhenPagingOutOfRange(int page, int size, string message)
    {
        var mock = new Mock<IRagIndex>(MockBehavior.Strict);

        var (code, _, err) = Capture(() => new RagListComponent(mock.Object).Run(new RagListOptions(page, size, RagOutputFormat.text)));

        code.Should().Be(1);
        err.Should().Be($"Error: {message}{Environment.NewLine}");
    }
}
