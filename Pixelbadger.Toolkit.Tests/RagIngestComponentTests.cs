using FluentAssertions;
using Moq;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Services.Rag;
using static Pixelbadger.Toolkit.Tests.RagTestSupport;

namespace Pixelbadger.Toolkit.Tests;

public class RagIngestComponentTests : IDisposable
{
    private readonly string _dir = NewTempDir();
    private readonly string _indexPath;
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));
    private readonly LuceneRagIndex _index;
    private readonly RagIngestComponent _component;

    public RagIngestComponentTests()
    {
        _indexPath = Path.Combine(_dir, "index");
        _index = new LuceneRagIndex(_indexPath);
        _component = new RagIngestComponent(_index, new ParagraphTextChunker(), _time);
    }

    public void Dispose() => Directory.Delete(_dir, true);

    private string Write(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private (int Code, string Out, string Err) Run(string file, string? id = null, string? title = null, int size = 1000, int overlap = 0,
        RagOutputFormat format = RagOutputFormat.text) =>
        Capture(() => _component.Run(new RagIngestOptions(file, id, title, size, overlap, format)));

    private void IndexShouldBeUntouched() => _index.List(1, 100).Total.Should().Be(0);

    [Fact]
    public void Run_ShouldCreateDocumentWithHeaderFields_WhenNoIdGiven()
    {
        var file = Write("a.txt", "Hello world.\n\nSecond paragraph.");

        var (code, output, _) = Run(file);

        code.Should().Be(0);
        var header = _index.List(1, 10).Items.Single();
        output.Should().Contain($"Created {header.Id} from '{file}' (1 chunks).");
        header.Id.Should().MatchRegex("^[0-9a-f]{32}$");
        header.Title.Should().Be("a.txt");
        header.FileName.Should().Be("a.txt");
        header.SourcePath.Should().Be(file);
        header.CreatedUtc.Should().Be(_time.Now);
        header.UpdatedUtc.Should().Be(_time.Now);
        header.CharCount.Should().Be("Hello world.\n\nSecond paragraph.".Length);
        header.Sha256.Should().Be(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant());
        header.ChunkSize.Should().Be(1000);
    }

    [Fact]
    public void Run_ShouldCreateSecondDocumentWithNewId_WhenSameFileIngestedTwice()
    {
        var file = Write("a.txt", "Some text.");

        Run(file);
        Run(file);

        var items = _index.List(1, 10).Items;
        items.Should().HaveCount(2);
        items.Select(i => i.Id).Distinct().Should().HaveCount(2);
    }

    [Fact]
    public void Run_ShouldUseTitleAndChunkOptions_WhenGiven()
    {
        var file = Write("a.txt", string.Join("\n\n", Enumerable.Range(0, 20).Select(i => $"Paragraph number {i} with some filler text to occupy room.")));

        var (code, _, _) = Run(file, title: "My Title", size: 100, overlap: 20);

        code.Should().Be(0);
        var header = _index.List(1, 10).Items.Single();
        header.Title.Should().Be("My Title");
        header.ChunkSize.Should().Be(100);
        header.ChunkOverlap.Should().Be(20);
        header.ChunkCount.Should().BeGreaterThan(5);
    }

    [Fact]
    public void Run_ShouldKeepIdAndCreatedAndAdvanceUpdated_WhenIdExists()
    {
        var first = Write("a.txt", "alpha oldterm text");
        Run(first);
        var original = _index.List(1, 10).Items.Single();
        _time.Now = _time.Now.AddHours(1);
        var second = Write("b.txt", "beta newterm text");

        var (code, output, _) = Run(second, id: original.Id.ToUpperInvariant(), title: "New");

        code.Should().Be(0);
        output.Should().Contain($"Updated {original.Id} from '{second}'");
        var updated = _index.List(1, 10).Items.Single();
        updated.Id.Should().Be(original.Id);
        updated.CreatedUtc.Should().Be(original.CreatedUtc);
        updated.UpdatedUtc.Should().Be(_time.Now).And.NotBe(original.UpdatedUtc);
        updated.Title.Should().Be("New");
        updated.FileName.Should().Be("b.txt");
        _index.Search($"doc_id:{original.Id} AND oldterm", 1, 10).Total.Should().Be(0);
        _index.Search($"doc_id:{original.Id} AND newterm", 1, 10).Total.Should().Be(1);
    }

    [Theory]
    [InlineData("not-an-id")]
    [InlineData("0123456789abcdef0123456789abcde")]
    [InlineData("0123456789abcdef0123456789abcdeg")]
    public void Run_ShouldFailAndLeaveIndexUntouched_WhenIdIsMalformed(string id)
    {
        var (code, _, err) = Run(Write("a.txt", "text"), id: id);

        code.Should().Be(1);
        err.Should().Be($"Error: Invalid document ID '{id}'. Expected 32 hexadecimal characters.{Environment.NewLine}");
        IndexShouldBeUntouched();
    }

    [Fact]
    public void Run_ShouldFailAndLeaveIndexUntouched_WhenIdIsUnknown()
    {
        var id = RagIds.NewId();

        var (code, _, err) = Run(Write("a.txt", "text"), id: id);

        code.Should().Be(1);
        err.Should().Contain($"Error: Document '{id}' not found.");
        IndexShouldBeUntouched();
    }

    [Fact]
    public void Run_ShouldFail_WhenFileIsMissing()
    {
        var missing = Path.Combine(_dir, "missing.txt");

        var (code, _, err) = Run(missing);

        code.Should().Be(1);
        err.Should().Contain($"Error: File '{missing}' not found.");
        IndexShouldBeUntouched();
    }

    [Fact]
    public void Run_ShouldFail_WhenFileContainsNul()
    {
        var file = Write("nul.txt", "abc\0def");

        var (code, _, err) = Run(file);

        code.Should().Be(1);
        err.Should().Contain($"Error: File '{file}' is not a plain text file.");
        IndexShouldBeUntouched();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \r\n\t\n  ")]
    public void Run_ShouldFail_WhenFileHasNoText(string content)
    {
        var file = Write("empty.txt", content);

        var (code, _, err) = Run(file);

        code.Should().Be(1);
        err.Should().Contain($"Error: File '{file}' contains no text.");
        IndexShouldBeUntouched();
    }

    [Fact]
    public void Run_ShouldFail_WhenFileIsLargerThan50Mb()
    {
        var file = Path.Combine(_dir, "big.txt");
        using (var stream = File.Create(file))
            stream.SetLength(50L * 1024 * 1024 + 1);

        var (code, _, err) = Run(file);

        code.Should().Be(1);
        err.Should().Contain($"Error: File '{file}' is larger than 50 MB.");
        IndexShouldBeUntouched();
    }

    [Fact]
    public void Run_ShouldStripUtf8BomAndAcceptInvalidBytes_WhenDecoding()
    {
        var file = Path.Combine(_dir, "bom.txt");
        File.WriteAllBytes(file, [0xEF, 0xBB, 0xBF, (byte)'h', (byte)'i', 0xFF, (byte)'!']);

        var (code, _, _) = Run(file);

        code.Should().Be(0);
        var doc = _index.Get(_index.List(1, 1).Items.Single().Id)!;
        doc.Chunks.Single().Content.Should().Be("hi�!");
    }

    [Theory]
    [InlineData(99, 0, "--chunk-size must be between 100 and 100000.")]
    [InlineData(100001, 0, "--chunk-size must be between 100 and 100000.")]
    [InlineData(1000, -1, "--chunk-overlap must be between 0 and half of --chunk-size.")]
    [InlineData(1000, 501, "--chunk-overlap must be between 0 and half of --chunk-size.")]
    public void Run_ShouldFailBeforeReadingFile_WhenRangeViolated(int size, int overlap, string message)
    {
        var (code, _, err) = Run(Path.Combine(_dir, "never-read.txt"), size: size, overlap: overlap);

        code.Should().Be(1);
        err.Should().Be($"Error: {message}{Environment.NewLine}");
        IndexShouldBeUntouched();
    }

    [Theory]
    [InlineData(100, 50)]
    [InlineData(100000, 0)]
    public void Run_ShouldAccept_WhenRangeBoundariesUsed(int size, int overlap)
    {
        Run(Write("a.txt", "text"), size: size, overlap: overlap).Code.Should().Be(0);
    }

    [Fact]
    public void Run_ShouldWriteJsonWithExactFieldOrder_WhenFormatIsJson()
    {
        var file = Write("a.txt", "Hello world.");

        var (code, output, _) = Run(file, format: RagOutputFormat.json);

        code.Should().Be(0);
        using var json = System.Text.Json.JsonDocument.Parse(output);
        json.RootElement.EnumerateObject().Select(p => p.Name).Should().Equal("id", "action", "title", "sourcePath", "chunkCount", "charCount");
        json.RootElement.GetProperty("action").GetString().Should().Be("created");
        json.RootElement.GetProperty("sourcePath").GetString().Should().Be(file);
        json.RootElement.GetProperty("chunkCount").GetInt32().Should().Be(1);
        json.RootElement.GetProperty("charCount").GetInt32().Should().Be(12);
        output.Trim().Split('\n').Should().HaveCount(1);
    }

    [Fact]
    public void Run_ShouldReportUpdatedAction_WhenJsonAndIdExists()
    {
        Run(Write("a.txt", "x"));
        var id = _index.List(1, 1).Items.Single().Id;

        var (_, output, _) = Run(Write("b.txt", "y"), id: id, format: RagOutputFormat.json);

        System.Text.Json.JsonDocument.Parse(output).RootElement.GetProperty("action").GetString().Should().Be("updated");
    }

    [Fact]
    public void Run_ShouldPrintLockMessage_WhenIndexIsLocked()
    {
        var mock = new Mock<IRagIndex>();
        mock.Setup(i => i.Upsert(It.IsAny<RagDocument>(), It.IsAny<CancellationToken>()))
            .Throws(new RagIndexLockedException("Index 'p' is locked by another writer"));
        var component = new RagIngestComponent(mock.Object, new ParagraphTextChunker(), _time);

        var (code, _, err) = Capture(() => component.Run(new RagIngestOptions(Write("a.txt", "text"), null, null, 1000, 0, RagOutputFormat.text)));

        code.Should().Be(1);
        err.Should().Be($"Error: Index 'p' is locked by another writer{Environment.NewLine}");
    }

    [Fact]
    public void Run_ShouldReturn1_WhenCancelled()
    {
        var mock = new Mock<IRagIndex>();
        mock.Setup(i => i.Upsert(It.IsAny<RagDocument>(), It.IsAny<CancellationToken>())).Throws<OperationCanceledException>();
        var component = new RagIngestComponent(mock.Object, new ParagraphTextChunker(), _time);

        var (code, _, err) = Capture(() => component.Run(new RagIngestOptions(Write("a.txt", "text"), null, null, 1000, 0, RagOutputFormat.text)));

        code.Should().Be(1);
        err.Should().StartWith("Error:");
    }

    [Fact]
    public void Run_ShouldPrintMarkupLiterally_WhenFileContainsMarkupInTitle()
    {
        var (code, output, _) = Run(Write("a.txt", "[red]x[/]"), title: "[bold]t[/]");

        code.Should().Be(0);
        output.Should().Contain("Created ");
        _index.List(1, 1).Items.Single().Title.Should().Be("[bold]t[/]");
    }
}
