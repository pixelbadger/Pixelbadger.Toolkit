using Pixelbadger.Toolkit.Services.Audio;

namespace Pixelbadger.Toolkit.Components;

/// <summary>Non-interactive playback view: one line at start and one at the end, nothing else.</summary>
public sealed class PlainMidiPlaybackView : IMidiPlaybackView
{
    private readonly TextWriter _output;
    private MidiPlaybackInfo? _info;

    public PlainMidiPlaybackView(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        _output = output;
    }

    public void Start(MidiPlaybackInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        _info = info;
        _output.WriteLine($"Playing '{info.MidiPath}' ({MidiPlaybackTime.Format(info.Length)}) with '{info.SoundFontPath}'");
    }

    public void Update(ReadOnlySpan<float> left, ReadOnlySpan<float> right, TimeSpan elapsed)
    {
    }

    public void Finish(TimeSpan elapsed)
        => _output.WriteLine($"Played '{_info?.MidiPath}' ({MidiPlaybackTime.Format(elapsed)})");

    public void Stop(TimeSpan elapsed)
        => _output.WriteLine($"Stopped at {MidiPlaybackTime.Format(elapsed)}");

    public void Dispose()
    {
    }
}
