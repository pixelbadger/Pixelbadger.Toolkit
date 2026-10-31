using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Pixelbadger.Toolkit.Services.Audio;

public sealed class FfmpegPlaybackSinkFactory : IAudioPlaybackSinkFactory
{
    private readonly Func<string?> _ffmpegLocator;

    public FfmpegPlaybackSinkFactory(Func<string?>? ffmpegLocator = null)
    {
        _ffmpegLocator = ffmpegLocator ?? (() => FfmpegLocator.Resolve());
    }

    public IAudioPlaybackSink Open(AudioPlaybackOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var ffmpeg = _ffmpegLocator();
        if (ffmpeg is null)
            throw new InvalidOperationException("ffmpeg was not found on PATH. Install ffmpeg (e.g. 'apt install ffmpeg' or 'brew install ffmpeg').");
        return FfmpegPlaybackSink.Start(ffmpeg, options);
    }
}

/// <summary>Plays s16le 48 kHz stereo by piping it into an ffmpeg output process.</summary>
public sealed class FfmpegPlaybackSink : IAudioPlaybackSink
{
    private readonly Process _process;
    private readonly Stream _stdin;
    private readonly Task _stderrTask;
    private readonly object _stderrLock = new();
    private string? _firstStderrLine;
    private byte[] _scratch = Array.Empty<byte>();
    private bool _completed;
    private bool _disposed;

    private FfmpegPlaybackSink(Process process)
    {
        _process = process;
        ProcessId = process.Id;
        _stdin = process.StandardInput.BaseStream;
        _stderrTask = Task.Run(DrainStderrAsync);
    }

    internal int ProcessId { get; }

    /// <summary>For tests: whether the ffmpeg process had exited once disposal completed.</summary>
    internal bool ProcessExitedAfterDispose { get; private set; }

    internal static IReadOnlyList<string> BuildArguments(AudioPlaybackOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var args = new List<string>
        {
            "-hide_banner", "-loglevel", "error",
            "-f", "s16le", "-ar", "48000", "-ac", "2", "-i", "pipe:0"
        };
        switch (options.OutputFormat)
        {
            case "pulse":
                // PulseAudio rejects the literal device name "default"; omitting -device selects the default sink.
                args.AddRange(new[] { "-buffer_duration", "50", "-f", "pulse" });
                if (!string.Equals(options.Device, "default", StringComparison.OrdinalIgnoreCase))
                    args.AddRange(new[] { "-device", options.Device });
                args.Add("Pixelbadger Toolkit");
                break;
            case "audiotoolbox":
                args.AddRange(new[] { "-f", "audiotoolbox", "-audio_device_index", options.Device, "-" });
                break;
            case "sdl":
            case "sdl2":
                args.AddRange(new[] { "-f", options.OutputFormat, "Pixelbadger Toolkit" });
                break;
            default:
                args.AddRange(new[] { "-f", options.OutputFormat, options.Device });
                break;
        }

        return args;
    }

    internal static FfmpegPlaybackSink Start(string ffmpegPath, AudioPlaybackOptions options)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var arg in BuildArguments(options))
            psi.ArgumentList.Add(arg);

        Process? process = null;
        try
        {
            process = Process.Start(psi) ?? throw new AudioPlaybackException("Could not start ffmpeg");
            return new FfmpegPlaybackSink(process);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            process?.Dispose();
            throw new AudioPlaybackException($"Could not start ffmpeg: {ex.Message}", ex);
        }
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<short> interleavedStereo, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (interleavedStereo.IsEmpty)
            return;

        var byteCount = interleavedStereo.Length * 2;
        if (_scratch.Length < byteCount)
            _scratch = new byte[byteCount];
        var span = interleavedStereo.Span;
        if (BitConverter.IsLittleEndian)
        {
            MemoryMarshal.AsBytes(span).CopyTo(_scratch);
        }
        else
        {
            for (var i = 0; i < span.Length; i++)
                System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(_scratch.AsSpan(i * 2), span[i]);
        }

        // A pipe write blocked on a full buffer may not observe the token, so wait on it separately;
        // the caller then disposes the sink, which kills ffmpeg and unblocks the abandoned write.
        var write = _stdin.WriteAsync(_scratch.AsMemory(0, byteCount), CancellationToken.None).AsTask();
        _ = write.ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
        try
        {
            await write.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            throw await ExitFailureAsync(ex).ConfigureAwait(false);
        }
    }

    public async Task CompleteAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completed)
            return;
        _completed = true;

        try
        {
            await _stdin.FlushAsync(cancellationToken).ConfigureAwait(false);
            _stdin.Close();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // ffmpeg already went away; report through its exit status below.
        }

        await _process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        await _stderrTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (_process.ExitCode != 0)
            throw FailureException();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;

        try
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Could not signal; fall through to the wait.
        }

        try
        {
            await _process.WaitForExitAsync().ConfigureAwait(false);
            await _stderrTask.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Disposal must not throw.
        }

        try
        {
            ProcessExitedAfterDispose = _process.HasExited;
            _process.Dispose();
        }
        catch (Exception)
        {
            // Disposal must not throw.
        }
    }

    private async Task<Exception> ExitFailureAsync(Exception writeError)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            await _stderrTask.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Fall through with whatever was captured.
        }

        return _process.HasExited && _process.ExitCode != 0
            ? FailureException()
            : new AudioPlaybackException($"ffmpeg playback failed: {writeError.Message}", writeError);
    }

    private AudioPlaybackException FailureException()
    {
        string line;
        lock (_stderrLock)
            line = _firstStderrLine ?? "no error output";
        return new AudioPlaybackException($"ffmpeg playback failed (exit {_process.ExitCode}): {line}");
    }

    private async Task DrainStderrAsync()
    {
        try
        {
            var stdout = _process.StandardOutput.BaseStream;
            _ = stdout.CopyToAsync(Stream.Null);
            // Keep reading so ffmpeg never blocks on a full pipe; only the first non-empty line is retained.
            string? line;
            while ((line = await _process.StandardError.ReadLineAsync().ConfigureAwait(false)) != null)
            {
                lock (_stderrLock)
                {
                    if (_firstStderrLine is null && line.Trim().Length > 0)
                        _firstStderrLine = line.Trim();
                }
            }
        }
        catch (Exception)
        {
            // Pipe closed during disposal.
        }
    }
}
