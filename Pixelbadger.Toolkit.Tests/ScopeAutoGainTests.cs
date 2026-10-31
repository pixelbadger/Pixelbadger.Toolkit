using FluentAssertions;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Services.Audio;
using Spectre.Console;

namespace Pixelbadger.Toolkit.Tests;

public class ScopeAutoGainTests
{
    private const int Block = 1024;
    private static readonly MidiPlaybackInfo Info = new("a.mid", "b.sf2", TimeSpan.FromSeconds(10));

    private static LiveMidiPlaybackView NewView() =>
        new(AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(new StringWriter()) }));

    private static void Feed(LiveMidiPlaybackView view, double amplitude, int totalFrames, ref long position)
    {
        var buf = new float[Block];
        for (var done = 0; done < totalFrames; done += Block)
        {
            for (var i = 0; i < Block; i++)
                buf[i] = (float)(amplitude * Math.Sin(2 * Math.PI * 440 * (position + i) / 48000.0));
            position += Block;
            view.Update(buf, buf, TimeSpan.FromSeconds(position / 48000.0));
        }
    }

    private static (int Min, int Max) LitDotRows(OscilloscopeFrame frame)
    {
        int min = int.MaxValue, max = -1;
        for (var r = 0; r < frame.Height; r++)
        {
            foreach (var ch in frame.Rows[r])
            {
                var bits = ch - 0x2800;
                if (bits == 0) continue;
                for (var dy = 0; dy < 4; dy++)
                {
                    // Row bits: dy0 = 0x01|0x08, dy1 = 0x02|0x10, dy2 = 0x04|0x20, dy3 = 0x40|0x80
                    var mask = dy switch { 0 => 0x09, 1 => 0x12, 2 => 0x24, _ => 0xC0 };
                    if ((bits & mask) == 0) continue;
                    min = Math.Min(min, r * 4 + dy);
                    max = Math.Max(max, r * 4 + dy);
                }
            }
        }

        return (min, max);
    }

    [Fact]
    public void CurrentFrame_ShouldSpanAtLeast75PercentOfDotRows_WhenQuietSineFedForOverOneSecond()
    {
        using var view = NewView();
        long pos = 0;
        Feed(view, 0.05, 60 * Block, ref pos); // ~1.28 s
        var (min, max) = LitDotRows(view.CurrentFrame(96, 8));
        (max - min + 1).Should().BeGreaterThanOrEqualTo(24);
        view.CurrentGain.Should().BeApproximately(18f, 0.5f);
    }

    [Fact]
    public void Render_ShouldNotSpan75PercentOfDotRows_WhenQuietSineIsDrawnWithoutGain()
    {
        var samples = Enumerable.Range(0, 960).Select(i => (float)(0.05 * Math.Sin(2 * Math.PI * 440 * i / 48000.0))).ToArray();
        var (min, max) = LitDotRows(OscilloscopeRenderer.Render(samples, samples, 96, 8));
        (max - min + 1).Should().BeLessThan(24);
    }

    [Fact]
    public void CurrentFrame_ShouldBeFlatCentreLine_WhenSilent()
    {
        using var view = NewView();
        long pos = 0;
        Feed(view, 0.0, 60 * Block, ref pos);
        view.CurrentGain.Should().Be(1f);
        var (min, max) = LitDotRows(view.CurrentFrame(96, 8));
        min.Should().Be(max);
        min.Should().BeInRange(15, 16);
    }

    [Fact]
    public void CurrentGain_ShouldBeExactlyOne_WhenFullScaleSine()
    {
        using var view = NewView();
        long pos = 0;
        Feed(view, 1.0, 60 * Block, ref pos);
        view.CurrentGain.Should().Be(1f);
    }

    [Fact]
    public void CurrentGain_ShouldRiseAgain_WhenQuietPassageFollowsLoudOne()
    {
        using var view = NewView();
        long pos = 0;
        Feed(view, 1.0, 10 * Block, ref pos);
        view.CurrentGain.Should().Be(1f);
        Feed(view, 0.05, 20 * Block, ref pos); // ~0.43 s
        var early = view.CurrentGain;
        Feed(view, 0.05, 100 * Block, ref pos); // ~2.1 s more: envelope 1.0 -> ~0.08 (e-fold per second)
        var mid = view.CurrentGain;
        mid.Should().BeGreaterThan(early);
        mid.Should().BeGreaterThan(10f);
        Feed(view, 0.05, 100 * Block, ref pos); // ~4.5 s after the loud passage: back at full gain
        view.CurrentGain.Should().BeApproximately(18f, 0.5f);
    }

    [Theory]
    [InlineData(0.0001)]
    [InlineData(0.00011)]
    [InlineData(0.001)]
    public void Gain_ShouldNeverExceedTwenty(double peak)
    {
        var agc = new ScopeAutoGain();
        agc.Observe((float)peak, Block);
        agc.Gain.Should().BeLessThanOrEqualTo(20f);
    }

    [Fact]
    public void Gain_ShouldBeOne_WhenEnvelopeBelowSilenceFloor()
    {
        var agc = new ScopeAutoGain();
        agc.Observe(0.00005f, Block);
        agc.Gain.Should().Be(1f);
    }

    [Fact]
    public void Observe_ShouldDecayEnvelopeByFactorE_OverOneSecondOfFrames()
    {
        var agc = new ScopeAutoGain();
        agc.Observe(1f, Block);
        agc.Observe(0f, 48000);
        agc.Envelope.Should().BeApproximately(Math.Exp(-1), 1e-6);
    }

    [Fact]
    public void Apply_ShouldClampToUnitRange()
    {
        ScopeAutoGain.Apply(new[] { 0.5f, -0.5f, 0.01f }, 4f).Should().Equal(1f, -1f, 0.04f);
    }
}
