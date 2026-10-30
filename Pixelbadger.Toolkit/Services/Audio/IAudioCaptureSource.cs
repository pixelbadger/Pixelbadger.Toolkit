namespace Pixelbadger.Toolkit.Services.Audio;

public interface IAudioCaptureSource : IAsyncDisposable
{
    /// <summary>Always 48000.</summary>
    int SampleRate { get; }

    int Channels { get; }

    /// <summary>Reads interleaved samples into <paramref name="buffer"/>. Returns the number read; 0 means end of stream.</summary>
    ValueTask<int> ReadAsync(Memory<short> buffer, CancellationToken cancellationToken);
}
