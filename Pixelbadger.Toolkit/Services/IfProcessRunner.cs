using System.Diagnostics;
using System.Text;

namespace Pixelbadger.Toolkit.Services;

/// <summary>Runs an external process with Process, capturing stdout and stderr into one buffer.</summary>
public sealed class IfProcessRunner : IIfProcessRunner
{
    public async Task<IfProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string standardInput, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        var buffer = new StringBuilder();
        var gate = new object();
        var stdoutDone = new TaskCompletionSource();
        var stderrDone = new TaskCompletionSource();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) stdoutDone.TrySetResult();
            else lock (gate) buffer.Append(e.Data).Append('\n');
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) stderrDone.TrySetResult();
            else lock (gate) buffer.Append(e.Data).Append('\n');
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            try
            {
                await process.StandardInput.WriteAsync(standardInput.AsMemory(), linked.Token);
                await process.StandardInput.FlushAsync(linked.Token);
            }
            catch (IOException)
            {
                // The process may exit before consuming all input.
            }
            finally
            {
                try { process.StandardInput.Close(); } catch (IOException) { }
            }

            await process.WaitForExitAsync(linked.Token);
            // Make sure the async readers have drained.
            await Task.WhenAll(stdoutDone.Task, stderrDone.Task).WaitAsync(TimeSpan.FromSeconds(5), linked.Token);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            if (cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException(cancellationToken);
            string partial;
            lock (gate) partial = buffer.ToString();
            return new IfProcessResult(-1, partial, true);
        }
        catch (TimeoutException)
        {
            Kill(process);
        }

        string output;
        lock (gate) output = buffer.ToString();
        return new IfProcessResult(process.HasExited ? process.ExitCode : -1, output, false);
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }
}
