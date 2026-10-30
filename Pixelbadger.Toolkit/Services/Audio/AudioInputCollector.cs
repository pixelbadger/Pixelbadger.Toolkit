using Pixelbadger.Toolkit.CommandLine;
using Pixelbadger.Toolkit.Components;

namespace Pixelbadger.Toolkit.Services.Audio;

public sealed class AudioInputCollector : IAudioInputCollector
{
    private const int SampleRate = 48000;
    private const int ChunkFrames = 960;
    private const int BlockSamples = 1 << 17;
    private const long MaxPreallocatedSamples = 1L << 28;
    // Ctrl+C reaches ffmpeg and the toolkit at about the same time; ffmpeg can exit before the toolkit observes its own cancellation.
    private static readonly TimeSpan StopRaceWindow = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(25);

    private readonly ITerminalInput _input;
    private readonly TextWriter? _output;
    private readonly Func<FfmpegCaptureOptions, IAudioCaptureSource> _sourceFactory;
    private readonly Func<string?> _ffmpegLocator;
    private readonly Func<bool> _isNonInteractive;
    private readonly Func<(string Format, string? Device)> _defaults;
    private readonly int _blockSamples;
    private readonly long _maxPreallocatedSamples;

    public AudioInputCollector(
        ITerminalInput input,
        TextWriter? output = null,
        Func<FfmpegCaptureOptions, IAudioCaptureSource>? sourceFactory = null,
        Func<string?>? ffmpegLocator = null,
        Func<bool>? isNonInteractive = null,
        Func<(string Format, string? Device)>? defaults = null)
        : this(input, output, sourceFactory, ffmpegLocator, isNonInteractive, defaults, BlockSamples, MaxPreallocatedSamples)
    {
    }

    /// <summary>Test seam: the capture buffer's block size and the largest known length it preallocates as a single array.</summary>
    internal AudioInputCollector(
        ITerminalInput input,
        TextWriter? output,
        Func<FfmpegCaptureOptions, IAudioCaptureSource>? sourceFactory,
        Func<string?>? ffmpegLocator,
        Func<bool>? isNonInteractive,
        Func<(string Format, string? Device)>? defaults,
        int blockSamples,
        long maxPreallocatedSamples)
    {
        ArgumentNullException.ThrowIfNull(input);
        _blockSamples = blockSamples;
        _maxPreallocatedSamples = maxPreallocatedSamples;
        _input = input;
        _output = output;
        _sourceFactory = sourceFactory ?? (o => FfmpegCaptureSource.Start(o));
        _ffmpegLocator = ffmpegLocator ?? (() => FfmpegLocator.Resolve());
        _isNonInteractive = isNonInteractive ?? (() => ToolkitConsole.IsCaptured || Console.IsInputRedirected);
        _defaults = defaults ?? AudioCaptureDefaults.ForCurrentPlatform;
    }

    public void Validate(AudioInputRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.FromWav is not null)
        {
            if (!File.Exists(request.FromWav))
                throw new FileNotFoundException($"File '{request.FromWav}' not found");
            return;
        }

        if (_ffmpegLocator() is null)
            throw new InvalidOperationException(
                "ffmpeg was not found on PATH. Install ffmpeg (e.g. 'apt install ffmpeg' or 'brew install ffmpeg') or use --from-wav.");
        if (request.Device is null && _defaults().Device is null)
            throw new InvalidOperationException("--device is required on Windows, e.g. --device \"audio=Microphone\"");
        if (request.Duration is null && _isNonInteractive())
            throw new InvalidOperationException("--duration or --from-wav is required when no interactive console is available");
    }

    public async Task<short[]> CollectAsync(AudioInputRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);
        var maxFrames = request.Duration is { } d ? DurationToFrames(d) : long.MaxValue;

        var abortOnCancel = AudioCommandSupport.AbortsOnCancel(request);

        if (request.FromWav is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var wav = ReadWav(request, maxFrames);
            cancellationToken.ThrowIfCancellationRequested();
            return wav;
        }

        var ffmpeg = _ffmpegLocator()!;
        var defaults = _defaults();
        var options = new FfmpegCaptureOptions(
            ffmpeg,
            request.CaptureFormat ?? defaults.Format,
            request.Device ?? defaults.Device!,
            request.Channels);

        if (request.Duration is null)
            (_output ?? ToolkitConsole.Out).WriteLine("Recording... press Enter or Ctrl+C to stop");

        var (pcm, failure) = await CaptureAsync(options, request.Channels, maxFrames, request.Duration is null, cancellationToken);
        // With --duration, a cancellation aborts the take; without it, the cancellation is the stop request.
        if (abortOnCancel)
            cancellationToken.ThrowIfCancellationRequested();

        var requestedFrames = maxFrames;
        if (failure is not null)
        {
            (_output ?? ToolkitConsole.Out).WriteLine(
                $"Warning: capture failed after {AudioCommandSupport.Seconds(pcm.Length / request.Channels)}s: {failure}");
        }
        else if (request.Duration is not null && pcm.Length / request.Channels < requestedFrames)
        {
            (_output ?? ToolkitConsole.Out).WriteLine(
                $"Warning: capture ended after {AudioCommandSupport.Seconds(pcm.Length / request.Channels)}s, before the requested {AudioCommandSupport.Seconds(requestedFrames)}s");
        }

        return pcm;
    }

    internal static long DurationToFrames(TimeSpan duration) =>
        (long)Math.Round(duration.TotalSeconds * SampleRate, MidpointRounding.AwayFromZero);

    private static short[] ReadWav(AudioInputRequest request, long maxFrames)
    {
        PcmAudio audio;
        using (var stream = File.OpenRead(request.FromWav!))
            audio = WavReader.Read(stream);
        var pcm = PcmConverter.ToOpusInput(audio, request.Channels);
        var maxSamples = maxFrames == long.MaxValue ? long.MaxValue : maxFrames * request.Channels;
        return pcm.Length <= maxSamples ? pcm : pcm.AsSpan(0, (int)maxSamples).ToArray();
    }

    /// <summary>Captures until the length, the source's end, or the user's stop. A source failure that is not the user's stop keeps what was captured (reported in the second item); with nothing captured it is thrown.</summary>
    private async Task<(short[] Pcm, string? Failure)> CaptureAsync(FfmpegCaptureOptions options, int channels, long maxFrames, bool waitForEnter, CancellationToken cancellationToken)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var maxSamples = maxFrames == long.MaxValue ? long.MaxValue : maxFrames * channels;
        var samples = new SampleBuffer(_blockSamples, maxSamples, _maxPreallocatedSamples);
        string? failure = null;
        var chunk = ChunkFrames * channels;
        Task? poller = null;
        var source = _sourceFactory(options);
        try
        {
            if (waitForEnter)
                poller = Task.Run(() => PollForEnterAsync(stop));

            while (samples.Count < maxSamples)
            {
                var want = (int)Math.Min(chunk, maxSamples - samples.Count);
                var target = samples.Next(want);
                int read;
                try
                {
                    read = await source.ReadAsync(target, stop.Token);
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested)
                {
                    break;
                }
                catch (AudioCaptureException ex)
                {
                    if (await StoppedByUserAsync(cancellationToken))
                        break;
                    if (samples.Count - samples.Count % channels == 0)
                        throw;
                    failure = ex.Message;
                    break;
                }

                if (read == 0)
                    break;
                samples.Commit(read);
            }
        }
        finally
        {
            stop.Cancel();
            if (poller is not null)
                await poller;
            await source.DisposeAsync();
        }

        return (samples.ToArray(samples.Count - samples.Count % channels), failure);
    }

    /// <summary>
    /// A capture failure counts as the user's stop when the action is cancelled within <see cref="StopRaceWindow"/> of the failure,
    /// however loaded the machine: Ctrl+C reaches ffmpeg too, and it may exit before the toolkit observes the cancellation.
    /// </summary>
    private static async Task<bool> StoppedByUserAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return true;
        try
        {
            await Task.Delay(StopRaceWindow, cancellationToken);
            return false;
        }
        catch (OperationCanceledException)
        {
            return true;
        }
    }

    private async Task PollForEnterAsync(CancellationTokenSource stop)
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                if (_input.KeyAvailable && _input.ReadKey().Key == ConsoleKey.Enter)
                {
                    stop.Cancel();
                    return;
                }

                await Task.Delay(PollInterval, stop.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (InvalidOperationException)
        {
            // No usable keyboard (stdin redirected): only --duration or Ctrl+C can stop the capture.
        }
    }
}

/// <summary>
/// Sample store for a capture. A known length (up to the preallocation limit) gets one array that reads fill in
/// place, so the result needs no copy; otherwise samples go into fixed-size blocks that are copied once, at the end.
/// </summary>
internal sealed class SampleBuffer
{
    private readonly int _blockSamples;
    private readonly short[]? _single;
    private readonly List<short[]> _blocks = [];
    private int _usedInLast;
    private int _pending;

    public SampleBuffer(int blockSamples, long expectedSamples, long maxPreallocatedSamples)
    {
        _blockSamples = blockSamples;
        _usedInLast = blockSamples;
        if (expectedSamples > 0 && expectedSamples <= maxPreallocatedSamples)
            _single = new short[expectedSamples];
    }

    public long Count { get; private set; }

    /// <summary>Blocks still referenced; zero once <see cref="ToArray"/> has consolidated them.</summary>
    public int RetainedBlocks => _blocks.Count;

    /// <summary>Space for up to <paramref name="want"/> more samples; <see cref="Commit"/> says how many were written.</summary>
    public Memory<short> Next(int want)
    {
        if (_single is not null)
        {
            _pending = (int)Math.Min(want, _single.Length - Count);
            return _single.AsMemory((int)Count, _pending);
        }

        if (_usedInLast == _blockSamples)
        {
            _blocks.Add(new short[_blockSamples]);
            _usedInLast = 0;
        }

        _pending = Math.Min(want, _blockSamples - _usedInLast);
        return _blocks[^1].AsMemory(_usedInLast, _pending);
    }

    public void Commit(int written)
    {
        if (written < 0 || written > _pending)
            throw new InvalidOperationException("Capture source reported more samples than requested");
        _usedInLast += written;
        Count += written;
    }

    public short[] ToArray(long length)
    {
        if (_single is not null)
        {
            if (length == _single.Length)
                return _single;
            return _single.AsSpan(0, (int)length).ToArray();
        }

        var result = new short[length];
        var offset = 0L;
        // Each block is dropped as soon as it is copied, so the blocks and the result are never both fully live.
        for (var i = 0; i < _blocks.Count; i++)
        {
            var block = _blocks[i];
            _blocks[i] = [];
            var n = (int)Math.Min(block.Length, length - offset);
            if (n > 0)
            {
                block.AsSpan(0, n).CopyTo(result.AsSpan((int)offset, n));
                offset += n;
            }
        }

        _blocks.Clear();
        return result;
    }
}
