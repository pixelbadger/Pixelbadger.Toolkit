namespace Pixelbadger.Toolkit.Services.Audio;

/// <summary>Live audio output taking 48 kHz s16 interleaved stereo.</summary>
public interface IAudioPlaybackSink : IAsyncDisposable
{
    /// <summary>Writes samples to the output; blocks on backpressure, which paces playback in real time.</summary>
    ValueTask WriteAsync(ReadOnlyMemory<short> interleavedStereo, CancellationToken cancellationToken);

    /// <summary>Signals end of audio and waits for the output to drain.</summary>
    Task CompleteAsync(CancellationToken cancellationToken);
}

public interface IAudioPlaybackSinkFactory
{
    IAudioPlaybackSink Open(AudioPlaybackOptions options);
}
