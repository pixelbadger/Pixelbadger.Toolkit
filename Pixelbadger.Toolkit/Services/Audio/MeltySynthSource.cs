using MeltySynth;

namespace Pixelbadger.Toolkit.Services.Audio;

public sealed class MeltySynthSourceFactory : IMidiSynthSourceFactory
{
    public IMidiSynthSource Open(string midiPath, string soundFontPath)
    {
        ArgumentNullException.ThrowIfNull(midiPath);
        ArgumentNullException.ThrowIfNull(soundFontPath);

        if (!File.Exists(midiPath))
            throw new FileNotFoundException($"File '{midiPath}' not found");
        if (!File.Exists(soundFontPath))
            throw new FileNotFoundException($"SoundFont '{soundFontPath}' not found");

        MidiFile midi;
        try
        {
            midi = new MidiFile(midiPath);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new InvalidOperationException($"Could not read MIDI file '{midiPath}': {ex.Message}", ex);
        }

        Synthesizer synth;
        try
        {
            var soundFont = new SoundFont(soundFontPath);
            synth = new Synthesizer(soundFont, new SynthesizerSettings(MeltySynthSource.SampleRate));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new InvalidOperationException($"Could not read SoundFont '{soundFontPath}': {ex.Message}", ex);
        }

        return new MeltySynthSource(midi, synth);
    }
}

/// <summary>MeltySynth-backed renderer. Not thread-safe.</summary>
public sealed class MeltySynthSource : IMidiSynthSource
{
    public const int SampleRate = 48000;
    private const int MaxTailSeconds = 3;

    private readonly Synthesizer _synth;
    private readonly MidiFileSequencer _sequencer;
    private readonly long _lengthFrames;
    private readonly long _capFrames;
    private long _rendered;
    private bool _finished;

    internal MeltySynthSource(MidiFile midi, Synthesizer synth)
    {
        _synth = synth;
        _sequencer = new MidiFileSequencer(synth);
        _sequencer.Play(midi, false);
        Length = midi.Length;
        _lengthFrames = (long)Math.Ceiling(midi.Length.TotalSeconds * SampleRate);
        _capFrames = _lengthFrames + (long)MaxTailSeconds * SampleRate;
    }

    public TimeSpan Length { get; }

    public int Render(Span<float> left, Span<float> right)
    {
        if (left.Length != right.Length)
            throw new ArgumentException("Left and right buffers must be the same length.", nameof(right));
        if (_finished)
            return 0;
        if (left.IsEmpty)
            return 0;

        long limit;
        if (_rendered < _lengthFrames)
        {
            limit = _lengthFrames;
        }
        else if (_synth.ActiveVoiceCount == 0 || _rendered >= _capFrames)
        {
            _finished = true;
            return 0;
        }
        else
        {
            limit = _capFrames;
        }

        var count = (int)Math.Min(left.Length, limit - _rendered);
        _sequencer.Render(left[..count], right[..count]);
        _rendered += count;
        return count;
    }

    public void Dispose()
    {
        _finished = true;
    }
}
