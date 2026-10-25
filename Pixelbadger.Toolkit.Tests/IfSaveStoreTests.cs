using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Tests;

public class IfSaveStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    private readonly string _game;
    private readonly List<IfSaveStore> _stores = [];

    public IfSaveStoreTests()
    {
        Directory.CreateDirectory(_root);
        _game = Path.Combine(_root, "story.z8");
        File.WriteAllBytes(_game, [1, 2, 3, 4, 5]);
    }

    public void Dispose()
    {
        foreach (var s in _stores) s.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private IfSaveStore Open(string? game = null)
    {
        var store = IfSaveStore.Open(game ?? _game, Path.Combine(_root, "saves"));
        _stores.Add(store);
        return store;
    }

    private static IfTranscriptEvent Event(string text = "t", string? output = null, string kind = IfEventKinds.Command) =>
        new(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), IfActors.Model, kind, text, output);

    private static void WriteAutosave(IfSaveStore store, string content = "save") => File.WriteAllText(store.AutosavePath, content);

    [Fact]
    public void Open_ShouldUseStemAndSha8DirectoryName()
    {
        var expected = Convert.ToHexString(SHA256.HashData([1, 2, 3, 4, 5])).ToLowerInvariant()[..8];

        var store = Open();

        Path.GetFileName(store.GameDirectory).Should().Be($"story-{expected}");
        Path.GetDirectoryName(store.GameDirectory).Should().Be(Path.Combine(_root, "saves"));
        Directory.Exists(store.GameDirectory).Should().BeTrue();
    }

    [Fact]
    public void Open_ShouldUseDifferentDirectory_WhenContentDiffers()
    {
        var other = Path.Combine(_root, "story2.z8");
        File.WriteAllBytes(other, [9, 9]);
        var a = Open();
        var b = Open(other);

        a.GameDirectory.Should().NotBe(b.GameDirectory);
    }

    [Fact]
    public void Open_ShouldDefaultRootToUserProfilePbtkIf()
    {
        IfSaveStore.DefaultRootDirectory.Should().Be(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".pbtk", "if"));

        var sha8 = Convert.ToHexString(SHA256.HashData([1, 2, 3, 4, 5])).ToLowerInvariant()[..8];
        var expectedDirectory = Path.Combine(IfSaveStore.DefaultRootDirectory, $"story-{sha8}");
        var existedBefore = Directory.Exists(expectedDirectory);
        try
        {
            string directory;
            using (var store = IfSaveStore.Open(_game))
                directory = store.GameDirectory;
            directory.Should().Be(expectedDirectory);
        }
        finally
        {
            if (!existedBefore && Directory.Exists(expectedDirectory))
                Directory.Delete(expectedDirectory, true);
        }
    }

    [Fact]
    public void Open_ShouldThrowIfStoreException_WhenAlreadyLocked()
    {
        Open();

        var act = () => IfSaveStore.Open(_game, Path.Combine(_root, "saves"));

        act.Should().Throw<IfStoreException>().WithMessage("Another llm if run is using this game's saves.");
    }

    [Fact]
    public void Dispose_ShouldReleaseLock_AndDeleteLockFile()
    {
        var store = Open();
        var lockPath = Path.Combine(store.GameDirectory, "run.lock");
        File.Exists(lockPath).Should().BeTrue();

        store.Dispose();

        File.Exists(lockPath).Should().BeFalse();
        var reopened = Open();
        reopened.Should().NotBeNull();
    }

    [Fact]
    public void Dispose_ShouldBeIdempotent()
    {
        var store = Open();
        store.Dispose();
        var act = () => store.Dispose();
        act.Should().NotThrow();
    }

    [Fact]
    public void CreateTempSavePath_ShouldReturnUniqueNonExistingPathInGameDirectory()
    {
        var store = Open();

        var a = store.CreateTempSavePath();
        var b = store.CreateTempSavePath();

        a.Should().NotBe(b);
        Path.GetDirectoryName(a).Should().Be(store.GameDirectory);
        Path.IsPathRooted(a).Should().BeTrue();
        Path.GetFileName(a).Should().MatchRegex("^t[0-9a-f]{8}\\.qzl$");
        File.Exists(a).Should().BeFalse();
    }

    [Fact]
    public void CommitAutosave_ShouldReplaceExistingAutosave_AndRemoveTemp()
    {
        var store = Open();
        WriteAutosave(store, "old");
        var temp = store.CreateTempSavePath();
        File.WriteAllText(temp, "new");

        store.CommitAutosave(temp);

        File.ReadAllText(store.AutosavePath).Should().Be("new");
        File.Exists(temp).Should().BeFalse();
        store.HasAutosave.Should().BeTrue();
    }

    [Fact]
    public void HasAutosave_ShouldBeFalse_WhenNoAutosave()
    {
        Open().HasAutosave.Should().BeFalse();
    }

    [Fact]
    public void DiscardTemp_ShouldDeleteFile_AndIgnoreMissing()
    {
        var store = Open();
        var temp = store.CreateTempSavePath();
        File.WriteAllText(temp, "x");

        store.DiscardTemp(temp);
        var act = () => store.DiscardTemp(temp);

        File.Exists(temp).Should().BeFalse();
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("")]
    [InlineData("a b")]
    [InlineData("../x")]
    [InlineData("a/b")]
    [InlineData("a.b")]
    [InlineData("é")]
    [InlineData("a\n")]
    [InlineData("alley\n")]
    public void SaveSlot_ShouldThrow_WhenNameInvalid(string name)
    {
        var store = Open();
        WriteAutosave(store);

        var act = () => store.SaveSlot(name);

        act.Should().Throw<IfStoreException>().WithMessage($"Invalid slot name '{name}'. Use 1-32 letters, digits, '-' or '_'.");
    }

    [Fact]
    public void SaveSlot_ShouldReject33CharacterName_AndAccept32()
    {
        var store = Open();
        WriteAutosave(store);

        store.SaveSlot(new string('a', 32));
        var act = () => store.SaveSlot(new string('a', 33));

        act.Should().Throw<IfStoreException>().WithMessage("Invalid slot name*");
        store.ListSlots().Should().ContainSingle();
    }

    [Fact]
    public void SaveSlot_ShouldThrow_WhenNoAutosave()
    {
        var act = () => Open().SaveSlot("a");

        act.Should().Throw<IfStoreException>().WithMessage("No game to save yet.");
    }

    [Fact]
    public void SaveSlot_ShouldCopyAutosave_AndListSlotsOrdinalSorted()
    {
        var store = Open();
        WriteAutosave(store, "state1");

        store.SaveSlot("b_2");
        store.SaveSlot("B-1");
        store.SaveSlot("a");

        store.ListSlots().Should().Equal("B-1", "a", "b_2");
        File.ReadAllText(Path.Combine(store.GameDirectory, "slots", "a.qzl")).Should().Be("state1");
    }

    [Fact]
    public void ListSlots_ShouldBeEmpty_WhenNoSlotsDirectory()
    {
        Open().ListSlots().Should().BeEmpty();
    }

    [Fact]
    public void SaveSlot_ShouldEnforceTwentySlotLimit_ForNewNames()
    {
        var store = Open();
        WriteAutosave(store);
        for (var i = 0; i < 20; i++) store.SaveSlot($"s{i:00}");

        var act = () => store.SaveSlot("extra");

        act.Should().Throw<IfStoreException>().WithMessage("Slot limit (20) reached. Overwrite an existing slot or delete one.");
        store.ListSlots().Should().HaveCount(20).And.NotContain("extra");
    }

    [Fact]
    public void SaveSlot_ShouldAllowOverwrite_WhenAtLimit()
    {
        var store = Open();
        WriteAutosave(store, "v1");
        for (var i = 0; i < 20; i++) store.SaveSlot($"s{i:00}");
        WriteAutosave(store, "v2");

        store.SaveSlot("s05");

        File.ReadAllText(Path.Combine(store.GameDirectory, "slots", "s05.qzl")).Should().Be("v2");
        store.ListSlots().Should().HaveCount(20);
    }

    [Fact]
    public void RestoreSlot_ShouldCopySlotOverAutosave_AndKeepSlot()
    {
        var store = Open();
        WriteAutosave(store, "at-alley");
        store.SaveSlot("alley");
        WriteAutosave(store, "elsewhere");

        store.RestoreSlot("alley");

        File.ReadAllText(store.AutosavePath).Should().Be("at-alley");
        store.ListSlots().Should().Equal("alley");
        Directory.GetFiles(store.GameDirectory, "t*.qzl").Should().BeEmpty();
    }

    [Fact]
    public void RestoreSlot_ShouldThrow_WhenSlotMissing()
    {
        var act = () => Open().RestoreSlot("ghost");

        act.Should().Throw<IfStoreException>().WithMessage("No slot named 'ghost'.");
    }

    [Fact]
    public void RestoreSlot_ShouldThrow_WhenNameInvalid()
    {
        var act = () => Open().RestoreSlot("../autosave");

        act.Should().Throw<IfStoreException>().WithMessage("Invalid slot name '../autosave'.*");
    }

    [Fact]
    public void ReadNotes_ShouldReturnEmpty_WhenMissing()
    {
        Open().ReadNotes().Should().BeEmpty();
    }

    [Fact]
    public void WriteNotes_ShouldReplaceWholesale()
    {
        var store = Open();

        store.WriteNotes("first");
        store.WriteNotes("second");

        store.ReadNotes().Should().Be("second");
    }

    [Fact]
    public void WriteNotes_ShouldAccept8000Characters_AndRejectMore()
    {
        var store = Open();

        store.WriteNotes(new string('n', 8000));
        var act = () => store.WriteNotes(new string('n', 8001));

        act.Should().Throw<IfStoreException>().WithMessage("Notes are longer than 8000 characters.");
        store.ReadNotes().Should().HaveLength(8000);
    }

    [Fact]
    public void ReadEvents_ShouldBeEmpty_WhenNoTranscript()
    {
        Open().ReadEvents().Should().BeEmpty();
    }

    [Fact]
    public void AppendEvent_ShouldRoundTripEventsInOrder()
    {
        var store = Open();
        var first = Event("look", "A \"room\"\nwith lines — é");
        var second = Event("thought", null, IfEventKinds.Thought);

        store.AppendEvent(first);
        store.AppendEvent(second);

        store.ReadEvents().Should().Equal(first, second);
    }

    [Fact]
    public void AppendEvent_ShouldWriteOneJsonLinePerEvent_WithExpectedPropertyNames_AndOmitNullOutput()
    {
        var store = Open();
        store.AppendEvent(Event("hi"));
        store.AppendEvent(Event("hi", "out"));

        var lines = File.ReadAllLines(Path.Combine(store.GameDirectory, "transcript.jsonl"));

        lines.Should().HaveCount(2);
        lines[0].Should().Be("{\"ts\":\"2026-01-02T03:04:05.0000000+00:00\",\"actor\":\"model\",\"kind\":\"command\",\"text\":\"hi\"}");
        lines[1].Should().EndWith(",\"output\":\"out\"}");
    }

    [Fact]
    public void AppendEvent_ShouldFlushImmediately_SoAnotherReaderSeesIt()
    {
        var store = Open();
        store.AppendEvent(Event("one"));

        using var reader = new FileStream(Path.Combine(store.GameDirectory, "transcript.jsonl"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        new StreamReader(reader).ReadToEnd().Should().Contain("\"text\":\"one\"");
    }

    [Fact]
    public void AppendEvent_ShouldStartNewLine_WhenTranscriptEndsWithTornFragment()
    {
        var store = Open();
        store.AppendEvent(Event("good1"));
        File.AppendAllText(Path.Combine(store.GameDirectory, "transcript.jsonl"), "{\"ts\":\"2026-01-02T03:04:0");

        store.AppendEvent(Event("good2"));

        store.ReadEvents().Select(e => e.Text).Should().Equal("good1", "good2");
    }

    [Fact]
    public void ReadEvents_ShouldSkipTornLastLine_AndGarbageLines()
    {
        var store = Open();
        store.AppendEvent(Event("good1"));
        var path = Path.Combine(store.GameDirectory, "transcript.jsonl");
        File.AppendAllText(path, "not json\n{\"ts\":\"2026-01-02T03:04:05+00:00\",\"actor\":\"model\"}\n");
        store.AppendEvent(Event("good2"));
        File.AppendAllText(path, "{\"ts\":\"2026-01-02T03:04:05+00:00\",\"actor\":\"mod");

        var events = store.ReadEvents();

        events.Select(e => e.Text).Should().Equal("good1", "good2");
    }

    [Fact]
    public void ReadEvents_ShouldSkipLine_WhenTimestampInvalid()
    {
        var store = Open();
        File.WriteAllText(Path.Combine(store.GameDirectory, "transcript.jsonl"),
            "{\"ts\":\"yesterday\",\"actor\":\"model\",\"kind\":\"command\",\"text\":\"x\"}\n");

        store.ReadEvents().Should().BeEmpty();
    }

    [Fact]
    public void ArchiveCurrent_ShouldMoveExistingFiles_KeepSlots()
    {
        var store = Open();
        WriteAutosave(store, "auto");
        store.SaveSlot("keep");
        store.AppendEvent(Event("x"));
        store.WriteNotes("notes");

        store.ArchiveCurrent();

        store.HasAutosave.Should().BeFalse();
        store.ReadEvents().Should().BeEmpty();
        store.ReadNotes().Should().BeEmpty();
        store.ListSlots().Should().Equal("keep");
        var archives = Directory.GetDirectories(Path.Combine(store.GameDirectory, "archive"));
        archives.Should().ContainSingle();
        Path.GetFileName(archives[0]).Should().MatchRegex(@"^\d{8}-\d{6}$");
        File.ReadAllText(Path.Combine(archives[0], "autosave.qzl")).Should().Be("auto");
        File.ReadAllText(Path.Combine(archives[0], "notes.md")).Should().Be("notes");
        File.Exists(Path.Combine(archives[0], "transcript.jsonl")).Should().BeTrue();
    }

    [Fact]
    public void ArchiveCurrent_ShouldDoNothing_WhenNoAutosaveTranscriptOrNotes()
    {
        var store = Open();
        var archiveRoot = Path.Combine(store.GameDirectory, "archive");
        for (var i = 1; i <= 5; i++)
            Directory.CreateDirectory(Path.Combine(archiveRoot, $"2020010{i}-000000"));

        store.ArchiveCurrent();

        Directory.GetDirectories(archiveRoot).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal)
            .Should().Equal(Enumerable.Range(1, 5).Select(i => $"2020010{i}-000000"));
    }

    [Fact]
    public void ArchiveCurrent_ShouldNotCreateArchiveDirectory_WhenNothingToArchive()
    {
        var store = Open();

        store.ArchiveCurrent();

        Directory.Exists(Path.Combine(store.GameDirectory, "archive")).Should().BeFalse();
    }

    [Fact]
    public void WriteNotes_ShouldLeaveNoTempFiles_AndReplaceContent()
    {
        var store = Open();
        store.WriteNotes("one");
        store.WriteNotes("two");

        store.ReadNotes().Should().Be("two");
        Directory.GetFiles(store.GameDirectory).Select(Path.GetFileName).Should().NotContain(n => n!.EndsWith(".tmp"));
    }

    [Fact]
    public void SaveSlotAndRestoreSlot_ShouldLeaveNoTempFiles()
    {
        var store = Open();
        WriteAutosave(store, "state");
        store.SaveSlot("a");
        store.RestoreSlot("a");

        Directory.GetFiles(store.GameDirectory, "*.tmp").Should().BeEmpty();
        Directory.GetFiles(Path.Combine(store.GameDirectory, "slots"), "*.tmp").Should().BeEmpty();
        File.ReadAllText(store.AutosavePath).Should().Be("state");
    }

    [Fact]
    public void ArchiveCurrent_ShouldOnlyMoveFilesThatExist()
    {
        var store = Open();
        store.WriteNotes("only notes");

        store.ArchiveCurrent();

        var archive = Directory.GetDirectories(Path.Combine(store.GameDirectory, "archive")).Single();
        Directory.GetFiles(archive).Select(Path.GetFileName).Should().Equal("notes.md");
    }

    [Fact]
    public void ArchiveCurrent_ShouldUseSuffix_WhenSameSecondDirectoryTaken()
    {
        var store = Open();
        for (var i = 0; i < 3; i++)
        {
            store.WriteNotes("n" + i);
            store.ArchiveCurrent();
        }

        var names = Directory.GetDirectories(Path.Combine(store.GameDirectory, "archive")).Select(Path.GetFileName).ToList();
        names.Should().HaveCount(3).And.OnlyHaveUniqueItems();
    }

    [Fact]
    public void ArchiveCurrent_ShouldKeepOnlyFiveMostRecent_DeletingOldest()
    {
        var store = Open();
        var archiveRoot = Path.Combine(store.GameDirectory, "archive");
        for (var i = 1; i <= 5; i++)
            Directory.CreateDirectory(Path.Combine(archiveRoot, $"2020010{i}-000000"));

        store.WriteNotes("sixth");
        store.ArchiveCurrent();

        var names = Directory.GetDirectories(archiveRoot).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToList();
        names.Should().HaveCount(5);
        names.Should().NotContain("20200101-000000");
        names.Should().Contain("20200102-000000");
        names.Last().Should().NotStartWith("2020");
    }
}
