using Pixelbadger.Toolkit.Services.Audio;

namespace Pixelbadger.Toolkit.Components;

public sealed record MidiPlayOptions(string File, string SoundFont, string? OutputFormat, string? Device);

public sealed class MidiPlayComponent
{
    private const int BlockFrames = 1024;
    private const int SampleRate = 48000;
    private const int CancelledExitCode = 130;

    private readonly IMidiSynthSourceFactory _synthFactory;
    private readonly IAudioPlaybackSinkFactory _sinkFactory;
    private readonly IMidiPlaybackView _view;

    public MidiPlayComponent(IMidiSynthSourceFactory synthFactory, IAudioPlaybackSinkFactory sinkFactory, IMidiPlaybackView view)
    {
        _synthFactory = synthFactory ?? throw new ArgumentNullException(nameof(synthFactory));
        _sinkFactory = sinkFactory ?? throw new ArgumentNullException(nameof(sinkFactory));
        _view = view ?? throw new ArgumentNullException(nameof(view));
    }

    public async Task<int> RunAsync(MidiPlayOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        IMidiSynthSource? synth = null;
        IAudioPlaybackSink? sink = null;
        long frames = 0;
        try
        {
            synth = _synthFactory.Open(options.File, options.SoundFont);

            var defaults = AudioPlaybackDefaults.ForCurrentPlatform();
            var playbackOptions = new AudioPlaybackOptions(options.OutputFormat ?? defaults.Format, options.Device ?? defaults.Device);

            try
            {
                sink = _sinkFactory.Open(playbackOptions);
                _view.Start(new MidiPlaybackInfo(options.File, options.SoundFont, synth.Length));

                var left = new float[BlockFrames];
                var right = new float[BlockFrames];
                var pcm = new short[BlockFrames * 2];
                int rendered;
                while ((rendered = synth.Render(left, right)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Interleave(left.AsSpan(0, rendered), right.AsSpan(0, rendered), pcm);
                    await sink.WriteAsync(pcm.AsMemory(0, rendered * 2), cancellationToken).AsTask()
                        .WaitAsync(cancellationToken).ConfigureAwait(false);
                    frames += rendered;
                    _view.Update(left.AsSpan(0, rendered), right.AsSpan(0, rendered), ElapsedFor(frames));
                }

                await sink.CompleteAsync(cancellationToken).ConfigureAwait(false);
                _view.Finish(ElapsedFor(frames));
                return 0;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Killing ffmpeg first unblocks any pipe write still in flight.
                var toDispose = sink;
                sink = null;
                if (toDispose is not null)
                    await toDispose.DisposeAsync().ConfigureAwait(false);
                _view.Stop(ElapsedFor(frames));
                return CancelledExitCode;
            }
        }
        finally
        {
            if (sink is not null)
                await sink.DisposeAsync().ConfigureAwait(false);
            synth?.Dispose();
            _view.Dispose();
        }
    }

    private static TimeSpan ElapsedFor(long frames) =>
        TimeSpan.FromTicks(frames * TimeSpan.TicksPerSecond / SampleRate);

    private static void Interleave(ReadOnlySpan<float> left, ReadOnlySpan<float> right, Span<short> destination)
    {
        for (var i = 0; i < left.Length; i++)
        {
            destination[i * 2] = ToSample(left[i]);
            destination[i * 2 + 1] = ToSample(right[i]);
        }
    }

    internal static short ToSample(float value)
    {
        if (float.IsNaN(value))
            return 0;
        var clamped = Math.Clamp(value, -1f, 1f);
        return (short)Math.Round(clamped * 32767.0, MidpointRounding.AwayFromZero);
    }
}
