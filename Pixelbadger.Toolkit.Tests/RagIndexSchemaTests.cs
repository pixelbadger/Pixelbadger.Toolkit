using FluentAssertions;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Services.Rag;
using static Pixelbadger.Toolkit.Tests.RagTestSupport;

namespace Pixelbadger.Toolkit.Tests;

/// <summary>An index without the rag_schema=2 commit marker (as built by an earlier pbtk) is refused by every action.</summary>
public class RagIndexSchemaTests : IDisposable
{
    private readonly string _dir = NewTempDir();
    private string IndexPath => Path.Combine(_dir, "legacy");

    public void Dispose() => System.IO.Directory.Delete(_dir, true);

    private void BuildLegacyIndex()
    {
        using var dir = FSDirectory.Open(IndexPath);
        using var analyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);
        using var writer = new IndexWriter(dir, new IndexWriterConfig(LuceneVersion.LUCENE_48, analyzer));
        writer.AddDocument(new Document { new StringField("kind", "header", Field.Store.YES), new StringField("doc_id", new string('a', 32), Field.Store.YES) });
        writer.Commit();
    }

    private string Message => $"Index '{Path.GetFullPath(IndexPath)}' was built by an older pbtk version; delete it and re-ingest.";

    private string WriteFile(string text)
    {
        var path = Path.Combine(_dir, "in.txt");
        File.WriteAllText(path, text);
        return path;
    }

    private void AssertRefused(Func<int> run)
    {
        var (code, output, err) = Capture(run);
        code.Should().Be(1);
        output.Should().BeEmpty();
        err.Should().Be($"Error: {Message}{Environment.NewLine}");
    }

    [Fact]
    public void Search_ShouldBeRefused_WhenIndexHasNoMarker()
    {
        BuildLegacyIndex();
        AssertRefused(() => new RagSearchComponent(new LuceneRagIndex(IndexPath)).Run(new RagSearchOptions("anything", 1, 10, RagOutputFormat.text)));
    }

    [Fact]
    public void List_ShouldBeRefused_WhenIndexHasNoMarker()
    {
        BuildLegacyIndex();
        AssertRefused(() => new RagListComponent(new LuceneRagIndex(IndexPath)).Run(new RagListOptions(1, 20, RagOutputFormat.text)));
    }

    [Fact]
    public void Get_ShouldBeRefused_WhenIndexHasNoMarker()
    {
        BuildLegacyIndex();
        AssertRefused(() => new RagGetComponent(new LuceneRagIndex(IndexPath)).Run(new RagGetOptions(new string('a', 32), RagOutputFormat.text)));
    }

    [Fact]
    public void Delete_ShouldBeRefusedAndLeaveIndexUntouched_WhenIndexHasNoMarker()
    {
        BuildLegacyIndex();
        AssertRefused(() => new RagDeleteComponent(new LuceneRagIndex(IndexPath)).Run(new RagDeleteOptions(new string('a', 32), RagOutputFormat.text)));
        using var dir = FSDirectory.Open(IndexPath);
        using var reader = DirectoryReader.Open(dir);
        reader.NumDocs.Should().Be(1);
        reader.IndexCommit.UserData.Should().NotContainKey("rag_schema");
    }

    [Fact]
    public void Ingest_ShouldBeRefusedForCreateAndUpdate_WhenIndexHasNoMarker()
    {
        BuildLegacyIndex();
        var file = WriteFile("some text");
        var component = new RagIngestComponent(new LuceneRagIndex(IndexPath), new ParagraphTextChunker(), TimeProvider.System);

        AssertRefused(() => component.Run(new RagIngestOptions(file, null, null, 1000, 0, RagOutputFormat.text)));
        AssertRefused(() => component.Run(new RagIngestOptions(file, new string('a', 32), null, 1000, 0, RagOutputFormat.text)));

        using var dir = FSDirectory.Open(IndexPath);
        using var reader = DirectoryReader.Open(dir);
        reader.NumDocs.Should().Be(1);
        reader.IndexCommit.UserData.Should().NotContainKey("rag_schema");
    }

    [Fact]
    public void Ingest_ShouldWriteMarker_WhenIndexIsFresh()
    {
        var file = WriteFile("some text");
        var path = Path.Combine(_dir, "fresh");
        var (code, _, _) = Capture(() => new RagIngestComponent(new LuceneRagIndex(path), new ParagraphTextChunker(), TimeProvider.System)
            .Run(new RagIngestOptions(file, null, null, 1000, 0, RagOutputFormat.text)));

        code.Should().Be(0);
        using var dir = FSDirectory.Open(path);
        using var reader = DirectoryReader.Open(dir);
        reader.IndexCommit.UserData["rag_schema"].Should().Be("2");
    }

    [Fact]
    public void Delete_ShouldKeepMarker_WhenDeletingFromFreshIndex()
    {
        var file = WriteFile("some text");
        var path = Path.Combine(_dir, "fresh");
        var index = new LuceneRagIndex(path);
        new RagIngestComponent(index, new ParagraphTextChunker(), TimeProvider.System).Run(new RagIngestOptions(file, null, null, 1000, 0, RagOutputFormat.json));
        var id = index.List(1, 10).Items.Single().Id;

        index.Delete(id).Should().BeTrue();

        using var dir = FSDirectory.Open(path);
        using var reader = DirectoryReader.Open(dir);
        reader.IndexCommit.UserData["rag_schema"].Should().Be("2");
        index.List(1, 10).Total.Should().Be(0);
    }
}
