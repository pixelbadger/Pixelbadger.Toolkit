using FluentAssertions;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Services.Rag;
using static Pixelbadger.Toolkit.Tests.RagTestSupport;

namespace Pixelbadger.Toolkit.Tests;

public class RagOutputRendererTests
{
    private static RagDocumentHeader Header(string title = "T") =>
        new("a".PadRight(32, 'a'), title, "f.txt", "/p/f.txt", DateTimeOffset.Parse("2026-01-01T02:00:00+02:00"), DateTimeOffset.Parse("2026-01-01T00:00:00Z"), 2, 10, "sha", 1000, 0);

    [Fact]
    public void Iso_ShouldReturnUtcRoundTripString_WhenOffsetIsNotUtc()
    {
        RagOutputRenderer.Iso(DateTimeOffset.Parse("2026-01-01T02:00:00+02:00")).Should().Be("2026-01-01T00:00:00.0000000Z");
    }

    [Fact]
    public void WriteError_ShouldWritePlainTextToStderrAndReturn1_WhenCalled()
    {
        var (code, output, err) = Capture(() => RagOutputRenderer.WriteError("[red]bad[/]"));

        code.Should().Be(1);
        output.Should().BeEmpty();
        err.Should().Be($"Error: [red]bad[/]{Environment.NewLine}");
    }

    [Fact]
    public void RenderIngest_ShouldEscapeSourcePath_WhenTextFormat()
    {
        var (_, output, _) = Capture(() =>
        {
            RagOutputRenderer.RenderIngest(RagOutputFormat.text, "updated", Header() with { SourcePath = "/[red]/f.txt" });
            return 0;
        });

        output.Should().Contain("Updated aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa from '/[red]/f.txt' (2 chunks).");
    }

    [Fact]
    public void RenderDelete_ShouldWriteCompactJson_WhenJson()
    {
        var (_, output, _) = Capture(() => { RagOutputRenderer.RenderDelete(RagOutputFormat.json, "abc"); return 0; });

        output.Trim().Should().Be("{\"id\":\"abc\",\"deleted\":true}");
    }

    [Fact]
    public void RenderSearch_ShouldWriteExactJson_WhenOneHit()
    {
        var page = new RagPage<RagSearchHit>(1, 10, 1, [new RagSearchHit("id1", "Ti", 3, 7, 1.5f, 4, 9, 123, "cont <&>")]);

        var (_, output, _) = Capture(() => { RagOutputRenderer.RenderSearch(RagOutputFormat.json, "q", page); return 0; });

        output.Trim().Should().Be(
            "{\"query\":\"q\",\"page\":1,\"pageSize\":10,\"total\":1,\"totalPages\":1,\"results\":[{\"id\":\"id1\",\"title\":\"Ti\",\"chunkIndex\":3,\"chunkCount\":7,\"score\":1.5,\"start\":4,\"end\":9,\"charCount\":123,\"content\":\"cont <&>\"}]}");
    }

    [Fact]
    public void RenderList_ShouldWriteExactJson_WhenOneDocument()
    {
        var page = new RagPage<RagDocumentHeader>(1, 20, 1, [Header()]);

        var (_, output, _) = Capture(() => { RagOutputRenderer.RenderList(RagOutputFormat.json, "/idx", page); return 0; });

        output.Trim().Should().Be(
            "{\"index\":\"/idx\",\"page\":1,\"pageSize\":20,\"total\":1,\"totalPages\":1,\"documents\":[{\"id\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"title\":\"T\",\"fileName\":\"f.txt\",\"sourcePath\":\"/p/f.txt\",\"createdUtc\":\"2026-01-01T00:00:00.0000000Z\",\"updatedUtc\":\"2026-01-01T00:00:00.0000000Z\",\"chunkCount\":2,\"charCount\":10,\"sha256\":\"sha\"}]}");
    }

    [Fact]
    public void RenderGet_ShouldWriteChunksWithOffsets_WhenText()
    {
        var doc = new RagDocument(Header(), [new TextChunk(0, 0, 5, "[b]hi"), new TextChunk(1, 5, 9, "more")]);

        var (_, output, _) = Capture(() => { RagOutputRenderer.RenderGet(RagOutputFormat.text, doc); return 0; });

        output.Should().Contain("--- chunk 0 [0-5] ---").And.Contain("[b]hi").And.Contain("--- chunk 1 [5-9] ---").And.Contain("SHA-256: sha");
    }

    [Fact]
    public void RenderSearch_ShouldShowOneBasedPositionOfDocument_WhenText()
    {
        var page = new RagPage<RagSearchHit>(1, 10, 1, [new RagSearchHit("id1", "Ti", 3, 7, 1.5f, 4, 9, 123, "cont")]);

        var (_, output, _) = Capture(() => { RagOutputRenderer.RenderSearch(RagOutputFormat.text, "q", page); return 0; });

        output.Should().Contain("1. id1 chunk 4/7 (score 1.500) Ti");
    }

    [Fact]
    public void RenderGet_ShouldWriteEffectiveRangeBeforeChunks_WhenJson()
    {
        var doc = new RagDocument(Header(), [new TextChunk(1, 5, 9, "more"), new TextChunk(2, 9, 12, "end")]);

        var (_, output, _) = Capture(() => { RagOutputRenderer.RenderGet(RagOutputFormat.json, doc); return 0; });

        output.Trim().Should().Be(
            "{\"id\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"title\":\"T\",\"fileName\":\"f.txt\",\"sourcePath\":\"/p/f.txt\",\"createdUtc\":\"2026-01-01T00:00:00.0000000Z\",\"updatedUtc\":\"2026-01-01T00:00:00.0000000Z\",\"chunkCount\":2,\"charCount\":10,\"sha256\":\"sha\",\"fromChunk\":1,\"toChunk\":2,\"chunks\":[{\"index\":1,\"start\":5,\"end\":9,\"content\":\"more\"},{\"index\":2,\"start\":9,\"end\":12,\"content\":\"end\"}]}");
    }

    [Fact]
    public void RenderGet_ShouldWriteNullRange_WhenNoChunksReturned()
    {
        var doc = new RagDocument(Header(), []);

        var (_, output, _) = Capture(() => { RagOutputRenderer.RenderGet(RagOutputFormat.json, doc); return 0; });

        output.Trim().Should().EndWith("\"sha256\":\"sha\",\"fromChunk\":null,\"toChunk\":null,\"chunks\":[]}");
    }
}
