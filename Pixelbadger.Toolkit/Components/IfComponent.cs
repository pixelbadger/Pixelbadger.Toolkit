using System.Text;
using Pixelbadger.Toolkit.Services;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Pixelbadger.Toolkit.Components;

public enum IfRunState { Running, Halted }

// Inform-style full-screen TUI for `llm if`: a status bar, a scrolling transcript, and an input line.
// The model plays through background agent steps; the human halts, plays by hand, leaves notes and resumes.
public class IfComponent
{
    public const int MinWidth = 60;
    public const int MinHeight = 16;
    private static readonly TimeSpan StepShutdownWait = TimeSpan.FromSeconds(15);
    private const string HaltedMessage = "Halted. Type a command, !note, /save <name>, /restore <name>, /slots or /quit. Enter on an empty line resumes.";
    private const string UnknownCommandMessage = "Unknown command. Try /save, /restore, /slots, /quit.";
    private const string RunningHint = "  (Esc to halt · PgUp/PgDn to scroll · Ctrl+C to quit)";
    private const string EnterAlternateScreen = "\x1b[?1049h\x1b[?25l\x1b[2J\x1b[H";
    private const string LeaveAlternateScreen = "\x1b[0m\x1b[?25h\x1b[?1049l";
    private const string Cursor = "█";

    private sealed record StyledLine(string Text, string? Style);

    private readonly IIfSession _session;
    private readonly Func<CancellationToken, Task<IfStepResult>> _step;
    private readonly int _maxSteps;
    private readonly StringBuilder _input = new();
    private Task<IfStepResult>? _stepTask;
    private CancellationTokenSource? _stepCts;
    private CancellationToken _runToken = CancellationToken.None;
    private int _dirty = 1;
    private int _scroll;
    private int _height = 24;
    private List<StyledLine> _lineCache = [];
    private int _cachedEventCount = -1;
    private int _cachedWidth = -1;

    public IfComponent(IIfSession session, Func<CancellationToken, Task<IfStepResult>> step, int maxSteps)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _step = step ?? throw new ArgumentNullException(nameof(step));
        _maxSteps = maxSteps;

        var events = _session.Events;
        if (events.Count > 0 && events[^1].Kind == IfEventKinds.Finish)
        {
            State = IfRunState.Halted;
            StatusMessage = "Finished: " + events[^1].Text;
        }
        _session.Changed += OnChanged;
    }

    public IfRunState State { get; private set; } = IfRunState.Running;

    public string InputLine => _input.ToString();

    public string? StatusMessage { get; private set; }

    public int StepsThisRun { get; private set; }

    public bool Thinking { get; private set; }

    private void OnChanged() => Interlocked.Exchange(ref _dirty, 1);

    // ---------------------------------------------------------------- step lifecycle

    // Collects a finished step and starts the next one when running. Called on every poll of the run loop.
    public void Tick()
    {
        if (_stepTask is { IsCompleted: true } done)
        {
            CompleteStep(done);
        }

        if (State == IfRunState.Running && _stepTask is null && StepsThisRun < _maxSteps)
        {
            StartStep();
        }
    }

    private void StartStep()
    {
        _stepCts?.Dispose();
        _stepCts = new CancellationTokenSource();
        Thinking = true;
        try
        {
            _stepTask = _step(_stepCts.Token);
        }
        catch (Exception ex)
        {
            _stepTask = Task.FromException<IfStepResult>(ex);
        }
        Interlocked.Exchange(ref _dirty, 1);
    }

    private void CompleteStep(Task<IfStepResult> task)
    {
        _stepTask = null;
        Thinking = false;
        bool haltRequested = _stepCts?.IsCancellationRequested == true;
        Interlocked.Exchange(ref _dirty, 1);

        if (task.IsCanceled || (task.IsFaulted && task.Exception!.GetBaseException() is OperationCanceledException && haltRequested))
        {
            if (haltRequested) return;
            ReportError(new OperationCanceledException("The request was cancelled."));
            return;
        }

        if (task.IsFaulted)
        {
            ReportError(task.Exception!.GetBaseException());
            return;
        }

        var result = task.Result;
        StepsThisRun++;
        if (result.Finished)
        {
            State = IfRunState.Halted;
            StatusMessage = "Finished: " + result.Summary;
        }
        else if (State == IfRunState.Running && StepsThisRun >= _maxSteps)
        {
            State = IfRunState.Halted;
            StatusMessage = $"Step limit reached ({StepsThisRun} steps). Press Enter to continue.";
        }
    }

    private void ReportError(Exception ex)
    {
        string message = "Model error: " + ex.Message;
        _session.RecordError(message);
        State = IfRunState.Halted;
        StatusMessage = message;
    }

    private void CancelStep()
    {
        try { _stepCts?.Cancel(); } catch (ObjectDisposedException) { }
    }

    // ---------------------------------------------------------------- input

    public async Task<bool> HandleKeyAsync(ConsoleKeyInfo key)
    {
        if (IsCtrlC(key))
        {
            CancelStep();
            return false;
        }

        switch (key.Key)
        {
            case ConsoleKey.PageUp:
                _scroll += PageSize();
                return true;
            case ConsoleKey.PageDown:
                _scroll = Math.Max(0, _scroll - PageSize());
                return true;
            case ConsoleKey.End:
                _scroll = 0;
                return true;
        }

        if (State == IfRunState.Running)
        {
            if (key.Key == ConsoleKey.Escape)
            {
                CancelStep();
                State = IfRunState.Halted;
                StatusMessage = HaltedMessage;
            }
            return true;
        }

        if (key.Key == ConsoleKey.Escape)
        {
            _input.Clear();
            return true;
        }
        if (key.Key == ConsoleKey.Backspace)
        {
            if (_input.Length > 0) _input.Length--;
            return true;
        }
        if (key.Key == ConsoleKey.Enter || key.KeyChar is '\r' or '\n')
        {
            string line = _input.ToString().Trim();
            _input.Clear();
            return await SubmitAsync(line);
        }
        if (key.KeyChar >= ' ' && !char.IsControl(key.KeyChar) && (key.Modifiers & ConsoleModifiers.Control) == 0
            && _input.Length < IfCommandValidator.MaxLength)
        {
            _input.Append(key.KeyChar);
        }
        return true;
    }

    // Run's key handler: a non-cancellation failure (e.g. an IOException from a session call) is shown, not fatal.
    internal async Task<bool> HandleKeySafeAsync(ConsoleKeyInfo key)
    {
        try
        {
            return await HandleKeyAsync(key);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            StatusMessage = "Error: " + ex.Message;
            Interlocked.Exchange(ref _dirty, 1);
            return true;
        }
    }

    private static bool IsCtrlC(ConsoleKeyInfo key) =>
        key.KeyChar == '\u0003' || (key.Key == ConsoleKey.C && (key.Modifiers & ConsoleModifiers.Control) != 0);

    private int PageSize() => Math.Max(1, _height - 3 - 1);

    private async Task<bool> SubmitAsync(string line)
    {
        if (line.Length == 0)
        {
            if (StepsThisRun >= _maxSteps) StepsThisRun = 0;
            State = IfRunState.Running;
            StatusMessage = null;
            return true;
        }

        if (line.StartsWith('/'))
        {
            string[] parts = line.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
            string command = parts[0].ToLowerInvariant();
            string argument = parts.Length > 1 ? parts[1].Trim() : string.Empty;
            switch (command)
            {
                case "/quit":
                    CancelStep();
                    return false;
                case "/save":
                    SetResult(await _session.SaveSlotAsync(IfActors.Human, argument, _runToken));
                    return true;
                case "/restore":
                    SetResult(await _session.RestoreSlotAsync(IfActors.Human, argument, _runToken));
                    return true;
                case "/slots":
                    var slots = _session.ListSlots();
                    StatusMessage = slots.Count == 0 ? "No slots saved." : "Slots: " + string.Join(", ", slots);
                    return true;
                default:
                    StatusMessage = UnknownCommandMessage;
                    return true;
            }
        }

        if (line.StartsWith('!'))
        {
            SetResult(_session.AddNote(IfActors.Human, line[1..].Trim()));
            return true;
        }

        SetResult(await _session.PlayAsync(IfActors.Human, line, null, _runToken));
        return true;
    }

    private void SetResult(IfActionResult result) => StatusMessage = result.Success ? null : result.Text;

    // ---------------------------------------------------------------- run loop

    public int Run(IAnsiConsole console, ITerminalInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(input);

        var writer = console.Profile.Out.Writer;
        bool? previousTreatControlC = null;
        _runToken = cancellationToken;
        try
        {
            try { previousTreatControlC = Console.TreatControlCAsInput; } catch (Exception) { }
            try { Console.TreatControlCAsInput = true; } catch (Exception) { }

            writer.Write(EnterAlternateScreen);
            writer.Flush();

            var size = input.Size;
            Tick();
            Draw(console, writer, size);

            while (!cancellationToken.IsCancellationRequested)
            {
                Tick();
                if (input.KeyAvailable)
                {
                    bool keepGoing;
                    try
                    {
                        keepGoing = HandleKeySafeAsync(input.ReadKey()).GetAwaiter().GetResult();
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                    if (!keepGoing) break;
                    Tick();
                    size = input.Size;
                    Draw(console, writer, size);
                    continue;
                }

                var current = input.Size;
                if (current != size)
                {
                    size = current;
                    writer.Write("\x1b[2J");
                    Draw(console, writer, size);
                }
                else if (Interlocked.CompareExchange(ref _dirty, 0, 1) == 1)
                {
                    Draw(console, writer, size);
                }
                else
                {
                    cancellationToken.WaitHandle.WaitOne(30);
                }
            }
            return 0;
        }
        finally
        {
            Shutdown();
            _session.Changed -= OnChanged;
            _runToken = CancellationToken.None;
            writer.Write(LeaveAlternateScreen);
            writer.Flush();
            if (previousTreatControlC is bool previous)
            {
                try { Console.TreatControlCAsInput = previous; } catch (Exception) { }
            }
        }
    }

    private void Shutdown()
    {
        CancelStep();
        var task = _stepTask;
        if (task is null) return;
        try { task.Wait(StepShutdownWait); } catch (Exception) { }
    }

    private void Draw(IAnsiConsole console, TextWriter writer, (int Width, int Height) size)
    {
        Interlocked.Exchange(ref _dirty, 0);
        writer.Write("\x1b[H");
        console.Write(Render(size.Width, size.Height));
        writer.Flush();
    }

    // ---------------------------------------------------------------- rendering

    public IRenderable Render(int width, int height)
    {
        _height = height;
        if (width < MinWidth || height < MinHeight) return RenderTooSmall(width, height);

        int paneHeight = height - 3;
        var lines = new List<string>(height) { StatusBar(width) };

        var transcript = TranscriptLines(width);
        int maxScroll = Math.Max(0, transcript.Count - paneHeight);
        _scroll = Math.Clamp(_scroll, 0, maxScroll);
        int end = transcript.Count - _scroll;
        int start = end - paneHeight;
        for (int i = start; i < end; i++)
        {
            lines.Add(i < 0 ? Pad(string.Empty, width) : Styled(transcript[i], width));
        }

        lines.Add(MessageRow(width));
        lines.Add(InputRow(width));
        return new TrimmedRenderable(new Markup(string.Join("\n", lines)) { Overflow = Overflow.Crop });
    }

    private static string Pad(string markup, int visibleLength, int width) =>
        markup + new string(' ', Math.Max(0, width - visibleLength));

    private static string Pad(string plain, int width) => Markup.Escape(plain) + new string(' ', Math.Max(0, width - plain.Length));

    private static string Styled(StyledLine line, int width)
    {
        string text = line.Text.Length > width ? line.Text[..width] : line.Text;
        string escaped = Markup.Escape(text);
        string body = line.Style is null || text.Length == 0 ? escaped : $"[{line.Style}]{escaped}[/]";
        return Pad(body, text.Length, width);
    }

    private string StatusBar(int width)
    {
        string state = Thinking ? "Thinking…" : State == IfRunState.Running ? "Running" : "Halted";
        string right = $"{state} · step {StepsThisRun}/{_maxSteps} · turns {_session.TurnCount} ";
        string left = " " + _session.GameName.Replace('\n', ' ');
        int room = width - right.Length;
        if (left.Length > room) left = room > 0 ? left[..room] : string.Empty;
        string bar = left + new string(' ', Math.Max(0, width - left.Length - right.Length)) + right;
        if (bar.Length > width) bar = bar[..width];
        return "[invert]" + Markup.Escape(bar) + "[/]";
    }

    private string MessageRow(int width)
    {
        if (string.IsNullOrEmpty(StatusMessage)) return Pad(string.Empty, width);
        string text = Clip(StatusMessage.ReplaceLineEndings(" "), width);
        return Pad($"[yellow]{Markup.Escape(text)}[/]", text.Length, width);
    }

    private string InputRow(int width)
    {
        if (State == IfRunState.Running)
        {
            string hint = Clip(RunningHint, width);
            return Pad($"[grey]{Markup.Escape(hint)}[/]", hint.Length, width);
        }
        string text = "> " + _input + Cursor;
        if (text.Length > width) text = text[^width..];
        return Pad(text, width);
    }

    private static string Clip(string text, int width) => text.Length <= width ? text : text[..width];

    private static IRenderable RenderTooSmall(int width, int height)
    {
        var lines = new string[height];
        Array.Fill(lines, string.Empty);
        string text = Clip($"Terminal too small (need {MinWidth}x{MinHeight})", Math.Max(1, width));
        int pad = Math.Max(0, (width - text.Length) / 2);
        lines[(height - 1) / 2] = new string(' ', pad) + Markup.Escape(text);
        return new TrimmedRenderable(new Markup(string.Join("\n", lines)) { Overflow = Overflow.Crop });
    }

    // Rendering a Markup ends with a line break; a full-height frame must not end in one or the terminal scrolls.
    private sealed class TrimmedRenderable(IRenderable inner) : IRenderable
    {
        public Measurement Measure(RenderOptions options, int maxWidth) => inner.Measure(options, maxWidth);

        public IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
        {
            var segments = inner.Render(options, maxWidth).ToList();
            int end = segments.Count;
            while (end > 0 && segments[end - 1].IsLineBreak) end--;
            return segments.Take(end);
        }
    }

    private List<StyledLine> TranscriptLines(int width)
    {
        var events = _session.Events;
        if (events.Count == _cachedEventCount && width == _cachedWidth) return _lineCache;

        var lines = new List<StyledLine>();
        foreach (var e in events)
        {
            var block = new List<StyledLine>();
            switch (e.Kind)
            {
                case IfEventKinds.Command when e.Actor == IfActors.System:
                    AddWrapped(block, e.Output, null, width);
                    break;
                case IfEventKinds.Command:
                    AddWrapped(block, ">" + e.Text + (e.Actor == IfActors.Human ? " (you)" : string.Empty), "bold", width);
                    AddWrapped(block, e.Output, null, width);
                    break;
                case IfEventKinds.Thought:
                    AddWrapped(block, "~ " + e.Text, "italic dim", width);
                    break;
                case IfEventKinds.Note:
                    AddWrapped(block, "! " + e.Text, "yellow", width);
                    break;
                case IfEventKinds.Save:
                    AddWrapped(block, $"[saved slot {e.Text}]", "green", width);
                    break;
                case IfEventKinds.Restore:
                    AddWrapped(block, $"[restored slot {e.Text}]", "green", width);
                    AddWrapped(block, e.Output, null, width);
                    break;
                case IfEventKinds.Error:
                    AddWrapped(block, "[error] " + e.Text, "red", width);
                    break;
                case IfEventKinds.Finish:
                    AddWrapped(block, "[finished] " + e.Text, "cyan", width);
                    break;
                default:
                    AddWrapped(block, e.Text, null, width);
                    break;
            }
            if (block.Count == 0) continue;
            if (lines.Count > 0) lines.Add(new StyledLine(string.Empty, null));
            lines.AddRange(block);
        }

        _lineCache = lines;
        _cachedEventCount = events.Count;
        _cachedWidth = width;
        return lines;
    }

    private static void AddWrapped(List<StyledLine> target, string? text, string? style, int width)
    {
        if (string.IsNullOrEmpty(text)) return;
        foreach (var raw in text.ReplaceLineEndings("\n").Replace("\t", "    ").Split('\n'))
        {
            foreach (var piece in WrapLine(raw, width)) target.Add(new StyledLine(piece, style));
        }
    }

    // Word wrap at width; words longer than a line are split hard. Leading indentation of the first piece is kept.
    internal static IEnumerable<string> WrapLine(string line, int width)
    {
        line = line.TrimEnd();
        if (line.Length <= width)
        {
            yield return line;
            yield break;
        }
        string rest = line;
        while (rest.Length > width)
        {
            int cut = rest.LastIndexOf(' ', width);
            if (cut <= 0)
            {
                yield return rest[..width];
                rest = rest[width..];
            }
            else
            {
                yield return rest[..cut].TrimEnd();
                rest = rest[(cut + 1)..].TrimStart();
            }
        }
        if (rest.Length > 0) yield return rest;
    }
}
