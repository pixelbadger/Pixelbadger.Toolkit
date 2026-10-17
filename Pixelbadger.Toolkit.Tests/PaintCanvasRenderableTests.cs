using FluentAssertions;
using Pixelbadger.Toolkit.Components;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Pixelbadger.Toolkit.Tests;

public class PaintCanvasRenderableTests
{
    private static readonly RenderOptions Options = RenderOptions.Create(AnsiConsole.Console);

    [Fact]
    public void Measure_ShouldReturnSizeColumns_WhenCalled()
    {
        var r = new PaintCanvasRenderable(new PixelColor[16], 4);
        var m = ((IRenderable)r).Measure(Options, 80);
        m.Min.Should().Be(4);
        m.Max.Should().Be(4);
    }

    [Fact]
    public void Render_ShouldEmitHalfBlocksWithTopAsForeground_WhenRendered()
    {
        var px = Enumerable.Repeat(PixelColor.White, 16).ToArray();
        px[0] = PixelColor.Red;            // top of cell (0,0)
        px[4] = new PixelColor(0, 0, 255); // bottom of cell (0,0)
        var segs = ((IRenderable)new PaintCanvasRenderable(px, 4)).Render(Options, 80).ToList();

        var blocks = segs.Where(s => !s.IsLineBreak).ToList();
        blocks.Should().HaveCount(8);
        blocks.Should().OnlyContain(s => s.Text == "▀");
        blocks[0].Style.Foreground.Should().Be(new Color(255, 0, 0));
        blocks[0].Style.Background.Should().Be(new Color(0, 0, 255));
        blocks[1].Style.Foreground.Should().Be(new Color(255, 255, 255));
        segs.Count(s => s.IsLineBreak).Should().Be(2);
    }
}
