using FluentAssertions;
using Pixelbadger.Toolkit.CommandLine;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Services;
using Spectre.Console;

namespace Pixelbadger.Toolkit.Tests;

public class IfComponentTests
{
    private const int W = 80;
    private const int H = 24;

    private sealed class FakeSession : IIfSession
    {
        public string GameName { get; set; } = "anchor";
        public int TurnCount { get; set; }
        public List<IfTranscriptEvent> EventList { get; } = [];
        public IReadOnlyList<IfTranscriptEvent> Events => EventList.ToList();
        public event Action? Changed;
        public List<string> Calls { get; } = [];
        public List<string> Slots { get; } = [];
        public IfActionResult NextResult { get; set; } = new(true, "ok");
        public CancellationToken LastToken { get; private set; }
        public Exception? PlayException { get; set; }

        public void Raise() => Changed?.Invoke();
        public void Add(string actor, string kind, string text, string? output = null) =>
            EventList.Add(new IfTranscriptEvent(DateTimeOffset.UnixEpoch, actor, kind, text, output));

        public Task<IfActionResult> StartAsync(CancellationToken cancellationToken) => Task.FromResult(new IfActionResult(true, ""));

        public Task<IfActionResult> PlayAsync(string actor, string command, string? thought, CancellationToken cancellationToken)
        {
            LastToken = cancellationToken;
            Calls.Add($"play:{actor}:{command}:{thought ?? "<null>"}");
            if (PlayException is not null) throw PlayException;
            return Task.FromResult(NextResult);
        }

        public Task<IfActionResult> SaveSlotAsync(string actor, string name, CancellationToken cancellationToken)
        {
            Calls.Add($"save:{actor}:{name}");
            return Task.FromResult(NextResult);
        }

        public Task<IfActionResult> RestoreSlotAsync(string actor, string name, CancellationToken cancellationToken)
        {
            Calls.Add($"restore:{actor}:{name}");
            return Task.FromResult(NextResult);
        }

        public IReadOnlyList<string> ListSlots() => Slots;

        public IfActionResult AddNote(string actor, string text)
        {
            Calls.Add($"note:{actor}:{text}");
            return NextResult;
        }

        public string ReadNotes() => "";
        public IfActionResult UpdateNotes(string notes) => new(true, "");
        public void RecordThought(string text) => Calls.Add("thought:" + text);
        public void RecordFinish(string summary) => Calls.Add("finish:" + summary);
        public void RecordError(string message) => Calls.Add("error:" + message);
    }

    private sealed class Step
    {
        public int Starts { get; private set; }
        public List<CancellationToken> Tokens { get; } = [];
        public List<TaskCompletionSource<IfStepResult>> Sources { get; } = [];
        public Exception? ThrowSynchronously { get; set; }
        public bool CompleteOnCancel { get; set; }

        public Task<IfStepResult> Invoke(CancellationToken token)
        {
            Starts++;
            Tokens.Add(token);
            if (ThrowSynchronously is not null) throw ThrowSynchronously;
            var tcs = new TaskCompletionSource<IfStepResult>();
            if (CompleteOnCancel) token.Register(() => tcs.TrySetCanceled());
            Sources.Add(tcs);
            return tcs.Task;
        }

        public TaskCompletionSource<IfStepResult> Last => Sources[^1];
    }

    private readonly FakeSession _session = new();
    private readonly Step _step = new();

    private IfComponent Create(int maxSteps = 3) => new(_session, _step.Invoke, maxSteps);

    // For Run tests: an in-flight step must end when cancelled, otherwise shutdown waits for it.
    private IfComponent CreateForRun(int maxSteps = 3)
    {
        _step.CompleteOnCancel = true;
        return Create(maxSteps);
    }

    private static IfStepResult Ok() => new(false, null, 1);

    private static ConsoleKeyInfo Key(ConsoleKey key, char ch = '\0', bool ctrl = false) => new(ch, key, false, false, ctrl);
    private static ConsoleKeyInfo Chr(char c) => new(c, ConsoleKey.A, false, false, false);
    private static ConsoleKeyInfo Enter => Key(ConsoleKey.Enter, '\r');
    private static ConsoleKeyInfo Esc => Key(ConsoleKey.Escape, '\u001b');
    private static ConsoleKeyInfo CtrlC => Key(ConsoleKey.C, '\u0003', true);

    private static async Task TypeAsync(IfComponent c, string text)
    {
        foreach (var ch in text) await c.HandleKeyAsync(Chr(ch));
    }

    private static async Task SubmitAsync(IfComponent c, string text)
    {
        await TypeAsync(c, text);
        await c.HandleKeyAsync(Enter);
    }

    private static async Task<IfComponent> HaltedAsync(IfComponent c)
    {
        await c.HandleKeyAsync(Esc);
        return c;
    }

    private static string[] RenderLines(IfComponent c, int width = W, int height = H)
    {
        var writer = new StringWriter();
        var console = ToolkitConsole.CreateCaptureConsole(writer);
        console.Profile.Width = width;
        console.Write(c.Render(width, height));
        return writer.ToString().Replace("\r", "").Split('\n');
    }

    private static string Text(string[] lines) => string.Join("\n", lines.Select(l => l.TrimEnd()));

    // ---------------------------------------------------------------- initial state

    [Fact]
    public void Constructor_ShouldStartRunning_WhenTranscriptIsEmpty()
    {
        var c = Create();

        c.State.Should().Be(IfRunState.Running);
        c.StatusMessage.Should().BeNull();
        c.StepsThisRun.Should().Be(0);
        c.Thinking.Should().BeFalse();
        c.InputLine.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_ShouldStartHaltedWithSummary_WhenLastEventIsFinish()
    {
        _session.Add("model", IfEventKinds.Command, "look", "A room.");
        _session.Add("model", IfEventKinds.Finish, "Won the game");

        var c = Create();

        c.State.Should().Be(IfRunState.Halted);
        c.StatusMessage.Should().Be("Finished: Won the game");
    }

    [Fact]
    public void Constructor_ShouldStartRunning_WhenFinishIsNotTheLastEvent()
    {
        _session.Add("model", IfEventKinds.Finish, "Done");
        _session.Add("human", IfEventKinds.Command, "look", "A room.");

        Create().State.Should().Be(IfRunState.Running);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenSessionIsNull()
    {
        var act = () => new IfComponent(null!, _step.Invoke, 3);
        act.Should().Throw<ArgumentNullException>();
    }

    // ---------------------------------------------------------------- step lifecycle

    [Fact]
    public void Tick_ShouldStartOneStepAndSetThinking_WhenRunning()
    {
        var c = Create();

        c.Tick();
        c.Tick();

        _step.Starts.Should().Be(1);
        c.Thinking.Should().BeTrue();
    }

    [Fact]
    public void Tick_ShouldCountStepAndStartNext_WhenStepSucceeds()
    {
        var c = Create();
        c.Tick();

        _step.Last.SetResult(Ok());
        c.Tick();

        c.StepsThisRun.Should().Be(1);
        _step.Starts.Should().Be(2);
        c.Thinking.Should().BeTrue();
        c.State.Should().Be(IfRunState.Running);
    }

    [Fact]
    public void Tick_ShouldHaltWithStepLimitMessage_WhenMaxStepsReached()
    {
        var c = Create(maxSteps: 2);
        c.Tick();
        _step.Last.SetResult(Ok());
        c.Tick();
        _step.Last.SetResult(Ok());
        c.Tick();

        c.State.Should().Be(IfRunState.Halted);
        c.StepsThisRun.Should().Be(2);
        c.Thinking.Should().BeFalse();
        c.StatusMessage.Should().Be("Step limit reached (2 steps). Press Enter to continue.");
        _step.Starts.Should().Be(2);
    }

    [Fact]
    public void Tick_ShouldHaltWithSummary_WhenStepFinishes()
    {
        var c = Create();
        c.Tick();

        _step.Last.SetResult(new IfStepResult(true, "All done", 2));
        c.Tick();

        c.State.Should().Be(IfRunState.Halted);
        c.StatusMessage.Should().Be("Finished: All done");
        c.StepsThisRun.Should().Be(1);
        _step.Starts.Should().Be(1);
    }

    [Fact]
    public void Tick_ShouldPreferFinish_WhenFinishCoincidesWithLastAllowedStep()
    {
        var c = Create(maxSteps: 1);
        c.Tick();

        _step.Last.SetResult(new IfStepResult(true, "Over", 1));
        c.Tick();

        c.StatusMessage.Should().Be("Finished: Over");
    }

    [Fact]
    public void Tick_ShouldRecordErrorAndHalt_WhenStepThrows()
    {
        var c = Create();
        c.Tick();

        _step.Last.SetException(new InvalidOperationException("401 unauthorized"));
        c.Tick();

        c.State.Should().Be(IfRunState.Halted);
        c.StatusMessage.Should().Be("Model error: 401 unauthorized");
        _session.Calls.Should().Equal("error:Model error: 401 unauthorized");
        c.StepsThisRun.Should().Be(0);
        c.Thinking.Should().BeFalse();
    }

    [Fact]
    public void Tick_ShouldRecordErrorAndHalt_WhenStepThrowsSynchronously()
    {
        _step.ThrowSynchronously = new InvalidOperationException("no api key");
        var c = Create();

        c.Tick();
        c.Tick();

        c.State.Should().Be(IfRunState.Halted);
        _session.Calls.Should().Equal("error:Model error: no api key");
        _step.Starts.Should().Be(1);
    }

    [Fact]
    public void Tick_ShouldRetryOnResume_WhenPreviousStepFailed()
    {
        var c = Create();
        c.Tick();
        _step.Last.SetException(new InvalidOperationException("boom"));
        c.Tick();

        c.HandleKeyAsync(Enter).GetAwaiter().GetResult();
        c.Tick();

        c.State.Should().Be(IfRunState.Running);
        _step.Starts.Should().Be(2);
        c.StatusMessage.Should().BeNull();
    }

    [Fact]
    public async Task EscapeWhileRunning_ShouldCancelTokenAndHalt()
    {
        var c = Create();
        c.Tick();

        await c.HandleKeyAsync(Esc);

        _step.Tokens[0].IsCancellationRequested.Should().BeTrue();
        c.State.Should().Be(IfRunState.Halted);
        c.StatusMessage.Should().Be("Halted. Type a command, !note, /save <name>, /restore <name>, /slots or /quit. Enter on an empty line resumes.");
    }

    [Fact]
    public async Task Tick_ShouldIgnoreCancelledStep_WhenHaltCancelledIt()
    {
        var c = Create();
        c.Tick();
        await c.HandleKeyAsync(Esc);

        _step.Last.SetCanceled();
        c.Tick();

        c.StepsThisRun.Should().Be(0);
        c.Thinking.Should().BeFalse();
        c.State.Should().Be(IfRunState.Halted);
        _session.Calls.Should().BeEmpty();
        _step.Starts.Should().Be(1);
    }

    [Fact]
    public async Task Tick_ShouldIgnoreOperationCanceledException_WhenHaltCancelledIt()
    {
        var c = Create();
        c.Tick();
        await c.HandleKeyAsync(Esc);

        _step.Last.SetException(new OperationCanceledException());
        c.Tick();

        _session.Calls.Should().BeEmpty();
        c.StepsThisRun.Should().Be(0);
    }

    [Fact]
    public void Tick_ShouldRecordError_WhenStepCancelledWithoutHalt()
    {
        var c = Create();
        c.Tick();

        _step.Last.SetException(new TaskCanceledException("The request was canceled due to the configured timeout."));
        c.Tick();

        c.State.Should().Be(IfRunState.Halted);
        _session.Calls.Should().ContainSingle().Which.Should().StartWith("error:Model error:");
    }

    [Fact]
    public async Task Tick_ShouldNotStartSecondStep_WhenResumedWhileCancelledStepStillInFlight()
    {
        var c = Create();
        c.Tick();
        await c.HandleKeyAsync(Esc);
        await c.HandleKeyAsync(Enter);

        c.Tick();
        _step.Starts.Should().Be(1);

        _step.Last.SetCanceled();
        c.Tick();
        _step.Starts.Should().Be(2);
        _step.Tokens[1].IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public async Task Tick_ShouldNotStartStep_WhenHalted()
    {
        var c = await HaltedAsync(Create());

        c.Tick();

        _step.Starts.Should().Be(0);
    }

    [Fact]
    public async Task EnterOnEmptyLine_ShouldResetStepCounter_WhenStepLimitReached()
    {
        var c = Create(maxSteps: 1);
        c.Tick();
        _step.Last.SetResult(Ok());
        c.Tick();
        c.State.Should().Be(IfRunState.Halted);

        await c.HandleKeyAsync(Enter);

        c.State.Should().Be(IfRunState.Running);
        c.StepsThisRun.Should().Be(0);
        c.StatusMessage.Should().BeNull();
        c.Tick();
        _step.Starts.Should().Be(2);
    }

    [Fact]
    public async Task EnterOnEmptyLine_ShouldKeepStepCounter_WhenBudgetRemains()
    {
        var c = Create(maxSteps: 5);
        c.Tick();
        _step.Last.SetResult(Ok());
        c.Tick();
        await c.HandleKeyAsync(Esc);
        _step.Last.SetCanceled();
        c.Tick();

        await c.HandleKeyAsync(Enter);

        c.StepsThisRun.Should().Be(1);
        c.State.Should().Be(IfRunState.Running);
    }

    [Fact]
    public async Task EnterOnWhitespaceOnlyLine_ShouldResume()
    {
        var c = await HaltedAsync(Create());

        await SubmitAsync(c, "   ");

        c.State.Should().Be(IfRunState.Running);
        _session.Calls.Should().BeEmpty();
    }

    [Fact]
    public void Run_ShouldRedraw_WhenSessionChanges()
    {
        var c = CreateForRun();
        var input = new ScriptedInput();
        input.OnPoll = n => { if (n == 1) _session.Raise(); if (n >= 4) input.Keys.Enqueue(CtrlC); };
        var writer = new StringWriter();
        var console = ToolkitConsole.CreateCaptureConsole(writer);

        c.Run(console, input, CancellationToken.None);

        // initial frame + redraw after the Changed event + redraw after the final key (cursor-home also in preamble)
        writer.ToString().Split("\u001b[H").Length.Should().BeGreaterThan(3);
    }

    // ---------------------------------------------------------------- keys

    [Fact]
    public async Task CtrlC_ShouldQuitAndCancelStep_WhenRunning()
    {
        var c = Create();
        c.Tick();

        bool keepGoing = await c.HandleKeyAsync(CtrlC);

        keepGoing.Should().BeFalse();
        _step.Tokens[0].IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task CtrlC_ShouldQuit_WhenHalted()
    {
        var c = await HaltedAsync(Create());

        (await c.HandleKeyAsync(CtrlC)).Should().BeFalse();
    }

    [Fact]
    public async Task CtrlC_ShouldQuit_WhenReportedOnlyAsControlCharacter()
    {
        var c = Create();

        (await c.HandleKeyAsync(new ConsoleKeyInfo('\u0003', ConsoleKey.NoName, false, false, false))).Should().BeFalse();
    }

    [Fact]
    public async Task Typing_ShouldBeIgnored_WhenRunning()
    {
        var c = Create();

        await TypeAsync(c, "look");

        c.InputLine.Should().BeEmpty();
        (await c.HandleKeyAsync(Chr('x'))).Should().BeTrue();
    }

    [Fact]
    public async Task Typing_ShouldAppendToInputLine_WhenHalted()
    {
        var c = await HaltedAsync(Create());

        await TypeAsync(c, "open door");

        c.InputLine.Should().Be("open door");
    }

    [Fact]
    public async Task Typing_ShouldCapInputAtTwoHundredCharacters()
    {
        var c = await HaltedAsync(Create());

        await TypeAsync(c, new string('a', 250));

        c.InputLine.Should().HaveLength(200);
    }

    [Fact]
    public async Task Typing_ShouldIgnoreControlCharacters()
    {
        var c = await HaltedAsync(Create());

        await c.HandleKeyAsync(new ConsoleKeyInfo('\t', ConsoleKey.Tab, false, false, false));
        await c.HandleKeyAsync(new ConsoleKeyInfo('a', ConsoleKey.A, false, false, true));

        c.InputLine.Should().BeEmpty();
    }

    [Fact]
    public async Task Backspace_ShouldDeleteLastCharacter_WhenHalted()
    {
        var c = await HaltedAsync(Create());
        await TypeAsync(c, "abc");

        await c.HandleKeyAsync(Key(ConsoleKey.Backspace, '\b'));

        c.InputLine.Should().Be("ab");
    }

    [Fact]
    public async Task Backspace_ShouldDoNothing_WhenInputEmpty()
    {
        var c = await HaltedAsync(Create());

        await c.HandleKeyAsync(Key(ConsoleKey.Backspace, '\b'));

        c.InputLine.Should().BeEmpty();
    }

    [Fact]
    public async Task Escape_ShouldClearInput_WhenHalted()
    {
        var c = await HaltedAsync(Create());
        await TypeAsync(c, "abc");

        await c.HandleKeyAsync(Esc);

        c.InputLine.Should().BeEmpty();
        c.State.Should().Be(IfRunState.Halted);
    }

    [Fact]
    public async Task Enter_ShouldPlayTrimmedLineAsHuman_WhenPlainText()
    {
        var c = await HaltedAsync(Create());

        await SubmitAsync(c, "  take lamp  ");

        _session.Calls.Should().Equal("play:human:take lamp:<null>");
        c.InputLine.Should().BeEmpty();
        c.StatusMessage.Should().BeNull();
        c.State.Should().Be(IfRunState.Halted);
    }

    [Fact]
    public async Task Enter_ShouldShowErrorText_WhenCommandRejected()
    {
        _session.NextResult = new IfActionResult(false, "Use /save, /restore or /quit.");
        var c = await HaltedAsync(Create());

        await SubmitAsync(c, "save");

        c.StatusMessage.Should().Be("Use /save, /restore or /quit.");
        c.InputLine.Should().BeEmpty();
    }

    [Fact]
    public async Task Enter_ShouldClearEarlierStatus_WhenCommandSucceeds()
    {
        _session.NextResult = new IfActionResult(false, "bad");
        var c = await HaltedAsync(Create());
        await SubmitAsync(c, "x");
        _session.NextResult = new IfActionResult(true, "A room.");

        await SubmitAsync(c, "look");

        c.StatusMessage.Should().BeNull();
    }

    [Fact]
    public async Task Enter_ShouldPassRunCancellationToken_WhenPlaying()
    {
        var c = await HaltedAsync(Create());

        await SubmitAsync(c, "look");

        _session.LastToken.Should().Be(CancellationToken.None);
    }

    [Fact]
    public async Task Enter_ShouldAddHumanNote_WhenLineStartsWithBang()
    {
        var c = await HaltedAsync(Create());

        await SubmitAsync(c, "!  try the cellar ");

        _session.Calls.Should().Equal("note:human:try the cellar");
        c.StatusMessage.Should().BeNull();
    }

    [Fact]
    public async Task Enter_ShouldShowError_WhenNoteEmpty()
    {
        _session.NextResult = new IfActionResult(false, "Note cannot be empty.");
        var c = await HaltedAsync(Create());

        await SubmitAsync(c, "!");

        _session.Calls.Should().Equal("note:human:");
        c.StatusMessage.Should().Be("Note cannot be empty.");
    }

    [Fact]
    public async Task Enter_ShouldSaveSlot_WhenSaveCommand()
    {
        var c = await HaltedAsync(Create());

        await SubmitAsync(c, "/save alley");

        _session.Calls.Should().Equal("save:human:alley");
        c.StatusMessage.Should().BeNull();
    }

    [Fact]
    public async Task Enter_ShouldShowStoreError_WhenSaveFails()
    {
        _session.NextResult = new IfActionResult(false, "Invalid slot name.");
        var c = await HaltedAsync(Create());

        await SubmitAsync(c, "/save bad name!");

        _session.Calls.Should().Equal("save:human:bad name!");
        c.StatusMessage.Should().Be("Invalid slot name.");
    }

    [Fact]
    public async Task Enter_ShouldSaveWithEmptyName_WhenNoArgumentGiven()
    {
        var c = await HaltedAsync(Create());

        await SubmitAsync(c, "/save");

        _session.Calls.Should().Equal("save:human:");
    }

    [Fact]
    public async Task Enter_ShouldRestoreSlot_WhenRestoreCommand()
    {
        var c = await HaltedAsync(Create());

        await SubmitAsync(c, "/RESTORE alley");

        _session.Calls.Should().Equal("restore:human:alley");
    }

    [Fact]
    public async Task Enter_ShouldListSlots_WhenSlotsCommand()
    {
        _session.Slots.AddRange(["a", "b"]);
        var c = await HaltedAsync(Create());

        await SubmitAsync(c, "/slots");

        c.StatusMessage.Should().Be("Slots: a, b");
    }

    [Fact]
    public async Task Enter_ShouldReportNoSlots_WhenNoneSaved()
    {
        var c = await HaltedAsync(Create());

        await SubmitAsync(c, "/slots");

        c.StatusMessage.Should().Be("No slots saved.");
    }

    [Fact]
    public async Task Enter_ShouldQuit_WhenQuitCommand()
    {
        var c = await HaltedAsync(Create());
        await TypeAsync(c, "/quit");

        (await c.HandleKeyAsync(Enter)).Should().BeFalse();
        _session.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData("/help")]
    [InlineData("/")]
    [InlineData("/quitnow")]
    public async Task Enter_ShouldShowUnknownCommandMessage_WhenSlashCommandUnknown(string line)
    {
        var c = await HaltedAsync(Create());

        await SubmitAsync(c, line);

        c.StatusMessage.Should().Be("Unknown command. Try /save, /restore, /slots, /quit.");
        _session.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Enter_ShouldBeIgnored_WhenRunning()
    {
        var c = Create();

        (await c.HandleKeyAsync(Enter)).Should().BeTrue();

        _session.Calls.Should().BeEmpty();
        c.State.Should().Be(IfRunState.Running);
    }

    // ---------------------------------------------------------------- scrolling

    private void AddOutputs(int count)
    {
        for (int i = 1; i <= count; i++) _session.Add("model", IfEventKinds.Command, $"cmd{i}", $"out{i}");
    }

    [Fact]
    public async Task PageUp_ShouldShowOlderLines_WhenTranscriptLongerThanPane()
    {
        AddOutputs(30);
        var c = await HaltedAsync(Create());
        Text(RenderLines(c)).Should().Contain("out30");

        await c.HandleKeyAsync(Key(ConsoleKey.PageUp));
        await c.HandleKeyAsync(Key(ConsoleKey.PageUp));
        await c.HandleKeyAsync(Key(ConsoleKey.PageUp));
        var text = Text(RenderLines(c));

        text.Should().NotContain("out30");
        text.Should().Contain(">cmd");
    }

    [Fact]
    public async Task PageUp_ShouldScrollByPaneHeightMinusOne()
    {
        AddOutputs(30);
        var c = Create();
        RenderLines(c); // learn the height (pane = 21 rows)
        var before = RenderLines(c)[1..^2];

        await c.HandleKeyAsync(Key(ConsoleKey.PageUp));
        var after = RenderLines(c)[1..^2];

        // bottom line of the new view is the 21st-from-bottom... shifted by 20 lines
        after[20].Should().Be(before[0]);
    }

    [Fact]
    public async Task PageDown_ShouldNotScrollBelowBottom()
    {
        AddOutputs(30);
        var c = Create();
        var bottom = Text(RenderLines(c));

        await c.HandleKeyAsync(Key(ConsoleKey.PageDown));

        Text(RenderLines(c)).Should().Be(bottom);
    }

    [Fact]
    public async Task PageUp_ShouldClampAtTopOfTranscript()
    {
        AddOutputs(30);
        var c = Create();
        for (int i = 0; i < 20; i++) await c.HandleKeyAsync(Key(ConsoleKey.PageUp));
        var atTop = RenderLines(c);

        await c.HandleKeyAsync(Key(ConsoleKey.PageDown));
        var oneDown = RenderLines(c);

        atTop[1].Should().StartWith(">cmd1");
        oneDown[1].Should().NotStartWith(">cmd1");
    }

    [Fact]
    public async Task End_ShouldJumpToBottom_WhenScrolledUp()
    {
        AddOutputs(30);
        var c = Create();
        var bottom = Text(RenderLines(c));
        await c.HandleKeyAsync(Key(ConsoleKey.PageUp));
        await c.HandleKeyAsync(Key(ConsoleKey.PageUp));

        await c.HandleKeyAsync(Key(ConsoleKey.End));

        Text(RenderLines(c)).Should().Be(bottom);
    }

    [Fact]
    public async Task Scrolling_ShouldWorkWhileHalted()
    {
        AddOutputs(30);
        var c = await HaltedAsync(Create());
        RenderLines(c);

        await c.HandleKeyAsync(Key(ConsoleKey.PageUp));

        Text(RenderLines(c)).Should().NotContain("out30");
    }

    [Fact]
    public async Task Scroll_ShouldStayInPlace_WhenNewEventArrivesWhileScrolled()
    {
        AddOutputs(30);
        var c = Create();
        RenderLines(c);
        await c.HandleKeyAsync(Key(ConsoleKey.PageUp));
        _session.Add("model", IfEventKinds.Command, "cmdNew", "outNew");
        _session.Raise();

        var text = Text(RenderLines(c));

        text.Should().NotContain("outNew");
    }

    // ---------------------------------------------------------------- rendering

    [Theory]
    [InlineData(60, 16)]
    [InlineData(80, 24)]
    [InlineData(120, 40)]
    [InlineData(61, 17)]
    public void Render_ShouldProduceExactlyHeightLinesNoWiderThanWidth(int width, int height)
    {
        AddOutputs(40);
        _session.Add("model", IfEventKinds.Thought, new string('w', 300));
        var c = Create();
        c.Tick();

        var lines = RenderLines(c, width, height);

        lines.Should().HaveCount(height);
        lines.Should().OnlyContain(l => l.Length <= width);
    }

    [Fact]
    public void Render_ShouldShowGameNameAndRunningStatusInBar()
    {
        _session.GameName = "anchor";
        _session.TurnCount = 7;
        var c = Create(maxSteps: 30);

        var bar = RenderLines(c)[0];

        bar.Should().StartWith(" anchor");
        bar.Should().EndWith("Running · step 0/30 · turns 7 ");
        bar.Should().HaveLength(W);
    }

    [Fact]
    public async Task Render_ShouldShowHaltedInBar_WhenHalted()
    {
        var c = await HaltedAsync(Create());

        RenderLines(c)[0].Should().EndWith("Halted · step 0/3 · turns 0 ");
    }

    [Fact]
    public void Render_ShouldShowThinkingInBar_WhenStepInFlight()
    {
        var c = Create();
        c.Tick();

        RenderLines(c)[0].Should().EndWith("Thinking… · step 0/3 · turns 0 ");
    }

    [Fact]
    public async Task Render_ShouldShowThinkingInBar_WhenHaltedWhileStepStillInFlight()
    {
        var c = Create();
        c.Tick();
        await c.HandleKeyAsync(Esc);

        RenderLines(c)[0].Should().Contain("Thinking…");
    }

    [Fact]
    public void Render_ShouldCountStepsInBar()
    {
        var c = Create(maxSteps: 10);
        c.Tick();
        _step.Last.SetResult(Ok());
        c.Tick();

        RenderLines(c)[0].Should().Contain("step 1/10");
    }

    [Fact]
    public void Render_ShouldTruncateGameName_WhenBarTooNarrow()
    {
        _session.GameName = new string('g', 200);
        var c = Create();

        var bar = RenderLines(c)[0];

        bar.Should().HaveLength(W);
        bar.Should().EndWith("Running · step 0/3 · turns 0 ");
    }

    [Fact]
    public void Render_ShouldShowTooSmallMessage_WhenTerminalBelowMinimum()
    {
        var c = Create();

        var narrow = RenderLines(c, 59, 20);
        var short_ = RenderLines(c, 80, 15);

        narrow.Should().HaveCount(20);
        Text(narrow).Should().Contain("Terminal too small (need 60x16)");
        short_.Should().HaveCount(15);
        Text(short_).Should().Contain("Terminal too small (need 60x16)");
    }

    [Fact]
    public void Render_ShouldCentreTooSmallMessage()
    {
        var c = Create();

        var lines = RenderLines(c, 59, 20);

        lines[9].Should().Be(new string(' ', (59 - 31) / 2) + "Terminal too small (need 60x16)");
    }

    [Fact]
    public void Render_ShouldFormatEachEventKind()
    {
        _session.Add("system", IfEventKinds.Command, "(new game)", "Welcome to the game.");
        _session.Add("model", IfEventKinds.Thought, "I feel uneasy.");
        _session.Add("model", IfEventKinds.Command, "look", "A dark alley.");
        _session.Add("human", IfEventKinds.Command, "north", "A door.");
        _session.Add("human", IfEventKinds.Note, "try the bin");
        _session.Add("model", IfEventKinds.Save, "alley");
        _session.Add("human", IfEventKinds.Restore, "alley", "Back in the alley.");
        _session.Add("system", IfEventKinds.Error, "Model error: boom");
        _session.Add("model", IfEventKinds.Finish, "Gave up");
        var c = Create();

        var lines = RenderLines(c, 80, 40).Select(l => l.TrimEnd()).ToList();
        var body = lines.Skip(1).Take(37).Where(l => l.Length > 0).ToList();

        body.Should().Equal(
            "Welcome to the game.",
            "~ I feel uneasy.",
            ">look",
            "A dark alley.",
            ">north (you)",
            "A door.",
            "! try the bin",
            "[saved slot alley]",
            "[restored slot alley]",
            "Back in the alley.",
            "[error] Model error: boom",
            "[finished] Gave up");
    }

    [Fact]
    public void Render_ShouldSeparateEventsWithOneBlankLine()
    {
        _session.Add("model", IfEventKinds.Command, "look", "A dark alley.");
        _session.Add("model", IfEventKinds.Thought, "Hmm.");
        var c = Create();

        var lines = RenderLines(c).Select(l => l.TrimEnd()).ToArray();
        int i = Array.IndexOf(lines, ">look");

        lines[i + 1].Should().Be("A dark alley.");
        lines[i + 2].Should().BeEmpty();
        lines[i + 3].Should().Be("~ Hmm.");
    }

    [Fact]
    public void Render_ShouldBottomAlignTranscript()
    {
        _session.Add("model", IfEventKinds.Command, "look", "A dark alley.");
        var c = Create();

        var lines = RenderLines(c);

        // rows 1..H-3 are the pane; the last pane row holds the newest line
        lines[H - 3].TrimEnd().Should().Be("A dark alley.");
        lines[H - 4].TrimEnd().Should().Be(">look");
        lines[1].Trim().Should().BeEmpty();
    }

    [Fact]
    public void Render_ShouldNotDuplicateBlankLinesForEventsWithoutText()
    {
        _session.Add("system", IfEventKinds.Command, "(new game)", null);
        _session.Add("model", IfEventKinds.Thought, "Hmm.");
        var c = Create();

        var lines = RenderLines(c);

        lines[H - 3].TrimEnd().Should().Be("~ Hmm.");
        lines[H - 4].Trim().Should().BeEmpty();
        lines[H - 5].Trim().Should().BeEmpty();
    }

    [Fact]
    public void Render_ShouldWrapLongLinesAtWordBoundaries()
    {
        var words = string.Join(" ", Enumerable.Range(1, 40).Select(i => $"word{i:00}"));
        _session.Add("model", IfEventKinds.Command, "look", words);
        var c = Create();

        var lines = RenderLines(c).Select(l => l.TrimEnd()).ToArray();
        var wrapped = lines.Where(l => l.Contains("word")).ToArray();

        wrapped.Should().HaveCountGreaterThan(1);
        wrapped.Should().OnlyContain(l => l.Length <= W);
        string.Join(" ", wrapped).Should().Be(words);
    }

    [Fact]
    public void Render_ShouldHardSplitWords_WhenLongerThanWidth()
    {
        _session.Add("model", IfEventKinds.Command, "look", new string('x', 200));
        var c = Create();

        var lines = RenderLines(c).Select(l => l.TrimEnd()).Where(l => l.StartsWith('x')).ToArray();

        lines.Should().HaveCount(3);
        string.Concat(lines).Should().HaveLength(200);
    }

    [Fact]
    public void Render_ShouldPreserveBlankLinesAndIndentationInGameOutput()
    {
        _session.Add("model", IfEventKinds.Command, "look", "Line one.\n\n   indented");
        var c = Create();

        var lines = RenderLines(c).Select(l => l.TrimEnd()).ToArray();
        int i = Array.IndexOf(lines, "Line one.");

        lines[i + 1].Should().BeEmpty();
        lines[i + 2].Should().Be("   indented");
    }

    [Fact]
    public void Render_ShouldEscapeMarkupCharactersInGameText()
    {
        _session.Add("model", IfEventKinds.Command, "x [red]", "A [bold]sign[/] says [[hi]].");
        _session.Add("model", IfEventKinds.Thought, "[blue]why[/]");
        _session.GameName = "[x]game";
        var c = Create();

        var text = Text(RenderLines(c));

        text.Should().Contain(">x [red]");
        text.Should().Contain("A [bold]sign[/] says [[hi]].");
        text.Should().Contain("~ [blue]why[/]");
        text.Should().Contain(" [x]game");
    }

    [Fact]
    public async Task Render_ShouldEscapeMarkupCharactersInStatusAndInput()
    {
        var c = await HaltedAsync(Create());
        await TypeAsync(c, "[red]");
        _session.NextResult = new IfActionResult(false, "bad [/] thing");
        await SubmitAsync(c, "[/x]");

        var lines = RenderLines(c);

        lines[H - 2].TrimEnd().Should().Be("bad [/] thing");
    }

    [Fact]
    public void Render_ShouldShowRunningHintOnBottomRow_WhenRunning()
    {
        var c = Create();

        RenderLines(c)[H - 1].TrimEnd().Should().Be("  (Esc to halt · PgUp/PgDn to scroll · Ctrl+C to quit)");
    }

    [Fact]
    public async Task Render_ShouldShowPromptAndCursorOnBottomRow_WhenHalted()
    {
        var c = await HaltedAsync(Create());
        await TypeAsync(c, "open box");

        RenderLines(c)[H - 1].TrimEnd().Should().Be("> open box█");
    }

    [Fact]
    public async Task Render_ShouldShowTailOfLongInput_WhenInputWiderThanScreen()
    {
        var c = await HaltedAsync(Create());
        await TypeAsync(c, new string('a', 100) + "END");

        var row = RenderLines(c)[H - 1];

        row.Should().HaveLength(W);
        row.Should().EndWith("END█");
    }

    [Fact]
    public async Task Render_ShouldShowStatusMessageAboveInput_WhenSet()
    {
        var c = await HaltedAsync(Create());
        await SubmitAsync(c, "/slots");

        RenderLines(c)[H - 2].TrimEnd().Should().Be("No slots saved.");
    }

    [Fact]
    public void Render_ShouldTruncateStatusMessage_WhenWiderThanScreen()
    {
        _session.Add("model", IfEventKinds.Finish, new string('s', 200));
        var c = Create();

        var row = RenderLines(c)[H - 2];

        row.Should().HaveLength(W);
        row.Should().StartWith("Finished: sss");
    }

    [Fact]
    public void Render_ShouldFlattenMultilineStatusMessage()
    {
        _session.Add("model", IfEventKinds.Finish, "one\ntwo");
        var c = Create();

        RenderLines(c)[H - 2].TrimEnd().Should().Be("Finished: one two");
    }

    [Fact]
    public void Render_ShouldPickUpNewEvents_WhenTranscriptGrows()
    {
        _session.Add("model", IfEventKinds.Command, "look", "First.");
        var c = Create();
        Text(RenderLines(c)).Should().Contain("First.");

        _session.Add("model", IfEventKinds.Command, "wait", "Second.");

        Text(RenderLines(c)).Should().Contain("Second.");
    }

    [Fact]
    public void Render_ShouldRewrap_WhenWidthChanges()
    {
        _session.Add("model", IfEventKinds.Command, "look", string.Join(" ", Enumerable.Repeat("abcdefghi", 12)));
        var c = Create();

        var wide = RenderLines(c, 120, 24).Count(l => l.Contains("abcdefghi"));
        var narrow = RenderLines(c, 60, 24).Count(l => l.Contains("abcdefghi"));

        narrow.Should().BeGreaterThan(wide);
    }

    // ---------------------------------------------------------------- Run loop

    private sealed class ScriptedInput : ITerminalInput
    {
        public Queue<ConsoleKeyInfo> Keys { get; } = new();
        public Action<int>? OnPoll { get; set; }
        public (int, int) SizeValue { get; set; } = (100, 30);
        private int _polls;

        public bool KeyAvailable
        {
            get
            {
                OnPoll?.Invoke(++_polls);
                return Keys.Count > 0;
            }
        }

        public ConsoleKeyInfo ReadKey() => Keys.Dequeue();

        public (int Width, int Height) Size => SizeValue;
    }

    private const string EnterScreen = "\u001b[?1049h";
    private const string LeaveScreen = "\u001b[?1049l";

    [Fact]
    public void Run_ShouldReturnZeroAndRestoreTerminal_WhenQuitKeyReceived()
    {
        var writer = new StringWriter();
        var console = ToolkitConsole.CreateCaptureConsole(writer);
        var input = new ScriptedInput();
        input.Keys.Enqueue(CtrlC);

        var code = CreateForRun().Run(console, input, CancellationToken.None);

        code.Should().Be(0);
        var text = writer.ToString();
        text.Should().StartWith(EnterScreen + "\u001b[?25l");
        text.Should().EndWith("\u001b[0m\u001b[?25h" + LeaveScreen);
        text.Should().Contain("anchor");
    }

    [Fact]
    public void Run_ShouldStartStepImmediatelyAndCancelItOnQuit()
    {
        var console = ToolkitConsole.CreateCaptureConsole(new StringWriter());
        var input = new ScriptedInput();
        input.OnPoll = n => { if (n == 2) input.Keys.Enqueue(CtrlC); };
        // the step honours cancellation and finishes once cancelled
        var c = new IfComponent(_session, async token =>
        {
            await Task.Delay(Timeout.Infinite, token).ContinueWith(_ => { });
            return Ok();
        }, 3);

        c.Run(console, input, CancellationToken.None).Should().Be(0);

        c.Thinking.Should().BeTrue();
    }

    [Fact]
    public void Run_ShouldCancelStepAndWaitForIt_WhenQuitting()
    {
        var console = ToolkitConsole.CreateCaptureConsole(new StringWriter());
        var input = new ScriptedInput();
        input.OnPoll = n => { if (n == 2) input.Keys.Enqueue(CtrlC); };
        bool observedCancel = false;
        bool finished = false;
        var c = new IfComponent(_session, async token =>
        {
            try { await Task.Delay(Timeout.Infinite, token); }
            catch (OperationCanceledException) { observedCancel = true; }
            await Task.Delay(50);
            finished = true;
            return Ok();
        }, 3);

        c.Run(console, input, CancellationToken.None).Should().Be(0);

        observedCancel.Should().BeTrue();
        finished.Should().BeTrue();
    }

    [Fact]
    public void Run_ShouldRunStepsAndHaltAtLimit_ThenQuit()
    {
        var writer = new StringWriter();
        var console = ToolkitConsole.CreateCaptureConsole(writer);
        var input = new ScriptedInput();
        int steps = 0;
        var c = new IfComponent(_session, _ => { steps++; return Task.FromResult(Ok()); }, 2);
        int haltedPolls = 0;
        // the halted frame is drawn on the poll after the halt; quit one poll later
        input.OnPoll = _ => { if (c.State == IfRunState.Halted && ++haltedPolls == 3) input.Keys.Enqueue(CtrlC); };

        c.Run(console, input, CancellationToken.None).Should().Be(0);

        steps.Should().Be(2);
        c.State.Should().Be(IfRunState.Halted);
        writer.ToString().Should().Contain("Step limit reached (2 steps). Press Enter to continue.");
    }

    [Fact]
    public void Run_ShouldHandleHumanCommandAndRedraw_WhenHalted()
    {
        var writer = new StringWriter();
        var console = ToolkitConsole.CreateCaptureConsole(writer);
        var input = new ScriptedInput();
        foreach (var k in new[] { Esc, Chr('l'), Chr('o'), Chr('o'), Chr('k'), Enter, CtrlC }) input.Keys.Enqueue(k);

        CreateForRun().Run(console, input, CancellationToken.None).Should().Be(0);

        _session.Calls.Should().Equal("play:human:look:<null>");
    }

    [Fact]
    public void Run_ShouldRedrawAtNewSize_WhenSizeChangesWhileIdle()
    {
        var writer = new StringWriter();
        var console = ToolkitConsole.CreateCaptureConsole(writer);
        var input = new ScriptedInput();
        input.OnPoll = n =>
        {
            if (n == 2) input.SizeValue = (90, 26);
            if (n == 4) input.Keys.Enqueue(CtrlC);
        };

        CreateForRun().Run(console, input, CancellationToken.None).Should().Be(0);

        writer.ToString().Should().Contain("\u001b[2J");
    }

    [Fact]
    public void Run_ShouldReturnZeroAndRestoreTerminal_WhenTokenAlreadyCancelled()
    {
        var writer = new StringWriter();
        var console = ToolkitConsole.CreateCaptureConsole(writer);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        CreateForRun().Run(console, new ScriptedInput(), cts.Token).Should().Be(0);

        writer.ToString().Should().EndWith(LeaveScreen);
    }

    [Fact]
    public async Task HandleKeySafeAsync_ShouldShowErrorAndContinue_WhenSessionThrowsIOException()
    {
        _session.PlayException = new IOException("disk full");
        var c = await HaltedAsync(Create());
        await TypeAsync(c, "look");

        var keepGoing = await c.HandleKeySafeAsync(Enter);

        keepGoing.Should().BeTrue();
        c.StatusMessage.Should().Be("Error: disk full");
        c.State.Should().Be(IfRunState.Halted);
    }

    [Fact]
    public async Task HandleKeySafeAsync_ShouldRethrowCancellation()
    {
        _session.PlayException = new OperationCanceledException();
        var c = await HaltedAsync(Create());
        await TypeAsync(c, "look");

        var act = () => c.HandleKeySafeAsync(Enter);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void Run_ShouldKeepTuiOpen_WhenSessionThrowsIOException()
    {
        var writer = new StringWriter();
        var console = ToolkitConsole.CreateCaptureConsole(writer);
        _session.PlayException = new IOException("disk full");
        var input = new ScriptedInput();
        foreach (var k in new[] { Esc, Chr('l'), Enter }) input.Keys.Enqueue(k);
        int polls = 0;
        input.OnPoll = _ => { if (++polls == 6) input.Keys.Enqueue(CtrlC); };
        var c = CreateForRun();

        c.Run(console, input, CancellationToken.None).Should().Be(0);

        writer.ToString().Should().Contain("Error: disk full");
        writer.ToString().Should().EndWith(LeaveScreen);
    }

    [Fact]
    public void Run_ShouldQuitCleanly_WhenTokenCancelledDuringSessionCall()
    {
        var writer = new StringWriter();
        var console = ToolkitConsole.CreateCaptureConsole(writer);
        using var cts = new CancellationTokenSource();
        var session = new CancellingSession(cts);
        var input = new ScriptedInput();
        foreach (var k in new[] { Esc, Chr('l'), Enter }) input.Keys.Enqueue(k);
        _step.CompleteOnCancel = true;
        var c = new IfComponent(session, _step.Invoke, 3);

        c.Run(console, input, cts.Token).Should().Be(0);

        writer.ToString().Should().EndWith(LeaveScreen);
    }

    private sealed class CancellingSession(CancellationTokenSource cts) : IIfSession
    {
        public string GameName => "g";
        public int TurnCount => 0;
        public IReadOnlyList<IfTranscriptEvent> Events => [];
        public event Action? Changed { add { } remove { } }
        public Task<IfActionResult> StartAsync(CancellationToken c) => throw new NotSupportedException();
        public Task<IfActionResult> PlayAsync(string actor, string command, string? thought, CancellationToken c)
        {
            cts.Cancel();
            c.ThrowIfCancellationRequested();
            return Task.FromResult(new IfActionResult(true, ""));
        }
        public Task<IfActionResult> SaveSlotAsync(string actor, string name, CancellationToken c) => throw new NotSupportedException();
        public Task<IfActionResult> RestoreSlotAsync(string actor, string name, CancellationToken c) => throw new NotSupportedException();
        public IReadOnlyList<string> ListSlots() => [];
        public IfActionResult AddNote(string actor, string text) => throw new NotSupportedException();
        public string ReadNotes() => "";
        public IfActionResult UpdateNotes(string notes) => throw new NotSupportedException();
        public void RecordThought(string text) { }
        public void RecordFinish(string summary) { }
        public void RecordError(string message) { }
    }

    [Fact]
    public void Run_ShouldUnsubscribeFromSessionChanges_WhenFinished()
    {
        var console = ToolkitConsole.CreateCaptureConsole(new StringWriter());
        var input = new ScriptedInput();
        input.Keys.Enqueue(CtrlC);
        var c = CreateForRun();
        c.Run(console, input, CancellationToken.None);

        var act = () => _session.Raise();

        act.Should().NotThrow();
    }
}
