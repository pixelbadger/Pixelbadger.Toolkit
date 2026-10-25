namespace Pixelbadger.Toolkit.Services;

/// <summary>Actor names recorded on transcript events.</summary>
public static class IfActors
{
    public const string Model = "model";
    public const string Human = "human";
    public const string System = "system";
}

/// <summary>Event kinds recorded on transcript events.</summary>
public static class IfEventKinds
{
    public const string Command = "command";
    public const string Note = "note";
    public const string Thought = "thought";
    public const string Save = "save";
    public const string Restore = "restore";
    public const string Error = "error";
    public const string Finish = "finish";
}

/// <summary>One transcript.jsonl line. JSON property names: ts, actor, kind, text, output (output omitted when null).</summary>
public sealed record IfTranscriptEvent(DateTimeOffset Ts, string Actor, string Kind, string Text, string? Output = null);

/// <summary>Raw result of one external process run.</summary>
public sealed record IfProcessResult(int ExitCode, string StdOut, bool TimedOut);

/// <summary>
/// Runs one external process to completion: writes standardInput then closes stdin, captures stdout+stderr
/// interleaved into StdOut, kills the whole process tree on timeout (TimedOut=true).
/// </summary>
public interface IIfProcessRunner
{
    Task<IfProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string standardInput, TimeSpan timeout, CancellationToken cancellationToken);
}

/// <summary>Outcome of one dfrotz turn.</summary>
public enum IfTurnStatus { Saved, NotSaved, TimedOut, Failed }

/// <summary>Output is the cleaned game text. For Failed, Output holds the error description.</summary>
public sealed record IfTurnResult(string Output, IfTurnStatus Status);

/// <summary>Game engine: runs dfrotz turns against a save store.</summary>
public interface IIfGameEngine
{
    /// <summary>New game: no -L; stdin "\nlook\n" then the save sequence. Creates the autosave on success.</summary>
    Task<IfTurnResult> StartNewGameAsync(CancellationToken cancellationToken);

    /// <summary>Loads the autosave (-L), runs the command, saves atomically to the autosave.</summary>
    Task<IfTurnResult> RunTurnAsync(string command, CancellationToken cancellationToken);
}

/// <summary>Raised by <see cref="IIfSaveStore"/> for invalid names, missing files, limits and oversized notes.</summary>
public sealed class IfStoreException(string message) : Exception(message);

/// <summary>
/// Per-game persistent store rooted at &lt;root&gt;/&lt;stem&gt;-&lt;sha8&gt;/. Holds run.lock until disposed.
/// </summary>
public interface IIfSaveStore : IDisposable
{
    string GameDirectory { get; }
    string AutosavePath { get; }
    bool HasAutosave { get; }

    /// <summary>Unique, not-yet-existing path inside GameDirectory for dfrotz to save to.</summary>
    string CreateTempSavePath();

    /// <summary>Atomically replaces the autosave with tempPath (File.Move overwrite).</summary>
    void CommitAutosave(string tempPath);

    /// <summary>Deletes tempPath if it exists.</summary>
    void DiscardTemp(string tempPath);

    /// <summary>Slot names, ordinal-sorted.</summary>
    IReadOnlyList<string> ListSlots();

    /// <summary>
    /// Copies the autosave to slots/&lt;name&gt;.qzl. Throws IfStoreException: invalid name, no autosave, or 20-slot limit for a new name.
    /// </summary>
    void SaveSlot(string name);

    /// <summary>Copies slots/&lt;name&gt;.qzl over the autosave. Throws IfStoreException: invalid name or missing slot.</summary>
    void RestoreSlot(string name);

    /// <summary>"" when notes.md is missing.</summary>
    string ReadNotes();

    /// <summary>Replaces notes.md. Throws IfStoreException when longer than 8000 chars.</summary>
    void WriteNotes(string notes);

    /// <summary>Appends one JSON line and flushes.</summary>
    void AppendEvent(IfTranscriptEvent transcriptEvent);

    /// <summary>All events in file order; malformed lines (e.g. torn last line) are skipped.</summary>
    IReadOnlyList<IfTranscriptEvent> ReadEvents();

    /// <summary>
    /// Moves autosave.qzl, transcript.jsonl and notes.md (those that exist) into archive/&lt;yyyyMMdd-HHmmss&gt;/
    /// (suffix -2, -3… if taken), keeps slots, then deletes the oldest archives so at most 5 remain.
    /// </summary>
    void ArchiveCurrent();
}

/// <summary>Result of a session action. Text is game output on success, else the error message.</summary>
public sealed record IfActionResult(bool Success, string Text);

/// <summary>
/// The single place where both actors act on the game. Thread-safe: every method that touches the engine or store is
/// serialised (one at a time); Events returns a snapshot copy. Changed is raised (on the calling thread) after every
/// appended event.
/// </summary>
public interface IIfSession
{
    string GameName { get; }

    /// <summary>Number of Command events in Events.</summary>
    int TurnCount { get; }

    IReadOnlyList<IfTranscriptEvent> Events { get; }

    event Action? Changed;

    /// <summary>
    /// If the store has no autosave: StartNewGameAsync and append (system, command, "(new game)", output).
    /// Otherwise no-op.
    /// </summary>
    Task<IfActionResult> StartAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Validates (IfCommandValidator with isHuman = actor == IfActors.Human). Invalid → (false, message), nothing appended,
    /// engine not called. Valid → if thought non-empty append (actor, thought, thought); run the turn;
    /// Saved → append (actor, command, command, output) and return (true, output);
    /// NotSaved → output += "\n[not saved: the game may have ended]"; append as above; return (true, output);
    /// TimedOut → append (system, error, "The interpreter timed out."); return (false, that);
    /// Failed → append (system, error, output); return (false, output).
    /// </summary>
    Task<IfActionResult> PlayAsync(string actor, string command, string? thought, CancellationToken cancellationToken);

    /// <summary>
    /// store.SaveSlot; append (actor, save, name); return (true, "Saved slot '&lt;name&gt;'."). IfStoreException → (false, message),
    /// nothing appended.
    /// </summary>
    Task<IfActionResult> SaveSlotAsync(string actor, string name, CancellationToken cancellationToken);

    /// <summary>
    /// store.RestoreSlot then engine.RunTurnAsync("look"); append (actor, restore, name, lookOutput); return (true, lookOutput).
    /// IfStoreException → (false, message), nothing appended.
    /// </summary>
    Task<IfActionResult> RestoreSlotAsync(string actor, string name, CancellationToken cancellationToken);

    IReadOnlyList<string> ListSlots();

    /// <summary>Empty/whitespace → (false, "Note cannot be empty."). Else append (actor, note, text) → (true, text).</summary>
    IfActionResult AddNote(string actor, string text);

    string ReadNotes();

    /// <summary>store.WriteNotes; returns (true, "Notes updated.") or (false, message). Appends nothing.</summary>
    IfActionResult UpdateNotes(string notes);

    /// <summary>Append (model, thought, text).</summary>
    void RecordThought(string text);

    /// <summary>Append (model, finish, summary).</summary>
    void RecordFinish(string summary);

    /// <summary>Append (system, error, message).</summary>
    void RecordError(string message);
}

/// <summary>Result of one agent step (one model response and all of its tool calls).</summary>
public sealed record IfStepResult(bool Finished, string? Summary, int ToolCalls);
