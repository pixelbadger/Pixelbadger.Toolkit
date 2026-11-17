using System.Text.Json;
using FluentAssertions;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Services.Rag;
using static Pixelbadger.Toolkit.Tests.RagTestSupport;

namespace Pixelbadger.Toolkit.Tests;

public class RagGetComponentTests : IDisposable
{
    private readonly string _dir = NewTempDir();
    private readonly LuceneRagIndex _index;

    public RagGetComponentTests() => _index = new LuceneRagIndex(Path.Combine(_dir, "i"));

    public void Dispose() => Directory.Delete(_dir, true);

    private string Seed(string text, int chunkSize = 1000)
    {
        var c = new ParagraphTextChunker().Chunk(text, chunkSize, 0);
        var now = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);
        var id = RagIds.NewId();
        _index.Upsert(new RagDocument(new RagDocumentHeader(id, "Title", "f.txt", "/p/f.txt", now, now, c.Chunks.Count, c.NormalizedText.Length, "abc123", chunkSize, 0), c.Chunks));
        return id;
    }

    private (int Code, string Out, string Err) Run(string id, RagOutputFormat format) =>
        Capture(() => new RagGetComponent(_index).Run(new RagGetOptions(id, format)));

    [Fact]
    public void Run_ShouldWriteJsonWithDocumentFieldsThenChunks_WhenFound()
    {
        var id = Seed(string.Join("\n\n", Enumerable.Range(0, 5).Select(i => new string((char)('a' + i), 60))), 100);

        var (code, output, _) = Run(id.ToUpperInvariant(), RagOutputFormat.json);

        code.Should().Be(0);
        using var json = JsonDocument.Parse(output);
        var root = json.RootElement;
        root.EnumerateObject().Select(p => p.Name).Should().Equal(
            "id", "title", "fileName", "sourcePath", "createdUtc", "updatedUtc", "chunkCount", "charCount", "sha256", "chunks");
        root.GetProperty("id").GetString().Should().Be(id);
        root.GetProperty("sha256").GetString().Should().Be("abc123");
        root.GetProperty("createdUtc").GetString().Should().Be("2026-03-04T05:06:07.0000000Z");
        var chunks = root.GetProperty("chunks").EnumerateArray().ToList();
        chunks.Should().HaveCount(root.GetProperty("chunkCount").GetInt32()).And.HaveCountGreaterThan(1);
        chunks[0].EnumerateObject().Select(p => p.Name).Should().Equal("index", "start", "end", "content");
        chunks.Select(c => c.GetProperty("index").GetInt32()).Should().Equal(Enumerable.Range(0, chunks.Count));
    }

    [Fact]
    public void Run_ShouldPrintHeaderAndChunksLiterally_WhenText()
    {
        var id = Seed("see [red]x[/] now");

        var (code, output, _) = Run(id, RagOutputFormat.text);

        code.Should().Be(0);
        output.Should().Contain($"Id: {id}").And.Contain("Title: Title").And.Contain("--- chunk 0").And.Contain("see [red]x[/] now");
    }

    [Fact]
    public void Run_ShouldFailNotFound_WhenIdUnknown()
    {
        var id = RagIds.NewId();

        var (code, _, err) = Run(id, RagOutputFormat.text);

        code.Should().Be(1);
        err.Should().Be($"Error: Document '{id}' not found.{Environment.NewLine}");
    }

    [Fact]
    public void Run_ShouldFailNotFoundAndCreateNothing_WhenIndexMissing()
    {
        Run(RagIds.NewId(), RagOutputFormat.json).Code.Should().Be(1);
        Directory.Exists(_index.IndexPath).Should().BeFalse();
    }

    [Fact]
    public void Run_ShouldFailInvalidId_WhenMalformed()
    {
        var (code, _, err) = Run("xyz", RagOutputFormat.text);

        code.Should().Be(1);
        err.Should().Be($"Error: Invalid document ID 'xyz'. Expected 32 hexadecimal characters.{Environment.NewLine}");
    }
}
