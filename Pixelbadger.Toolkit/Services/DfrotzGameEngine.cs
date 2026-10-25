namespace Pixelbadger.Toolkit.Services;

/// <summary>Runs one dfrotz load, command, save, quit cycle per call, saving atomically.</summary>
public sealed class DfrotzGameEngine(IIfProcessRunner runner, string interpreterPath, string gameFile, IIfSaveStore store, TimeSpan? turnTimeout = null) : IIfGameEngine
{
    private readonly TimeSpan _timeout = turnTimeout ?? TimeSpan.FromSeconds(10);
    private readonly string _gameFile = Path.GetFullPath(gameFile);

    public Task<IfTurnResult> StartNewGameAsync(CancellationToken cancellationToken) =>
        RunAsync(loadAutosave: false, "\nlook\n", cancellationToken);

    public Task<IfTurnResult> RunTurnAsync(string command, CancellationToken cancellationToken) =>
        RunAsync(loadAutosave: true, command + "\n", cancellationToken);

    private async Task<IfTurnResult> RunAsync(bool loadAutosave, string commandInput, CancellationToken cancellationToken)
    {
        var temp = store.CreateTempSavePath();
        var arguments = new List<string> { "-m", "-p", "-q", "-w", "80", "-R", store.GameDirectory };
        if (loadAutosave)
        {
            arguments.Add("-L");
            arguments.Add(store.AutosavePath);
        }
        arguments.Add(_gameFile);

        // dfrotz truncates typed input at the screen width, so type only the short file name; -R resolves it inside the game directory.
        var stdin = $"{commandInput}save\n{Path.GetFileName(temp)}\nquit\ny\n";

        IfProcessResult result;
        try
        {
            result = await runner.RunAsync(interpreterPath, arguments, stdin, _timeout, cancellationToken);
        }
        catch
        {
            store.DiscardTemp(temp);
            throw;
        }

        if (result.TimedOut)
        {
            store.DiscardTemp(temp);
            return new IfTurnResult("The interpreter timed out.", IfTurnStatus.TimedOut);
        }

        var output = IfOutputCleaner.Clean(result.StdOut);

        if (File.Exists(temp) && new FileInfo(temp).Length > 0)
        {
            store.CommitAutosave(temp);
            return new IfTurnResult(output, IfTurnStatus.Saved);
        }

        store.DiscardTemp(temp);
        if (result.ExitCode != 0 && output.Length == 0)
            return new IfTurnResult($"The interpreter failed (exit code {result.ExitCode}).", IfTurnStatus.Failed);
        return new IfTurnResult(output, IfTurnStatus.NotSaved);
    }
}
