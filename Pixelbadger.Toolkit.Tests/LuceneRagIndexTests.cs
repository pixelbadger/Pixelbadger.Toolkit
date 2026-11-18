using System.Diagnostics;
using FluentAssertions;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Index;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Directory = System.IO.Directory;
using Pixelbadger.Toolkit.Services.Rag;
using Xunit;

namespace Pixelbadger.Toolkit.Tests;

public class LuceneRagIndexTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private readonly string _path;
    private readonly LuceneRagIndex _index;
    private readonly ParagraphTextChunker _chunker = new();
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public LuceneRagIndexTests()
    {
        _path = Path.Combine(_root, "idx");
        _index = new LuceneRagIndex(_path, TimeSpan.FromMilliseconds(300));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private RagDocument MakeDoc(string text, string? id = null, string title = "Title", DateTimeOffset? created = null,
        DateTimeOffset? updated = null, int size = 100, int overlap = 0)
    {
        id ??= RagIds.NewId();
        var chunked = _chunker.Chunk(text, size, overlap);
        var header = new RagDocumentHeader(id, title, "file.txt", "/abs/path/file.txt", created ?? T0, updated ?? created ?? T0,
            chunked.Chunks.Count, chunked.NormalizedText.Length, "abc123", size, overlap);
        return new RagDocument(header, chunked.Chunks);
    }

    private RagDocument Add(string text, string title = "Title", DateTimeOffset? created = null)
    {
        var d = MakeDoc(text, title: title, created: created);
        _index.Upsert(d);
        return d;
    }

    [Fact]
    public void Upsert_ShouldStoreHeaderAndChunks_WhenDocumentNew()
    {
        var text = string.Join("\n\n", Enumerable.Range(0, 10).Select(i => $"paragraph{i} " + new string('z', 60)));
        var doc = MakeDoc(text, title: "My Title", created: T0, updated: T0.AddMinutes(5));
        doc.Chunks.Count.Should().BeGreaterThan(2);
        _index.Upsert(doc);

        var header = _index.GetHeader(doc.Header.Id);
        header.Should().Be(doc.Header);

        var got = _index.Get(doc.Header.Id)!;
        got.Header.Should().Be(doc.Header);
        got.Chunks.Should().Equal(doc.Chunks);
        got.Chunks.Select(c => c.Index).Should().BeInAscendingOrder();
    }

    [Fact]
    public void Upsert_ShouldReplaceContent_WhenIdExists()
    {
        var old = MakeDoc("oldterm alpha\n\n" + new string('o', 90) + "\n\nanotherold", created: T0);
        _index.Upsert(old);
        var other = Add("unrelated stuff", created: T0.AddHours(1));

        var replacement = MakeDoc("newterm beta", id: old.Header.Id, title: "New", created: T0, updated: T0.AddDays(1));
        _index.Upsert(replacement);

        _index.List(1, 10).Total.Should().Be(2);
        _index.Search($"doc_id:{old.Header.Id} AND content:oldterm", 1, 10).Total.Should().Be(0);
        _index.Search($"doc_id:{old.Header.Id} AND content:newterm", 1, 10).Total.Should().Be(1);
        _index.Search($"doc_id:{old.Header.Id}", 1, 10).Total.Should().Be(1);
        var got = _index.Get(old.Header.Id)!;
        got.Header.Title.Should().Be("New");
        got.Header.CreatedUtc.Should().Be(T0);
        got.Header.UpdatedUtc.Should().Be(T0.AddDays(1));
        got.Chunks.Should().ContainSingle();
        _index.Get(other.Header.Id)!.Chunks.Should().Equal(other.Chunks);
    }

    [Fact]
    public void Upsert_ShouldRollBackAndKeepPriorState_WhenChunkCannotBeIndexed()
    {
        var original = MakeDoc("keepme original text", created: T0);
        _index.Upsert(original);

        var broken = new RagDocument(
            original.Header with { Title = "Broken", ChunkCount = 2 },
            new[] { new TextChunk(0, 0, 3, "fine"), new TextChunk(1, 3, 6, null!) });
        var act = () => _index.Upsert(broken);
        act.Should().Throw<ArgumentNullException>();

        _index.Get(original.Header.Id)!.Should().BeEquivalentTo(original);
        _index.List(1, 10).Total.Should().Be(1);
        _index.Search("keepme", 1, 10).Total.Should().Be(1);

        // The write lock was released by the rollback, so a normal write still succeeds.
        Add("later doc");
        _index.List(1, 10).Total.Should().Be(2);
    }

    [Fact]
    public void Upsert_ShouldThrowAndLeaveIndexUnchanged_WhenCancelledBeforeWriterOpens()
    {
        var original = Add("persistent text");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var act = () => _index.Upsert(MakeDoc("other", id: original.Header.Id), cts.Token);
        act.Should().Throw<OperationCanceledException>();
        _index.Get(original.Header.Id)!.Chunks.Should().Equal(original.Chunks);
    }

    private sealed class CancellingChunks(IReadOnlyList<TextChunk> inner, CancellationTokenSource cts, int cancelAtIndex) : IReadOnlyList<TextChunk>
    {
        public int Count => inner.Count;
        public TextChunk this[int index] => inner[index];
        public IEnumerator<TextChunk> GetEnumerator()
        {
            for (var i = 0; i < inner.Count; i++)
            {
                if (i == cancelAtIndex) cts.Cancel();
                yield return inner[i];
            }
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [Fact]
    public void Upsert_ShouldRollBackAndReleaseLock_WhenCancelledAfterDeleteAndFirstChunkAdded()
    {
        var original = Add("old persistent text about badgers " + new string('q', 300) + "\n\n" + new string('r', 300));
        var replacement = MakeDoc(string.Join("\n\n", Enumerable.Range(0, 6).Select(i => $"fresh{i} " + new string('n', 80))), id: original.Header.Id);
        replacement.Chunks.Count.Should().BeGreaterThan(2);
        using var cts = new CancellationTokenSource();
        var cancelling = new RagDocument(replacement.Header, new CancellingChunks(replacement.Chunks, cts, 1));

        var act = () => _index.Upsert(cancelling, cts.Token);

        act.Should().Throw<OperationCanceledException>();
        var got = _index.Get(original.Header.Id)!;
        got.Header.Should().Be(original.Header);
        got.Chunks.Should().Equal(original.Chunks);
        _index.Search("badgers", 1, 10).Total.Should().Be(1);
        _index.Search("fresh0", 1, 10).Total.Should().Be(0);
        _index.List(1, 10).Total.Should().Be(1);
        _index.Upsert(MakeDoc("later write"));
        _index.List(1, 10).Total.Should().Be(2);
    }

    [Fact]
    public void Upsert_ShouldSurfaceFailureAndReleaseLock_WhenCommitFails()
    {
        var original = Add("persistent text");
        var thrown = false;
        _index.FaultHook = stage =>
        {
            if (stage != "beforeCommit" || thrown) return;
            thrown = true;
            throw new IOException("injected commit failure");
        };

        var act = () => _index.Upsert(MakeDoc("replacement", id: original.Header.Id));

        act.Should().Throw<IOException>().WithMessage("injected commit failure");
        _index.Get(original.Header.Id)!.Chunks.Should().Equal(original.Chunks);
        _index.Upsert(MakeDoc("next"));
        _index.List(1, 10).Total.Should().Be(2);
    }

    [Fact]
    public void Upsert_ShouldSurfaceFailureAndReleaseLock_WhenWriterCloseFails()
    {
        var thrown = false;
        _index.FaultHook = stage =>
        {
            if (stage != "beforeClose" || thrown) return;
            thrown = true;
            throw new IOException("injected close failure");
        };

        var act = () => _index.Upsert(MakeDoc("first"));

        act.Should().Throw<IOException>().WithMessage("injected close failure");
        _index.Upsert(MakeDoc("second"));
        _index.List(1, 10).Total.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void Delete_ShouldSurfaceFailureAndReleaseLock_WhenWriterCloseFails()
    {
        var doc = Add("to delete");
        var thrown = false;
        _index.FaultHook = stage =>
        {
            if (stage != "beforeClose" || thrown) return;
            thrown = true;
            throw new IOException("injected close failure");
        };

        var act = () => _index.Delete(doc.Header.Id);

        act.Should().Throw<IOException>();
        _index.Upsert(MakeDoc("after"));
        _index.List(1, 10).Total.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void Replace_ShouldReturnNullAndLeaveIndexUnchanged_WhenIdMissing()
    {
        var other = Add("bystander");

        _index.Replace(MakeDoc("ghost", id: RagIds.NewId())).Should().BeNull();

        _index.List(1, 10).Total.Should().Be(1);
        _index.Get(other.Header.Id)!.Chunks.Should().Equal(other.Chunks);
        _index.Upsert(MakeDoc("lock free"));
    }

    [Fact]
    public void Replace_ShouldNotCreateIndexDirectory_WhenIndexMissing()
    {
        _index.Replace(MakeDoc("ghost")).Should().BeNull();

        Directory.Exists(_path).Should().BeFalse();
    }

    [Fact]
    public void Replace_ShouldKeepStoredCreatedUtc_WhenIncomingHeaderCarriesAnotherValue()
    {
        var original = MakeDoc("old words " + new string('o', 150), title: "Old", created: T0, updated: T0);
        _index.Upsert(original);
        var incoming = MakeDoc("brand new words", id: original.Header.Id, title: "New", created: T0.AddDays(30), updated: T0.AddDays(31));

        var stored = _index.Replace(incoming);

        stored.Should().Be(incoming.Header with { CreatedUtc = T0 });
        _index.GetHeader(original.Header.Id).Should().Be(stored);
        _index.Get(original.Header.Id)!.Chunks.Should().Equal(incoming.Chunks);
        _index.Search("old", 1, 10).Total.Should().Be(0);
        _index.List(1, 10).Total.Should().Be(1);
    }

    [Fact]
    public void Delete_ShouldRemoveHeaderAndChunks_WhenIdExists()
    {
        var a = Add("alpha " + new string('a', 150) + "\n\n" + new string('b', 150));
        var b = Add("beta survives");
        a.Chunks.Count.Should().BeGreaterThan(1);

        _index.Delete(a.Header.Id).Should().BeTrue();

        _index.GetHeader(a.Header.Id).Should().BeNull();
        _index.Get(a.Header.Id).Should().BeNull();
        _index.Search($"doc_id:{a.Header.Id}", 1, 10).Total.Should().Be(0);
        _index.List(1, 10).Total.Should().Be(1);
        _index.Get(b.Header.Id).Should().NotBeNull();
    }

    [Fact]
    public void Delete_ShouldReturnFalseAndChangeNothing_WhenIdUnknown()
    {
        var a = Add("alpha");
        _index.Delete(RagIds.NewId()).Should().BeFalse();
        _index.List(1, 10).Total.Should().Be(1);
        _index.Get(a.Header.Id)!.Chunks.Should().Equal(a.Chunks);
    }

    [Fact]
    public void Delete_ShouldReturnFalseWithoutCreatingDirectory_WhenIndexMissing()
    {
        _index.Delete(RagIds.NewId()).Should().BeFalse();
        Directory.Exists(_path).Should().BeFalse();
    }

    [Fact]
    public void Reads_ShouldReturnEmptyAndNotCreateDirectory_WhenIndexMissing()
    {
        _index.GetHeader(RagIds.NewId()).Should().BeNull();
        _index.Get(RagIds.NewId()).Should().BeNull();
        var list = _index.List(1, 20);
        list.Total.Should().Be(0);
        list.Items.Should().BeEmpty();
        list.TotalPages.Should().Be(0);
        _index.Search("anything", 1, 10).Total.Should().Be(0);
        Directory.Exists(_path).Should().BeFalse();
    }

    [Fact]
    public void Reads_ShouldReturnEmptyAndNotThrow_WhenDirectoryExistsButIsEmpty()
    {
        Directory.CreateDirectory(_path);
        _index.List(1, 10).Total.Should().Be(0);
        _index.Search("x", 1, 10).Total.Should().Be(0);
        _index.Get(RagIds.NewId()).Should().BeNull();
        Directory.GetFileSystemEntries(_path).Should().BeEmpty();
    }

    [Fact]
    public void List_ShouldPage25Documents_WhenPageSize10()
    {
        var ids = new List<string>();
        for (var i = 0; i < 25; i++)
            ids.Add(Add($"doc {i}", title: $"T{i}", created: T0.AddMinutes(i)).Header.Id);

        var p1 = _index.List(1, 10);
        var p2 = _index.List(2, 10);
        var p3 = _index.List(3, 10);
        var p4 = _index.List(4, 10);

        p1.Items.Should().HaveCount(10);
        p2.Items.Should().HaveCount(10);
        p3.Items.Should().HaveCount(5);
        p4.Items.Should().BeEmpty();
        foreach (var p in new[] { p1, p2, p3, p4 })
        {
            p.Total.Should().Be(25);
            p.TotalPages.Should().Be(3);
            p.PageSize.Should().Be(10);
        }
        p4.Page.Should().Be(4);
        p1.Items.Concat(p2.Items).Concat(p3.Items).Select(h => h.Id).Should().Equal(ids);
    }

    [Fact]
    public void List_ShouldOrderByCreatedThenId_WhenTimestampsTie()
    {
        // Inserted in reverse creation order to prove ordering is not insertion order.
        var late = Add("late", created: T0.AddDays(2));
        var tieIds = new[] { "ffffffffffffffffffffffffffffffff", "00000000000000000000000000000001", "7777777777777777777777777777777a" };
        foreach (var id in tieIds) _index.Upsert(MakeDoc("tie", id: id, created: T0.AddDays(1)));
        var early = Add("early", created: T0);

        _index.List(1, 10).Items.Select(h => h.Id).Should().Equal(
            early.Header.Id,
            "00000000000000000000000000000001", "7777777777777777777777777777777a", "ffffffffffffffffffffffffffffffff",
            late.Header.Id);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(-1, 5)]
    public void ListAndSearch_ShouldThrow_WhenPagingInvalid(int page, int size)
    {
        ((Action)(() => _index.List(page, size))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => _index.Search("x", page, size))).Should().Throw<ArgumentOutOfRangeException>();
    }

    private (RagDocument A, RagDocument B, RagDocument C) SeedSearch()
    {
        var a = Add("The quick brown fox jumps over the lazy dog.\n\nSecond paragraph about wombats.", title: "Animals Guide");
        var b = Add("Quick thinking saves the day. A brown bear sleeps.", title: "Bears");
        var c = Add("Completely different topic: database indexing with Lucene.", title: "Databases");
        return (a, b, c);
    }

    [Fact]
    public void Search_ShouldMatchDefaultFieldTerm_WhenTermGiven()
    {
        var (a, b, _) = SeedSearch();
        var r = _index.Search("quick", 1, 10);
        r.Total.Should().Be(2);
        r.Items.Select(h => h.Id).Should().BeEquivalentTo(new[] { a.Header.Id, b.Header.Id });
        r.Items.Should().OnlyContain(h => h.Score > 0 && h.Content.Length > 0);
    }

    [Fact]
    public void Search_ShouldReturnChunkFields_WhenHitFound()
    {
        var (a, _, _) = SeedSearch();
        var hit = _index.Search("wombats", 1, 10).Items.Single();
        var chunk = a.Chunks.Single(c => c.Content.Contains("wombats"));
        hit.Id.Should().Be(a.Header.Id);
        hit.Title.Should().Be("Animals Guide");
        hit.ChunkIndex.Should().Be(chunk.Index);
        hit.Start.Should().Be(chunk.Start);
        hit.End.Should().Be(chunk.End);
        hit.Content.Should().Be(chunk.Content);
    }

    [Fact]
    public void Search_ShouldMatchPhrase_WhenQuoted()
    {
        var (a, _, _) = SeedSearch();
        _index.Search("\"quick brown fox\"", 1, 10).Items.Select(h => h.Id).Should().Equal(a.Header.Id);
        _index.Search("\"brown quick\"", 1, 10).Total.Should().Be(0);
    }

    [Fact]
    public void Search_ShouldHonourBooleanOperators_WhenUsed()
    {
        var (a, b, c) = SeedSearch();
        _index.Search("quick AND brown", 1, 10).Total.Should().Be(2);
        _index.Search("fox AND bear", 1, 10).Total.Should().Be(0);
        _index.Search("fox OR lucene", 1, 10).Items.Select(h => h.Id).Should().BeEquivalentTo(new[] { a.Header.Id, c.Header.Id });
        _index.Search("brown AND NOT fox", 1, 10).Items.Select(h => h.Id).Should().Equal(b.Header.Id);
    }

    [Fact]
    public void Search_ShouldMatchExactly_WhenDocIdFieldQueried()
    {
        var (a, _, _) = SeedSearch();
        var r = _index.Search($"doc_id:{a.Header.Id}", 1, 10);
        r.Total.Should().Be(a.Chunks.Count);
        r.Items.Should().OnlyContain(h => h.Id == a.Header.Id);
        _index.Search($"doc_id:{a.Header.Id.ToUpperInvariant()}", 1, 10).Total.Should().Be(0);
    }

    [Fact]
    public void Search_ShouldMatchTitleField_WhenTitleQueried()
    {
        var (_, b, _) = SeedSearch();
        _index.Search("title:bears", 1, 10).Items.Select(h => h.Id).Should().Equal(b.Header.Id);
        _index.Search("title:guide AND quick", 1, 10).Total.Should().Be(1);
    }

    [Fact]
    public void Search_ShouldSupportWildcardsAndFuzzy_WhenUsed()
    {
        var (a, _, c) = SeedSearch();
        _index.Search("womb*", 1, 10).Items.Select(h => h.Id).Should().Equal(a.Header.Id);
        _index.Search("luc?ne", 1, 10).Items.Select(h => h.Id).Should().Equal(c.Header.Id);
        _index.Search("wombat~", 1, 10).Items.Select(h => h.Id).Should().Equal(a.Header.Id);
    }

    [Fact]
    public void Search_ShouldReturnOnlyChunks_WhenQueryMatchesHeaderFields()
    {
        var a = Add("body text only", title: "uniquetitleword");
        _index.Search("kind:header", 1, 10).Total.Should().Be(0);
        _index.Search("file_name:file.txt", 1, 10).Total.Should().Be(0);
        _index.Search("title:uniquetitleword", 1, 10).Items.Should().OnlyContain(h => h.Id == a.Header.Id && h.Content == "body text only");
        _index.Search("title:uniquetitleword", 1, 10).Total.Should().Be(a.Chunks.Count);
    }

    [Fact]
    public void Search_ShouldPaginate_WhenManyHits()
    {
        for (var i = 0; i < 25; i++) Add($"common term number {i}");
        var all = new List<string>();
        var counts = new List<int>();
        foreach (var page in new[] { 1, 2, 3, 4 })
        {
            var r = _index.Search("common", page, 10);
            r.Total.Should().Be(25);
            r.TotalPages.Should().Be(3);
            counts.Add(r.Items.Count);
            all.AddRange(r.Items.Select(h => h.Id));
        }
        counts.Should().Equal(10, 10, 5, 0);
        all.Distinct().Should().HaveCount(25);
    }

    [Fact]
    public void Search_ShouldOrderByScoreDescending_WhenHitsDiffer()
    {
        var best = Add("lucene");
        Add("lucene lucene lucene and filler words around here to dilute the field norms a lot more");
        Add("something with lucene appearing once among many many other tokens in this chunk of text");
        var items = _index.Search("lucene", 1, 10).Items;
        items.Select(h => h.Score).Should().HaveCount(3).And.BeInDescendingOrder();
        items[0].Id.Should().Be(best.Header.Id);
        items[0].Score.Should().BeGreaterThan(items[1].Score);
    }

    [Theory]
    [InlineData("\"unterminated")]
    [InlineData("foo AND")]
    [InlineData("(a OR b")]
    [InlineData("*leading")]
    [InlineData("[a TO")]
    [InlineData("title:")]
    public void Search_ShouldThrowRagQueryException_WhenQueryInvalid(string query)
    {
        Add("some text");
        var act = () => _index.Search(query, 1, 10);
        act.Should().Throw<RagQueryException>().WithMessage("Invalid query: *");
    }

    [Fact]
    public void Search_ShouldThrowRagQueryException_WhenQueryInvalidAndIndexMissing()
    {
        var act = () => _index.Search("(a OR", 1, 10);
        act.Should().Throw<RagQueryException>().WithMessage("Invalid query: *");
        Directory.Exists(_path).Should().BeFalse();
    }

    [Fact]
    public void Upsert_ShouldThrowRagIndexLockedExceptionAndLeaveIndexUnchanged_WhenAnotherWriterHoldsLock()
    {
        var original = Add("before lock");
        var other = Add("second");

        using (var dir = FSDirectory.Open(_path))
        using (var analyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48))
        using (var holder = new IndexWriter(dir, new IndexWriterConfig(LuceneVersion.LUCENE_48, analyzer)))
        {
            var sw = Stopwatch.StartNew();
            var act = () => _index.Upsert(MakeDoc("blocked write", id: original.Header.Id));
            act.Should().Throw<RagIndexLockedException>().WithMessage($"Index '{_index.IndexPath}' is locked by another writer");
            sw.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(250)).And.BeLessThan(TimeSpan.FromSeconds(5));

            ((Action)(() => _index.Delete(other.Header.Id))).Should().Throw<RagIndexLockedException>();

            // Readers never block.
            _index.List(1, 10).Total.Should().Be(2);
        }

        _index.Get(original.Header.Id)!.Chunks.Should().Equal(original.Chunks);
        _index.Get(other.Header.Id).Should().NotBeNull();
    }

    [Fact]
    public async Task Upsert_ShouldSucceed_WhenLockReleasedWithinTimeout()
    {
        Add("seed");
        var longIndex = new LuceneRagIndex(_path, TimeSpan.FromSeconds(10));
        var dir = FSDirectory.Open(_path);
        var analyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);
        var holder = new IndexWriter(dir, new IndexWriterConfig(LuceneVersion.LUCENE_48, analyzer));
        var releaser = Task.Run(async () =>
        {
            await Task.Delay(400);
            holder.Dispose();
            analyzer.Dispose();
            dir.Dispose();
        });
        longIndex.Upsert(MakeDoc("after release"));
        await releaser;
        longIndex.List(1, 10).Total.Should().Be(2);
    }

    [Fact]
    public void Upsert_ShouldHonourCancellation_WhenWaitingForLock()
    {
        Add("seed");
        var longIndex = new LuceneRagIndex(_path, TimeSpan.FromSeconds(30));
        using var dir = FSDirectory.Open(_path);
        using var analyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);
        using var holder = new IndexWriter(dir, new IndexWriterConfig(LuceneVersion.LUCENE_48, analyzer));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var sw = Stopwatch.StartNew();
        var act = () => longIndex.Upsert(MakeDoc("x"), cts.Token);
        act.Should().Throw<OperationCanceledException>();
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Upsert_ShouldStoreBothDocuments_WhenTwoInstancesWriteConcurrently()
    {
        var a = new LuceneRagIndex(_path, TimeSpan.FromSeconds(5));
        var b = new LuceneRagIndex(_path, TimeSpan.FromSeconds(5));
        string Text(string tag) => string.Join("\n\n", Enumerable.Range(0, 8).Select(i => $"{tag}{i} " + new string('w', 70)));
        var docA = MakeDoc(Text("alpha"), title: "A");
        var docB = MakeDoc(Text("beta"), title: "B");
        docA.Chunks.Count.Should().BeGreaterThan(2);

        await Task.WhenAll(Task.Run(() => a.Upsert(docA)), Task.Run(() => b.Upsert(docB)));

        _index.List(1, 10).Items.Select(h => h.Id).Should().BeEquivalentTo([docA.Header.Id, docB.Header.Id]);
        _index.Get(docA.Header.Id)!.Chunks.Should().Equal(docA.Chunks);
        _index.Get(docB.Header.Id)!.Chunks.Should().Equal(docB.Chunks);
    }

    [Fact]
    public void IndexPath_ShouldBeAbsolute_WhenRelativePathGiven()
    {
        new LuceneRagIndex("some/rel").IndexPath.Should().Be(Path.GetFullPath("some/rel"));
    }

    [Fact]
    public void RagPage_ShouldComputeTotalPages_WhenTotalsVary()
    {
        new RagPage<int>(1, 10, 0, Array.Empty<int>()).TotalPages.Should().Be(0);
        new RagPage<int>(1, 10, 1, Array.Empty<int>()).TotalPages.Should().Be(1);
        new RagPage<int>(1, 10, 10, Array.Empty<int>()).TotalPages.Should().Be(1);
        new RagPage<int>(1, 10, 11, Array.Empty<int>()).TotalPages.Should().Be(2);
    }

    [Fact]
    public void Get_ShouldReturnNull_WhenIdUnknown()
    {
        Add("something");
        _index.Get(RagIds.NewId()).Should().BeNull();
        _index.GetHeader(RagIds.NewId()).Should().BeNull();
    }
}
