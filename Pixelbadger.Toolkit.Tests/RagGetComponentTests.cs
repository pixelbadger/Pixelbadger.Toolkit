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

    private (int Code, string Out, string Err) Run(string id, RagOutputFormat format, int? from = null, int? to = null) =>
        Capture(() => new RagGetComponent(_index).Run(new RagGetOptions(id, format, from, to)));

    private string SeedSix() => Seed(string.Join("\n\n", Enumerable.Range(0, 6).Select(i => $"chunk{i} " + new string((char)('a' + i), 80))), 100);

    private static JsonElement Root(string output) => JsonDocument.Parse(output).RootElement;

    private static int[] Indexes(JsonElement root) => root.GetProperty("chunks").EnumerateArray().Select(c => c.GetProperty("index").GetInt32()).ToArray();

    [Fact]
    public void Run_ShouldReturnOnlyRequestedChunksAndFullHeader_WhenRangeGiven()
    {
        var id = SeedSix();

        var (code, output, _) = Run(id, RagOutputFormat.json, 1, 2);

        code.Should().Be(0);
        var root = Root(output);
        Indexes(root).Should().Equal(1, 2);
        root.GetProperty("fromChunk").GetInt32().Should().Be(1);
        root.GetProperty("toChunk").GetInt32().Should().Be(2);
        root.GetProperty("chunkCount").GetInt32().Should().Be(6);
        root.GetProperty("title").GetString().Should().Be("Title");
        root.GetProperty("chunks")[0].GetProperty("content").GetString().Should().StartWith("chunk1 ");
    }

    [Fact]
    public void Run_ShouldClampToLastChunk_WhenToBeyondEnd()
    {
        var id = SeedSix();

        var root = Root(Run(id, RagOutputFormat.json, 4, 99).Out);

        Indexes(root).Should().Equal(4, 5);
        root.GetProperty("fromChunk").GetInt32().Should().Be(4);
        root.GetProperty("toChunk").GetInt32().Should().Be(5);
    }

    [Fact]
    public void Run_ShouldReturnEmptyChunksAndNullRange_WhenFromBeyondLast()
    {
        var id = SeedSix();

        var (code, output, _) = Run(id, RagOutputFormat.json, 6, null);

        code.Should().Be(0);
        var root = Root(output);
        root.GetProperty("chunks").GetArrayLength().Should().Be(0);
        root.GetProperty("fromChunk").ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("toChunk").ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("chunkCount").GetInt32().Should().Be(6);
    }

    [Fact]
    public void Run_ShouldDefaultFromToZeroAndToToLast_WhenOnlyOneBoundGiven()
    {
        var id = SeedSix();

        Indexes(Root(Run(id, RagOutputFormat.json, null, 1).Out)).Should().Equal(0, 1);
        Indexes(Root(Run(id, RagOutputFormat.json, 4, null).Out)).Should().Equal(4, 5);
        Indexes(Root(Run(id, RagOutputFormat.json).Out)).Should().Equal(0, 1, 2, 3, 4, 5);
    }

    [Fact]
    public void Run_ShouldReturnSingleChunk_WhenFromEqualsTo()
    {
        var id = SeedSix();

        Indexes(Root(Run(id, RagOutputFormat.json, 3, 3).Out)).Should().Equal(3);
    }

    [Theory]
    [InlineData(-1, null, "--from-chunk must be ≥ 0.")]
    [InlineData(null, -1, "--to-chunk must be ≥ 0.")]
    [InlineData(-2, -1, "--from-chunk must be ≥ 0.")]
    [InlineData(3, 2, "--from-chunk must not be greater than --to-chunk.")]
    public void Run_ShouldFail_WhenRangeInvalid(int? from, int? to, string message)
    {
        var id = SeedSix();

        var (code, output, err) = Run(id, RagOutputFormat.json, from, to);

        code.Should().Be(1);
        output.Should().BeEmpty();
        err.Should().Be($"Error: {message}{Environment.NewLine}");
    }

    [Fact]
    public void Run_ShouldWriteJsonWithDocumentFieldsThenChunks_WhenFound()
    {
        var id = Seed(string.Join("\n\n", Enumerable.Range(0, 5).Select(i => new string((char)('a' + i), 60))), 100);

        var (code, output, _) = Run(id.ToUpperInvariant(), RagOutputFormat.json);

        code.Should().Be(0);
        using var json = JsonDocument.Parse(output);
        var root = json.RootElement;
        root.EnumerateObject().Select(p => p.Name).Should().Equal(
            "id", "title", "fileName", "sourcePath", "createdUtc", "updatedUtc", "chunkCount", "charCount", "sha256", "fromChunk", "toChunk", "chunks");
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
