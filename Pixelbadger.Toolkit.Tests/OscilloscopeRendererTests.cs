using FluentAssertions;
using Pixelbadger.Toolkit.Components;

namespace Pixelbadger.Toolkit.Tests;

public class OscilloscopeRendererTests
{
    private static float[] Sine(int n, double cycles, double amplitude = 1.0)
        => Enumerable.Range(0, n).Select(i => (float)(amplitude * Math.Sin(2 * Math.PI * cycles * i / n))).ToArray();

    // Lit dots per dot row, from the braille characters.
    private static bool[,] Dots(OscilloscopeFrame f)
    {
        var dots = new bool[f.Height * 4, f.Width * 2];
        int[,] bits = { { 0x01, 0x02, 0x04, 0x40 }, { 0x08, 0x10, 0x20, 0x80 } };
        for (var r = 0; r < f.Height; r++)
            for (var c = 0; c < f.Width; c++)
            {
                var v = f.Rows[r][c] - 0x2800;
                for (var x = 0; x < 2; x++)
                    for (var y = 0; y < 4; y++)
                        dots[r * 4 + y, c * 2 + x] = (v & bits[x, y]) != 0;
            }

        return dots;
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(10, 3)]
    [InlineData(96, 8)]
    public void Render_ShouldProduceExactDimensions_WhenGivenSize(int width, int height)
    {
        var f = OscilloscopeRenderer.Render(Sine(960, 3), Sine(960, 5), width, height);

        f.Rows.Should().HaveCount(height);
        f.Rows.Should().OnlyContain(r => r.Length == width);
        f.CellChannels.GetLength(0).Should().Be(height);
        f.CellChannels.GetLength(1).Should().Be(width);
        f.Rows.SelectMany(r => r).Should().OnlyContain(ch => ch >= '⠀' && ch <= '⣿');
    }

    [Fact]
    public void Render_ShouldDrawFlatCentreLine_WhenInputIsSilent()
    {
        var silence = new float[960];
        var f = OscilloscopeRenderer.Render(silence, silence, 20, 8);

        var dots = Dots(f);
        var lit = Enumerable.Range(0, dots.GetLength(0))
            .Where(y => Enumerable.Range(0, dots.GetLength(1)).Any(x => dots[y, x])).ToList();
        lit.Should().ContainSingle().Which.Should().Be(16); // round(0.5 * 31)
        Enumerable.Range(0, dots.GetLength(1)).Should().OnlyContain(x => dots[16, x]);
    }

    [Fact]
    public void Render_ShouldDrawFlatCentreLine_WhenSpansAreEmpty()
    {
        var f = OscilloscopeRenderer.Render(ReadOnlySpan<float>.Empty, ReadOnlySpan<float>.Empty, 12, 4);

        var dots = Dots(f);
        Enumerable.Range(0, 24).Should().OnlyContain(x => dots[8, x]); // round(0.5 * 15)
        f.CellChannels[2, 5].Should().Be(ScopeChannels.Both);
    }

    [Fact]
    public void Render_ShouldSpanTopToBottom_WhenFullScaleSine()
    {
        var s = Sine(960, 2);
        var f = OscilloscopeRenderer.Render(s, s, 48, 8);

        var dots = Dots(f);
        Enumerable.Range(0, 96).Any(x => dots[0, x]).Should().BeTrue("top dot row is lit");
        Enumerable.Range(0, 96).Any(x => dots[31, x]).Should().BeTrue("bottom dot row is lit");
    }

    [Fact]
    public void Render_ShouldKeepTraceContinuous_WhenSineHasNoGaps()
    {
        var s = Sine(960, 3);
        var f = OscilloscopeRenderer.Render(s, new float[960], 60, 8);

        var dots = Dots(f);
        // Adjacent columns must touch or overlap vertically.
        for (var x = 0; x < 119; x++)
        {
            var a = Enumerable.Range(0, 32).Where(y => dots[y, x]).ToList();
            var b = Enumerable.Range(0, 32).Where(y => dots[y, x + 1]).ToList();
            a.Should().NotBeEmpty();
            b.Should().NotBeEmpty();
            (a.Min() <= b.Max() && b.Min() <= a.Max()).Should().BeTrue($"columns {x} and {x + 1} must connect");
        }
    }

    [Fact]
    public void Render_ShouldFlagChannels_WhenOnlyOneChannelCarriesSignal()
    {
        var up = Enumerable.Repeat(1f, 100).ToArray();
        var down = Enumerable.Repeat(-1f, 100).ToArray();
        var f = OscilloscopeRenderer.Render(up, down, 10, 2);

        f.CellChannels[0, 3].Should().Be(ScopeChannels.Left);
        f.CellChannels[1, 3].Should().Be(ScopeChannels.Right);
        f.Rows[0][3].Should().Be('⠉'); // top dot row, both columns
        f.Rows[1][3].Should().Be('\u28C0'); // bottom dot row, both columns
    }

    [Fact]
    public void Render_ShouldFlagBoth_WhenChannelsOverlap()
    {
        var s = Enumerable.Repeat(1f, 100).ToArray();
        var f = OscilloscopeRenderer.Render(s, s, 10, 2);

        f.CellChannels[0, 0].Should().Be(ScopeChannels.Both);
        f.CellChannels[1, 0].Should().Be(ScopeChannels.None);
        f.Rows[1][0].Should().Be('⠀');
    }

    [Fact]
    public void Render_ShouldClampAndSurviveBadSamples_WhenOutOfRange()
    {
        var wild = new[] { 5f, -5f, float.NaN, float.PositiveInfinity, float.NegativeInfinity };
        var f = OscilloscopeRenderer.Render(wild, wild, 8, 2);

        f.Rows.Should().HaveCount(2);
        var dots = Dots(f);
        Enumerable.Range(0, 16).Any(x => dots[0, x]).Should().BeTrue();
        Enumerable.Range(0, 16).Any(x => dots[7, x]).Should().BeTrue();
    }

    [Fact]
    public void Render_ShouldHandleSingleSample_WhenInputHasOneFrame()
    {
        var f = OscilloscopeRenderer.Render(new[] { 1f }, new[] { -1f }, 4, 1);

        f.Rows[0].Should().HaveLength(4);
        f.CellChannels[0, 0].Should().Be(ScopeChannels.Both);
    }

    [Theory]
    [InlineData(0, 8)]
    [InlineData(-1, 8)]
    [InlineData(10, 0)]
    [InlineData(10, -3)]
    public void Render_ShouldThrow_WhenSizeIsNotPositive(int width, int height)
    {
        var act = () => OscilloscopeRenderer.Render(new float[4], new float[4], width, height);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
