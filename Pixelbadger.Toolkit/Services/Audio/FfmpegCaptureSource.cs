using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Pixelbadger.Toolkit.Services.Audio;

public sealed record FfmpegCaptureOptions(string FfmpegPath, string CaptureFormat, string Device, int Channels);

public sealed class FfmpegCaptureSource : IAudioCaptureSource
{
    private const int MaxStderrChars = 8192;

    private readonly Process _process;
    private readonly Stream _stdout;
    private readonly Task _stderrTask;
    private readonly StringBuilder _stderr = new();
    private readonly object _stderrLock = new();
    private string? _firstStderrLine;
    private byte[] _scratch = Array.Empty<byte>();
    private int _leftoverByte = -1;
    private bool _disposed;

    private FfmpegCaptureSource(Process process, int channels)
    {
        _process = process;
        Channels = channels;
        ProcessId = process.Id;
        _stdout = process.StandardOutput.BaseStream;
        _stderrTask = Task.Run(DrainStderrAsync);
    }

    public int SampleRate => 48000;

    public int Channels { get; }

    internal int ProcessId { get; }

    /// <summary>For tests: whether the ffmpeg process had exited once disposal completed.</summary>
    internal bool ProcessExitedAfterDispose { get; private set; }

    public static FfmpegCaptureSource Start(FfmpegCaptureOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Channels is not (1 or 2))
            throw new ArgumentOutOfRangeException(nameof(options), "Channels must be 1 or 2.");

        var psi = new ProcessStartInfo
        {
            FileName = options.FfmpegPath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var arg in new[]
                 {
                     "-hide_banner", "-nostdin", "-loglevel", "error",
                     "-f", options.CaptureFormat, "-i", options.Device,
                     "-ac", options.Channels.ToString(System.Globalization.CultureInfo.InvariantCulture),
                     "-ar", "48000", "-f", "s16le", "pipe:1"
                 })
        {
            psi.ArgumentList.Add(arg);
        }

        Process? process = null;
        try
        {
            process = Process.Start(psi) ?? throw new AudioCaptureException("Could not start ffmpeg");
            process.StandardInput.Close();
            return new FfmpegCaptureSource(process, options.Channels);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            process?.Dispose();
            throw new AudioCaptureException($"Could not start ffmpeg: {ex.Message}", ex);
        }
    }

    public async ValueTask<int> ReadAsync(Memory<short> buffer, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (buffer.IsEmpty)
            return 0;

        var bytes = buffer.Length * 2;
        if (_scratch.Length < bytes)
            _scratch = new byte[bytes];
        while (true)
        {
            var offset = 0;
            if (_leftoverByte >= 0)
            {
                _scratch[0] = (byte)_leftoverByte;
                offset = 1;
                _leftoverByte = -1;
            }

            var read = await _stdout.ReadAsync(_scratch.AsMemory(offset, bytes - offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                // A dangling partial sample at end of stream is dropped.
                await _process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                await _stderrTask.WaitAsync(cancellationToken).ConfigureAwait(false);
                if (_process.ExitCode != 0)
                {
                    string line;
                    lock (_stderrLock)
                        line = _firstStderrLine ?? "no error output";
                    throw new AudioCaptureException($"ffmpeg capture failed (exit {_process.ExitCode}): {line}");
                }

                return 0;
            }

            var total = offset + read;
            if ((total & 1) == 1)
            {
                _leftoverByte = _scratch[total - 1];
                total--;
            }

            if (total > 0)
            {
                var samples = total / 2;
                _scratch.AsSpan(0, total).CopyTo(MemoryMarshal.AsBytes(buffer.Span));
                if (!BitConverter.IsLittleEndian)
                {
                    foreach (ref var sample in buffer.Span[..samples])
                        sample = System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(sample);
                }

                return samples;
            }
        }
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

        ProcessExitedAfterDispose = _process.HasExited;
        _process.Dispose();
    }

    private async Task DrainStderrAsync()
    {
        try
        {
            string? line;
            while ((line = await _process.StandardError.ReadLineAsync().ConfigureAwait(false)) != null)
            {
                lock (_stderrLock)
                {
                    if (_firstStderrLine is null && line.Trim().Length > 0)
                        _firstStderrLine = line.Trim();
                    if (_stderr.Length < MaxStderrChars)
                        _stderr.AppendLine(line);
                }
            }
        }
        catch (Exception)
        {
            // Pipe closed during disposal.
        }
    }
}
