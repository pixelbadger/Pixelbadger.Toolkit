using System.Text.Json;
using FluentAssertions;
using Moq;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Services.Rag;
using static Pixelbadger.Toolkit.Tests.RagTestSupport;

namespace Pixelbadger.Toolkit.Tests;

public class RagDeleteComponentTests : IDisposable
{
    private readonly string _dir = NewTempDir();
    private readonly LuceneRagIndex _index;

    public RagDeleteComponentTests() => _index = new LuceneRagIndex(Path.Combine(_dir, "i"));

    public void Dispose() => Directory.Delete(_dir, true);

    private string Seed(string text)
    {
        var c = new ParagraphTextChunker().Chunk(text, 1000, 0);
        var now = DateTimeOffset.UtcNow;
        var id = RagIds.NewId();
        _index.Upsert(new RagDocument(new RagDocumentHeader(id, "T", "f", "/f", now, now, c.Chunks.Count, c.NormalizedText.Length, "ab", 1000, 0), c.Chunks));
        return id;
    }

    private (int Code, string Out, string Err) Run(string id, RagOutputFormat format = RagOutputFormat.text) =>
        Capture(() => new RagDeleteComponent(_index).Run(new RagDeleteOptions(id, format)));

    [Fact]
    public void Run_ShouldRemoveHeaderAndChunks_WhenIdExists()
    {
        var id = Seed("deleteme");
        var other = Seed("keepme");

        var (code, output, _) = Run(id.ToUpperInvariant());

        code.Should().Be(0);
        output.Should().Contain($"Deleted {id}.");
        _index.Get(id).Should().BeNull();
        _index.Search($"doc_id:{id}", 1, 10).Total.Should().Be(0);
        _index.Get(other).Should().NotBeNull();
    }

    [Fact]
    public void Run_ShouldWriteJson_WhenFormatIsJson()
    {
        var id = Seed("x");

        var (_, output, _) = Run(id, RagOutputFormat.json);

        using var json = JsonDocument.Parse(output);
        json.RootElement.EnumerateObject().Select(p => p.Name).Should().Equal("id", "deleted");
        json.RootElement.GetProperty("deleted").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void Run_ShouldFailAndLeaveIndexUnchanged_WhenIdUnknown()
    {
        var keep = Seed("x");
        var unknown = RagIds.NewId();

        var (code, _, err) = Run(unknown);

        code.Should().Be(1);
        err.Should().Be($"Error: Document '{unknown}' not found.{Environment.NewLine}");
        _index.List(1, 10).Total.Should().Be(1);
        _index.Get(keep).Should().NotBeNull();
    }

    [Fact]
    public void Run_ShouldFailInvalidId_WhenMalformedWithoutTouchingIndex()
    {
        var mock = new Mock<IRagIndex>(MockBehavior.Strict);

        var (code, _, err) = Capture(() => new RagDeleteComponent(mock.Object).Run(new RagDeleteOptions("bad", RagOutputFormat.text)));

        code.Should().Be(1);
        err.Should().Be($"Error: Invalid document ID 'bad'. Expected 32 hexadecimal characters.{Environment.NewLine}");
    }

    [Fact]
    public void Run_ShouldFailAndCreateNothing_WhenIndexMissing()
    {
        Run(RagIds.NewId()).Code.Should().Be(1);
        Directory.Exists(_index.IndexPath).Should().BeFalse();
    }

    [Fact]
    public void Run_ShouldPrintLockMessage_WhenIndexLocked()
    {
        var mock = new Mock<IRagIndex>();
        mock.Setup(i => i.Delete(It.IsAny<string>(), It.IsAny<CancellationToken>())).Throws(new RagIndexLockedException("Index 'p' is locked by another writer"));

        var (code, _, err) = Capture(() => new RagDeleteComponent(mock.Object).Run(new RagDeleteOptions(RagIds.NewId(), RagOutputFormat.text)));

        code.Should().Be(1);
        err.Should().Contain("Index 'p' is locked by another writer");
    }
}
