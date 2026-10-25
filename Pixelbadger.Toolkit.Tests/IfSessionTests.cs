using FluentAssertions;
using Moq;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Tests;

public class IfSessionTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 6, 7, 8, 9, TimeSpan.Zero);

    private readonly Mock<IIfGameEngine> _engine = new();
    private readonly Mock<IIfSaveStore> _store = new();
    private readonly List<IfTranscriptEvent> _appended = [];

    public IfSessionTests()
    {
        _store.Setup(s => s.ReadEvents()).Returns([]);
        _store.Setup(s => s.AppendEvent(It.IsAny<IfTranscriptEvent>())).Callback<IfTranscriptEvent>(e => _appended.Add(e));
    }

    private IfSession Create() => new(_engine.Object, _store.Object, "Anchorhead", () => Now);

    private void EngineReturns(string output, IfTurnStatus status = IfTurnStatus.Saved) =>
        _engine.Setup(e => e.RunTurnAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new IfTurnResult(output, status));

    [Fact]
    public void Constructor_ShouldLoadExistingEvents_AndCountCommands()
    {
        _store.Setup(s => s.ReadEvents()).Returns(
        [
            new IfTranscriptEvent(Now, IfActors.System, IfEventKinds.Command, "(new game)", "intro"),
            new IfTranscriptEvent(Now, IfActors.Model, IfEventKinds.Thought, "hm"),
            new IfTranscriptEvent(Now, IfActors.Human, IfEventKinds.Command, "look", "room"),
        ]);

        var session = Create();

        session.GameName.Should().Be("Anchorhead");
        session.Events.Should().HaveCount(3);
        session.TurnCount.Should().Be(2);
    }

    [Fact]
    public void Events_ShouldReturnSnapshotCopy()
    {
        var session = Create();
        var snapshot = session.Events;

        session.RecordThought("later");

        snapshot.Should().BeEmpty();
        session.Events.Should().ContainSingle();
    }

    [Fact]
    public async Task StartAsync_ShouldStartNewGameAndAppendSystemCommand_WhenNoAutosave()
    {
        _store.Setup(s => s.HasAutosave).Returns(false);
        _engine.Setup(e => e.StartNewGameAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new IfTurnResult("Opening text", IfTurnStatus.Saved));
        var session = Create();

        var result = await session.StartAsync(default);

        result.Should().Be(new IfActionResult(true, "Opening text"));
        _appended.Should().Equal(new IfTranscriptEvent(Now, IfActors.System, IfEventKinds.Command, "(new game)", "Opening text"));
        session.TurnCount.Should().Be(1);
    }

    [Fact]
    public async Task StartAsync_ShouldBeNoOp_WhenAutosaveExists()
    {
        _store.Setup(s => s.HasAutosave).Returns(true);
        var session = Create();

        var result = await session.StartAsync(default);

        result.Success.Should().BeTrue();
        _engine.Verify(e => e.StartNewGameAsync(It.IsAny<CancellationToken>()), Times.Never);
        _appended.Should().BeEmpty();
    }

    [Fact]
    public async Task StartAsync_ShouldAppendNothingAndFail_WhenNewGameNotSaved()
    {
        _store.Setup(s => s.HasAutosave).Returns(false);
        _engine.Setup(e => e.StartNewGameAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new IfTurnResult("Intro", IfTurnStatus.NotSaved));
        var session = Create();

        var result = await session.StartAsync(default);

        result.Should().Be(new IfActionResult(false, "The interpreter did not save the new game."));
        _appended.Should().BeEmpty();
        session.TurnCount.Should().Be(0);
    }

    [Theory]
    [InlineData(IfTurnStatus.Failed, "The interpreter failed (exit code 2).", "The interpreter failed (exit code 2).")]
    [InlineData(IfTurnStatus.TimedOut, "The interpreter timed out.", "The interpreter timed out.")]
    public async Task StartAsync_ShouldRecordError_WhenEngineFails(IfTurnStatus status, string engineOutput, string expected)
    {
        _store.Setup(s => s.HasAutosave).Returns(false);
        _engine.Setup(e => e.StartNewGameAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new IfTurnResult(engineOutput, status));
        var session = Create();

        var result = await session.StartAsync(default);

        result.Should().Be(new IfActionResult(false, expected));
        _appended.Should().Equal(new IfTranscriptEvent(Now, IfActors.System, IfEventKinds.Error, expected));
    }

    [Theory]
    [InlineData(IfActors.Model, "save", "Use the save/restore slot tools instead of in-game save, restore, quit or restart.")]
    [InlineData(IfActors.Human, "quit", "Use /save, /restore or /quit.")]
    [InlineData(IfActors.Model, "\\foo", "Commands may not start with a backslash.")]
    [InlineData(IfActors.Human, "  ", "Command cannot be empty.")]
    public async Task PlayAsync_ShouldRejectInvalidCommand_WithoutEngineOrEvents(string actor, string command, string message)
    {
        var session = Create();

        var result = await session.PlayAsync(actor, command, "thinking", default);

        result.Should().Be(new IfActionResult(false, message));
        _engine.Verify(e => e.RunTurnAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _appended.Should().BeEmpty();
        session.TurnCount.Should().Be(0);
    }

    [Fact]
    public async Task PlayAsync_ShouldAppendThoughtThenCommand_WhenSaved()
    {
        EngineReturns("You see a door.");
        var session = Create();

        var result = await session.PlayAsync(IfActors.Model, "  look  ", "How eerie.", default);

        result.Should().Be(new IfActionResult(true, "You see a door."));
        _engine.Verify(e => e.RunTurnAsync("look", It.IsAny<CancellationToken>()), Times.Once);
        _appended.Should().Equal(
            new IfTranscriptEvent(Now, IfActors.Model, IfEventKinds.Thought, "How eerie."),
            new IfTranscriptEvent(Now, IfActors.Model, IfEventKinds.Command, "look", "You see a door."));
        session.TurnCount.Should().Be(1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PlayAsync_ShouldSkipThought_WhenEmpty(string? thought)
    {
        EngineReturns("ok");
        var session = Create();

        await session.PlayAsync(IfActors.Human, "look", thought, default);

        _appended.Should().ContainSingle().Which.Kind.Should().Be(IfEventKinds.Command);
        _appended[0].Actor.Should().Be(IfActors.Human);
    }

    [Fact]
    public async Task PlayAsync_ShouldAppendNotSavedWarning_WhenNotSaved()
    {
        EngineReturns("You die.", IfTurnStatus.NotSaved);
        var session = Create();

        var result = await session.PlayAsync(IfActors.Model, "jump", null, default);

        const string expected = "You die.\n[not saved: the game may have ended]";
        result.Should().Be(new IfActionResult(true, expected));
        _appended.Should().Equal(new IfTranscriptEvent(Now, IfActors.Model, IfEventKinds.Command, "jump", expected));
    }

    [Fact]
    public async Task PlayAsync_ShouldRecordSystemError_WhenTimedOut()
    {
        EngineReturns("The interpreter timed out.", IfTurnStatus.TimedOut);
        var session = Create();

        var result = await session.PlayAsync(IfActors.Model, "look", "hm", default);

        result.Should().Be(new IfActionResult(false, "The interpreter timed out."));
        _appended.Should().Equal(
            new IfTranscriptEvent(Now, IfActors.Model, IfEventKinds.Thought, "hm"),
            new IfTranscriptEvent(Now, IfActors.System, IfEventKinds.Error, "The interpreter timed out."));
        session.TurnCount.Should().Be(0);
    }

    [Fact]
    public async Task PlayAsync_ShouldRecordSystemError_WhenFailed()
    {
        EngineReturns("The interpreter failed (exit code 9).", IfTurnStatus.Failed);
        var session = Create();

        var result = await session.PlayAsync(IfActors.Human, "look", null, default);

        result.Should().Be(new IfActionResult(false, "The interpreter failed (exit code 9)."));
        _appended.Should().Equal(new IfTranscriptEvent(Now, IfActors.System, IfEventKinds.Error, "The interpreter failed (exit code 9)."));
    }

    [Fact]
    public async Task PlayAsync_ShouldRaiseChangedAfterEachEvent_OutsideLock()
    {
        EngineReturns("ok");
        var session = Create();
        var counts = new List<int>();
        session.Changed += () => counts.Add(session.Events.Count);

        await session.PlayAsync(IfActors.Model, "look", "thought", default);

        counts.Should().Equal(1, 2);
    }

    [Fact]
    public async Task PlayAsync_ShouldSerialiseConcurrentCalls()
    {
        var active = 0;
        var maxActive = 0;
        _engine.Setup(e => e.RunTurnAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                var now = Interlocked.Increment(ref active);
                maxActive = Math.Max(maxActive, now);
                await Task.Delay(30);
                Interlocked.Decrement(ref active);
                return new IfTurnResult("ok", IfTurnStatus.Saved);
            });
        var session = Create();

        await Task.WhenAll(Enumerable.Range(0, 6).Select(i => session.PlayAsync(IfActors.Model, $"look {i}", null, default)));

        maxActive.Should().Be(1);
        session.TurnCount.Should().Be(6);
    }

    [Fact]
    public async Task PlayAsync_ShouldThrow_WhenCancelledWhileWaitingForLock()
    {
        var release = new TaskCompletionSource<IfTurnResult>();
        _engine.Setup(e => e.RunTurnAsync("first", It.IsAny<CancellationToken>())).Returns(release.Task);
        var session = Create();
        var first = session.PlayAsync(IfActors.Model, "first", null, default);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var act = () => session.PlayAsync(IfActors.Human, "second", null, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        release.SetResult(new IfTurnResult("done", IfTurnStatus.Saved));
        (await first).Success.Should().BeTrue();
    }

    [Fact]
    public async Task SaveSlotAsync_ShouldSaveAndAppendEvent()
    {
        var session = Create();

        var result = await session.SaveSlotAsync(IfActors.Human, "alley", default);

        result.Should().Be(new IfActionResult(true, "Saved slot 'alley'."));
        _store.Verify(s => s.SaveSlot("alley"), Times.Once);
        _appended.Should().Equal(new IfTranscriptEvent(Now, IfActors.Human, IfEventKinds.Save, "alley"));
    }

    [Fact]
    public async Task SaveSlotAsync_ShouldReturnError_AndAppendNothing_WhenStoreThrows()
    {
        _store.Setup(s => s.SaveSlot("bad name")).Throws(new IfStoreException("Invalid slot name 'bad name'. Use 1-32 letters, digits, '-' or '_'."));
        var session = Create();

        var result = await session.SaveSlotAsync(IfActors.Model, "bad name", default);

        result.Should().Be(new IfActionResult(false, "Invalid slot name 'bad name'. Use 1-32 letters, digits, '-' or '_'."));
        _appended.Should().BeEmpty();
    }

    [Fact]
    public async Task RestoreSlotAsync_ShouldRestoreThenLook_AndAppendEventWithOutput()
    {
        var order = new List<string>();
        _store.Setup(s => s.RestoreSlot("alley")).Callback(() => order.Add("restore"));
        _engine.Setup(e => e.RunTurnAsync("look", It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("look")).ReturnsAsync(new IfTurnResult("The alley.", IfTurnStatus.Saved));
        var session = Create();

        var result = await session.RestoreSlotAsync(IfActors.Model, "alley", default);

        result.Should().Be(new IfActionResult(true, "The alley."));
        order.Should().Equal("restore", "look");
        _appended.Should().Equal(new IfTranscriptEvent(Now, IfActors.Model, IfEventKinds.Restore, "alley", "The alley."));
    }

    [Fact]
    public async Task RestoreSlotAsync_ShouldRecordTimeoutError_WhenLookTimesOut()
    {
        EngineReturns("The interpreter timed out.", IfTurnStatus.TimedOut);
        var session = Create();

        var result = await session.RestoreSlotAsync(IfActors.Model, "alley", default);

        result.Should().Be(new IfActionResult(false, "The interpreter timed out."));
        _appended.Should().Equal(new IfTranscriptEvent(Now, IfActors.System, IfEventKinds.Error, "The interpreter timed out."));
    }

    [Fact]
    public async Task RestoreSlotAsync_ShouldRecordFailureOutput_WhenLookFails()
    {
        EngineReturns("The interpreter failed (exit code 2).", IfTurnStatus.Failed);
        var session = Create();

        var result = await session.RestoreSlotAsync(IfActors.Human, "alley", default);

        result.Should().Be(new IfActionResult(false, "The interpreter failed (exit code 2)."));
        _appended.Should().Equal(new IfTranscriptEvent(Now, IfActors.System, IfEventKinds.Error, "The interpreter failed (exit code 2)."));
    }

    [Fact]
    public async Task RestoreSlotAsync_ShouldSucceedWithNotSavedNote_WhenLookNotSaved()
    {
        EngineReturns("The alley.", IfTurnStatus.NotSaved);
        var session = Create();

        var result = await session.RestoreSlotAsync(IfActors.Model, "alley", default);

        const string expected = "The alley.\n[not saved: the game may have ended]";
        result.Should().Be(new IfActionResult(true, expected));
        _appended.Should().Equal(new IfTranscriptEvent(Now, IfActors.Model, IfEventKinds.Restore, "alley", expected));
    }

    [Fact]
    public async Task RestoreSlotAsync_ShouldReturnError_WithoutLookOrEvent_WhenSlotMissing()
    {
        _store.Setup(s => s.RestoreSlot("ghost")).Throws(new IfStoreException("No slot named 'ghost'."));
        var session = Create();

        var result = await session.RestoreSlotAsync(IfActors.Human, "ghost", default);

        result.Should().Be(new IfActionResult(false, "No slot named 'ghost'."));
        _engine.Verify(e => e.RunTurnAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _appended.Should().BeEmpty();
    }

    [Fact]
    public void ListSlots_ShouldDelegateToStore()
    {
        _store.Setup(s => s.ListSlots()).Returns(["a", "b"]);

        Create().ListSlots().Should().Equal("a", "b");
    }

    [Fact]
    public void AddNote_ShouldAppendNoteEvent()
    {
        var session = Create();

        var result = session.AddNote(IfActors.Human, "try the cellar");

        result.Should().Be(new IfActionResult(true, "try the cellar"));
        _appended.Should().Equal(new IfTranscriptEvent(Now, IfActors.Human, IfEventKinds.Note, "try the cellar"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  \t")]
    public void AddNote_ShouldRejectEmpty(string text)
    {
        var session = Create();

        session.AddNote(IfActors.Human, text).Should().Be(new IfActionResult(false, "Note cannot be empty."));
        _appended.Should().BeEmpty();
    }

    [Fact]
    public void ReadNotes_ShouldDelegateToStore()
    {
        _store.Setup(s => s.ReadNotes()).Returns("map");

        Create().ReadNotes().Should().Be("map");
    }

    [Fact]
    public void UpdateNotes_ShouldWriteNotes_WithoutAppendingEvents()
    {
        var session = Create();

        session.UpdateNotes("new notes").Should().Be(new IfActionResult(true, "Notes updated."));

        _store.Verify(s => s.WriteNotes("new notes"), Times.Once);
        _appended.Should().BeEmpty();
    }

    [Fact]
    public void UpdateNotes_ShouldReturnError_WhenStoreRejects()
    {
        _store.Setup(s => s.WriteNotes(It.IsAny<string>())).Throws(new IfStoreException("Notes are longer than 8000 characters."));

        Create().UpdateNotes("x").Should().Be(new IfActionResult(false, "Notes are longer than 8000 characters."));
        _appended.Should().BeEmpty();
    }

    [Fact]
    public void RecordMethods_ShouldAppendExpectedEvents()
    {
        var session = Create();

        session.RecordThought("pondering");
        session.RecordFinish("all done");
        session.RecordError("boom");

        _appended.Should().Equal(
            new IfTranscriptEvent(Now, IfActors.Model, IfEventKinds.Thought, "pondering"),
            new IfTranscriptEvent(Now, IfActors.Model, IfEventKinds.Finish, "all done"),
            new IfTranscriptEvent(Now, IfActors.System, IfEventKinds.Error, "boom"));
        session.Events.Should().Equal(_appended);
    }

    [Fact]
    public void Constructor_ShouldDefaultClockToUtcNow()
    {
        var session = new IfSession(_engine.Object, _store.Object, "g");

        session.RecordError("x");

        _appended.Single().Ts.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }
}
