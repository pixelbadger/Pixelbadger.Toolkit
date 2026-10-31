namespace Pixelbadger.Toolkit.Services.Audio;

/// <summary>A source of rendered MIDI audio at 48 000 Hz stereo.</summary>
public interface IMidiSynthSource : IDisposable
{
    /// <summary>Length of the MIDI sequence (excluding the release tail).</summary>
    TimeSpan Length { get; }

    /// <summary>
    /// Fills up to <c>left.Length</c> frames (left and right must be equal length) and returns the number written.
    /// Returns 0 once finished: the sequence has reached <see cref="Length"/> and either no voices remain active
    /// or 3 s past the end have been rendered.
    /// </summary>
    int Render(Span<float> left, Span<float> right);
}

public interface IMidiSynthSourceFactory
{
    /// <summary>Loads both files up front. Throws with a user-facing message when either is missing or unreadable.</summary>
    IMidiSynthSource Open(string midiPath, string soundFontPath);
}
