namespace Pixelbadger.Toolkit.Services.Audio;

/// <summary>What is being played: the paths exactly as given on the command line, and the MIDI file's length.</summary>
public sealed record MidiPlaybackInfo(string MidiPath, string SoundFontPath, TimeSpan Length);

/// <summary>Shows MIDI playback progress. Called from a single playback thread, in order: Start, Update*, then Finish or Stop, then Dispose.</summary>
public interface IMidiPlaybackView : IDisposable
{
    /// <summary>Playback is about to begin; the output device has been opened.</summary>
    void Start(MidiPlaybackInfo info);

    /// <summary>A block of 48 kHz stereo audio (left and right, equal lengths, nominally -1..1) was handed to the output; elapsed is total frames handed over ÷ 48 000. The view must copy what it needs before returning.</summary>
    void Update(ReadOnlySpan<float> left, ReadOnlySpan<float> right, TimeSpan elapsed);

    /// <summary>Playback completed normally and the output has drained.</summary>
    void Finish(TimeSpan elapsed);

    /// <summary>Playback was cancelled (Ctrl+C).</summary>
    void Stop(TimeSpan elapsed);
}
