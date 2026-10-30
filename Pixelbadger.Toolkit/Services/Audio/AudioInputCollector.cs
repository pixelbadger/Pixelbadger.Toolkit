using Pixelbadger.Toolkit.CommandLine;
using Pixelbadger.Toolkit.Components;

namespace Pixelbadger.Toolkit.Services.Audio;

public sealed class AudioInputCollector : IAudioInputCollector
{
    private const int SampleRate = 48000;
    private const int ChunkFrames = 960;
    private static readonly TimeSpan StopRaceWindow = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(25);

    private readonly ITerminalInput _input;
    private readonly TextWriter? _output;
    private readonly Func<FfmpegCaptureOptions, IAudioCaptureSource> _sourceFactory;
    private readonly Func<string?> _ffmpegLocator;
    private readonly Func<bool> _isNonInteractive;
    private readonly Func<(string Format, string? Device)> _defaults;

    public AudioInputCollector(
        ITerminalInput input,
        TextWriter? output = null,
        Func<FfmpegCaptureOptions, IAudioCaptureSource>? sourceFactory = null,
        Func<string?>? ffmpegLocator = null,
        Func<bool>? isNonInteractive = null,
        Func<(string Format, string? Device)>? defaults = null)
    {
        ArgumentNullException.ThrowIfNull(input);
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

        if (request.FromWav is not null)
            return ReadWav(request, maxFrames);

        var ffmpeg = _ffmpegLocator()!;
        var defaults = _defaults();
        var options = new FfmpegCaptureOptions(
            ffmpeg,
            request.CaptureFormat ?? defaults.Format,
            request.Device ?? defaults.Device!,
            request.Channels);

        if (request.Duration is null)
            (_output ?? ToolkitConsole.Out).WriteLine("Recording... press Enter or Ctrl+C to stop");

        return await CaptureAsync(options, request.Channels, maxFrames, request.Duration is null, cancellationToken);
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

    private async Task<short[]> CaptureAsync(FfmpegCaptureOptions options, int channels, long maxFrames, bool waitForEnter, CancellationToken cancellationToken)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var maxSamples = maxFrames == long.MaxValue ? long.MaxValue : maxFrames * channels;
        var samples = new List<short>();
        var buffer = new short[ChunkFrames * channels];
        Task? poller = null;
        var source = _sourceFactory(options);
        try
        {
            if (waitForEnter)
                poller = Task.Run(() => PollForEnterAsync(stop));

            while (samples.Count < maxSamples)
            {
                var want = (int)Math.Min(buffer.Length, maxSamples - samples.Count);
                int read;
                try
                {
                    read = await source.ReadAsync(buffer.AsMemory(0, want), stop.Token);
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested)
                {
                    break;
                }
                catch (AudioCaptureException) when (stop.Token.WaitHandle.WaitOne(StopRaceWindow))
                {
                    // Ctrl+C reaches ffmpeg as well, so it can end before our own stop request is observed.
                    break;
                }

                if (read == 0)
                    break;
                for (var i = 0; i < read; i++)
                    samples.Add(buffer[i]);
            }
        }
        finally
        {
            stop.Cancel();
            if (poller is not null)
                await poller;
            await source.DisposeAsync();
        }

        var whole = samples.Count - samples.Count % channels;
        return samples.GetRange(0, whole).ToArray();
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
