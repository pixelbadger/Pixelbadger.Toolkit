namespace Pixelbadger.Toolkit.Services.Audio;

/// <summary>Where the new audio comes from. <see cref="Channels"/> is the channel count of the returned PCM.</summary>
public sealed record AudioInputRequest(int Channels, TimeSpan? Duration, string? FromWav, string? Device, string? CaptureFormat);

public interface IAudioInputCollector
{
    /// <summary>Checks that the audio source is usable (WAV exists, ffmpeg present, device given, a keypress is possible) without starting it.</summary>
    void Validate(AudioInputRequest request);

    /// <summary>
    /// Produces 48 kHz interleaved PCM at <see cref="AudioInputRequest.Channels"/>. Without a duration, a stop by Enter or by
    /// cancelling <paramref name="cancellationToken"/> ends a live capture normally and returns what was captured. With a duration
    /// (or a WAV), cancelling aborts: <see cref="OperationCanceledException"/> is thrown and nothing is returned.
    /// </summary>
    Task<short[]> CollectAsync(AudioInputRequest request, CancellationToken cancellationToken);
}
