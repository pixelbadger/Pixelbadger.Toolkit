using FluentAssertions;
using Pixelbadger.Toolkit.Services.Audio;

namespace Pixelbadger.Toolkit.Tests;

public class MeltySynthSourceTests : IDisposable
{
    private readonly MidiTestAssets.TempDirectory _dir = new();
    private readonly MeltySynthSourceFactory _factory = new();

    public void Dispose() => _dir.Dispose();

    private static (float[] Left, float[] Right) RenderAll(IMidiSynthSource source, int block = 1024)
    {
        var left = new List<float>();
        var right = new List<float>();
        var l = new float[block];
        var r = new float[block];
        int n;
        while ((n = source.Render(l, r)) > 0)
        {
            left.AddRange(l.Take(n));
            right.AddRange(r.Take(n));
        }

        return (left.ToArray(), right.ToArray());
    }

    private string Midi(params (int, double, double)[] notes) => MidiTestAssets.WriteMidi(_dir.Path, "song.mid", notes);

    private string Sf2(Waveform w, string name = "bank.sf2") => MidiTestAssets.WriteSoundFont(_dir.Path, name, w);

    [Fact]
    public void Length_ShouldMatchMidi_WhenOpened()
    {
        using var source = _factory.Open(Midi((60, 0, 1.0), (64, 0.5, 1.5)), Sf2(Waveform.Sine));

        source.Length.TotalSeconds.Should().BeApproximately(2.0, 0.01);
    }

    [Fact]
    public void Render_ShouldProduceNonSilentAudio_WhenNotePlays()
    {
        using var source = _factory.Open(Midi((69, 0, 1.0)), Sf2(Waveform.Sine));
        var l = new float[4800];
        var r = new float[4800];

        source.Render(l, r).Should().Be(4800);

        l.Max(Math.Abs).Should().BeGreaterThan(0.01f);
        r.Max(Math.Abs).Should().BeGreaterThan(0.01f);
    }

    [Fact]
    public void Render_ShouldRenderLengthPlusTail_WhenSampleHasRelease()
    {
        using var source = _factory.Open(Midi((69, 0, 1.0)), Sf2(Waveform.Sine));

        var (left, right) = RenderAll(source);

        left.Length.Should().Be(right.Length);
        var lengthFrames = (int)Math.Ceiling(source.Length.TotalSeconds * 48000);
        left.Length.Should().BeGreaterThan(lengthFrames);
        left.Length.Should().BeLessThanOrEqualTo(lengthFrames + 3 * 48000);
        // The release tail is audible right after the last note-off.
        left.Skip(lengthFrames).Take(2400).Max(Math.Abs).Should().BeGreaterThan(0.001f);
    }

    [Fact]
    public void Render_ShouldStopBeforeCap_WhenVoicesGoSilent()
    {
        // The test SoundFont releases in ~0.5 s, so rendering must end well before the 3 s cap.
        using var source = _factory.Open(Midi((69, 0, 1.0)), Sf2(Waveform.Sine));

        var (left, _) = RenderAll(source);

        var lengthFrames = (int)Math.Ceiling(source.Length.TotalSeconds * 48000);
        left.Length.Should().BeGreaterThan(lengthFrames);
        left.Length.Should().BeLessThan(lengthFrames + 2 * 48000);
    }

    [Fact]
    public void Render_ShouldStopAtLength_WhenNoNotesPlay()
    {
        using var source = _factory.Open(Midi(), Sf2(Waveform.Sine));

        var (left, _) = RenderAll(source);

        left.Length.Should().BeLessThanOrEqualTo((int)Math.Ceiling(source.Length.TotalSeconds * 48000));
    }

    [Fact]
    public void Render_ShouldCapTailAtThreeSeconds_WhenVoicesNeverEnd()
    {
        // A note that is never released keeps its voice active past the end of the sequence.
        var path = _dir.Combine("hold.mid");
        var bytes = MidiTestAssets.Midi((69, 0, 1.0));
        // Replace the note-off (0x80) with a second note-on of velocity 0 on another key so the voice stays held.
        var idx = Array.IndexOf(bytes, (byte)0x80);
        bytes[idx] = 0x90;
        bytes[idx + 2] = 0;
        bytes[idx + 1] = 71;
        File.WriteAllBytes(path, bytes);
        using var source = _factory.Open(path, Sf2(Waveform.Sine));

        var (left, _) = RenderAll(source);

        var lengthFrames = (int)Math.Ceiling(source.Length.TotalSeconds * 48000);
        left.Length.Should().Be(lengthFrames + 3 * 48000);
    }

    [Fact]
    public void Render_ShouldReturnZero_WhenFinished()
    {
        using var source = _factory.Open(Midi((69, 0, 0.1)), Sf2(Waveform.Sine));
        RenderAll(source);

        source.Render(new float[64], new float[64]).Should().Be(0);
        source.Render(new float[64], new float[64]).Should().Be(0);
    }

    [Fact]
    public void Render_ShouldFinishWithinOneBlockOfSameTail_WhenBlockSizeDiffers()
    {
        var midi = Midi((69, 0, 0.5));
        var sf2 = Sf2(Waveform.Sine);
        using var a = _factory.Open(midi, sf2);
        using var b = _factory.Open(midi, sf2);

        var small = RenderAll(a, 333).Left.Length;
        var large = RenderAll(b, 1024).Left.Length;

        Math.Abs(small - large).Should().BeLessThan(1024);
    }

    [Fact]
    public void Render_ShouldThrow_WhenChannelLengthsDiffer()
    {
        using var source = _factory.Open(Midi((69, 0, 0.1)), Sf2(Waveform.Sine));

        var act = () => source.Render(new float[8], new float[4]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Render_ShouldProduceDifferentAudio_WhenSoundFontDiffers()
    {
        var midi = Midi((69, 0, 0.5));
        using var sine = _factory.Open(midi, Sf2(Waveform.Sine, "sine.sf2"));
        using var square = _factory.Open(midi, Sf2(Waveform.Square, "square.sf2"));
        var ls = new float[9600];
        var rs = new float[9600];
        var lq = new float[9600];
        var rq = new float[9600];

        sine.Render(ls, rs);
        square.Render(lq, rq);

        ls.Should().NotEqual(lq);
        ls.Max(Math.Abs).Should().BeGreaterThan(0.01f);
        lq.Max(Math.Abs).Should().BeGreaterThan(0.01f);
    }

    [Fact]
    public void Render_ShouldBeDeterministic_WhenRenderedTwice()
    {
        var midi = Midi((69, 0, 0.5));
        var sf2 = Sf2(Waveform.Sine);
        using var a = _factory.Open(midi, sf2);
        using var b = _factory.Open(midi, sf2);

        RenderAll(a).Left.Should().Equal(RenderAll(b).Left);
    }

    [Fact]
    public void Open_ShouldThrowFileNotFound_WhenMidiMissing()
    {
        var path = _dir.Combine("nope.mid");

        var act = () => _factory.Open(path, Sf2(Waveform.Sine));

        act.Should().Throw<FileNotFoundException>().WithMessage($"File '{path}' not found");
    }

    [Fact]
    public void Open_ShouldThrowFileNotFound_WhenSoundFontMissing()
    {
        var path = _dir.Combine("nope.sf2");

        var act = () => _factory.Open(Midi((60, 0, 1)), path);

        act.Should().Throw<FileNotFoundException>().WithMessage($"SoundFont '{path}' not found");
    }

    [Fact]
    public void Open_ShouldReportMidiFirst_WhenBothMissing()
    {
        var act = () => _factory.Open(_dir.Combine("a.mid"), _dir.Combine("b.sf2"));

        act.Should().Throw<FileNotFoundException>().WithMessage("File '*a.mid' not found");
    }

    [Fact]
    public void Open_ShouldCheckSoundFontExistsBeforeParsingMidi_WhenMidiIsGarbage()
    {
        var midi = _dir.Combine("bad.mid");
        File.WriteAllText(midi, "not midi");

        var act = () => _factory.Open(midi, _dir.Combine("nope.sf2"));

        act.Should().Throw<FileNotFoundException>().WithMessage("SoundFont '*nope.sf2' not found");
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("empty")]
    [InlineData("truncated")]
    public void Open_ShouldThrowInvalidOperation_WhenMidiUnreadable(string kind)
    {
        var midi = _dir.Combine("bad.mid");
        var good = MidiTestAssets.Midi((60, 0, 1));
        File.WriteAllBytes(midi, kind switch
        {
            "garbage" => System.Text.Encoding.ASCII.GetBytes("this is definitely not a midi file"),
            "empty" => Array.Empty<byte>(),
            _ => good[..(good.Length - 12)]
        });

        var act = () => _factory.Open(midi, Sf2(Waveform.Sine));

        var ex = act.Should().Throw<InvalidOperationException>().Which;
        ex.Message.Should().StartWith($"Could not read MIDI file '{midi}': ");
        ex.Message.Length.Should().BeGreaterThan($"Could not read MIDI file '{midi}': ".Length);
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("empty")]
    [InlineData("truncated")]
    public void Open_ShouldThrowInvalidOperation_WhenSoundFontUnreadable(string kind)
    {
        var sf2 = _dir.Combine("bad.sf2");
        var good = MidiTestAssets.SoundFont(Waveform.Sine);
        File.WriteAllBytes(sf2, kind switch
        {
            "garbage" => System.Text.Encoding.ASCII.GetBytes("this is definitely not a soundfont"),
            "empty" => Array.Empty<byte>(),
            _ => good[..(good.Length / 2)]
        });

        var act = () => _factory.Open(Midi((60, 0, 1)), sf2);

        var ex = act.Should().Throw<InvalidOperationException>().Which;
        ex.Message.Should().StartWith($"Could not read SoundFont '{sf2}': ");
        ex.Message.Length.Should().BeGreaterThan($"Could not read SoundFont '{sf2}': ".Length);
    }
}
