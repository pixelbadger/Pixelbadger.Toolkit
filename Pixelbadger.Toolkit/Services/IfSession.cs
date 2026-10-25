namespace Pixelbadger.Toolkit.Services;

/// <summary>The single place where both actors act on the game; serialises engine and store work.</summary>
public sealed class IfSession : IIfSession
{
    private readonly IIfGameEngine _engine;
    private readonly IIfSaveStore _store;
    private readonly Func<DateTimeOffset> _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _eventsLock = new();
    private readonly List<IfTranscriptEvent> _events;

    public IfSession(IIfGameEngine engine, IIfSaveStore store, string gameName, Func<DateTimeOffset>? clock = null)
    {
        _engine = engine;
        _store = store;
        GameName = gameName;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _events = [.. store.ReadEvents()];
    }

    public string GameName { get; }

    public int TurnCount
    {
        get { lock (_eventsLock) return _events.Count(e => e.Kind == IfEventKinds.Command); }
    }

    public IReadOnlyList<IfTranscriptEvent> Events
    {
        get { lock (_eventsLock) return _events.ToList(); }
    }

    public event Action? Changed;

    public async Task<IfActionResult> StartAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_store.HasAutosave)
                return new IfActionResult(true, string.Empty);

            var result = await _engine.StartNewGameAsync(cancellationToken);
            switch (result.Status)
            {
                case IfTurnStatus.TimedOut:
                    Append(IfActors.System, IfEventKinds.Error, "The interpreter timed out.");
                    return new IfActionResult(false, "The interpreter timed out.");
                case IfTurnStatus.Failed:
                    Append(IfActors.System, IfEventKinds.Error, result.Output);
                    return new IfActionResult(false, result.Output);
                case IfTurnStatus.NotSaved:
                    return new IfActionResult(false, "The interpreter did not save the new game.");
                default:
                    Append(IfActors.System, IfEventKinds.Command, "(new game)", result.Output);
                    return new IfActionResult(true, result.Output);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IfActionResult> PlayAsync(string actor, string command, string? thought, CancellationToken cancellationToken)
    {
        var error = IfCommandValidator.Validate(command, actor == IfActors.Human);
        if (error != null)
            return new IfActionResult(false, error);

        var trimmed = command.Trim();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrWhiteSpace(thought))
                Append(actor, IfEventKinds.Thought, thought);

            var result = await _engine.RunTurnAsync(trimmed, cancellationToken);
            switch (result.Status)
            {
                case IfTurnStatus.TimedOut:
                    Append(IfActors.System, IfEventKinds.Error, "The interpreter timed out.");
                    return new IfActionResult(false, "The interpreter timed out.");
                case IfTurnStatus.Failed:
                    Append(IfActors.System, IfEventKinds.Error, result.Output);
                    return new IfActionResult(false, result.Output);
            }

            var output = result.Output;
            if (result.Status == IfTurnStatus.NotSaved)
                output += "\n[not saved: the game may have ended]";
            Append(actor, IfEventKinds.Command, trimmed, output);
            return new IfActionResult(true, output);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IfActionResult> SaveSlotAsync(string actor, string name, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            try
            {
                _store.SaveSlot(name);
            }
            catch (IfStoreException ex)
            {
                return new IfActionResult(false, ex.Message);
            }
            Append(actor, IfEventKinds.Save, name);
            return new IfActionResult(true, $"Saved slot '{name}'.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IfActionResult> RestoreSlotAsync(string actor, string name, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            try
            {
                _store.RestoreSlot(name);
            }
            catch (IfStoreException ex)
            {
                return new IfActionResult(false, ex.Message);
            }

            var result = await _engine.RunTurnAsync("look", cancellationToken);
            switch (result.Status)
            {
                case IfTurnStatus.TimedOut:
                    Append(IfActors.System, IfEventKinds.Error, "The interpreter timed out.");
                    return new IfActionResult(false, "The interpreter timed out.");
                case IfTurnStatus.Failed:
                    Append(IfActors.System, IfEventKinds.Error, result.Output);
                    return new IfActionResult(false, result.Output);
            }

            var output = result.Output;
            if (result.Status == IfTurnStatus.NotSaved)
                output += "\n[not saved: the game may have ended]";
            Append(actor, IfEventKinds.Restore, name, output);
            return new IfActionResult(true, output);
        }
        finally
        {
            _gate.Release();
        }
    }

    public IReadOnlyList<string> ListSlots() => _store.ListSlots();

    public IfActionResult AddNote(string actor, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new IfActionResult(false, "Note cannot be empty.");
        Append(actor, IfEventKinds.Note, text);
        return new IfActionResult(true, text);
    }

    public string ReadNotes() => _store.ReadNotes();

    public IfActionResult UpdateNotes(string notes)
    {
        try
        {
            _store.WriteNotes(notes);
            return new IfActionResult(true, "Notes updated.");
        }
        catch (IfStoreException ex)
        {
            return new IfActionResult(false, ex.Message);
        }
    }

    public void RecordThought(string text) => Append(IfActors.Model, IfEventKinds.Thought, text);

    public void RecordFinish(string summary) => Append(IfActors.Model, IfEventKinds.Finish, summary);

    public void RecordError(string message) => Append(IfActors.System, IfEventKinds.Error, message);

    private void Append(string actor, string kind, string text, string? output = null)
    {
        var transcriptEvent = new IfTranscriptEvent(_clock(), actor, kind, text, output);
        lock (_eventsLock)
        {
            _store.AppendEvent(transcriptEvent);
            _events.Add(transcriptEvent);
        }
        Changed?.Invoke();
    }
}
