using FluentAssertions;
using Pixelbadger.Toolkit.Components;

namespace Pixelbadger.Toolkit.Tests;

public class SpectrePaintRendererTests
{
    private static PixelColor[] White(int n) => Enumerable.Repeat(PixelColor.White, n * n).ToArray();
    private static int Count(string s, string needle) => (s.Length - s.Replace(needle, "").Length) / needle.Length;

    [Fact]
    public void NonInteractive_ShouldWriteOneLinePerToolCallAndFinalFrameOnce_WhenRun()
    {
        var w = new StringWriter();
        var console = PaintTestSupport.CreateConsole(w, width: 80, height: 40, interactive: false);
        using var r = new SpectrePaintRenderer(console, interactive: false);
        var px = White(4);

        r.Begin();
        r.RenderFrame(px, 4, "status");
        w.ToString().Should().BeEmpty();
        r.LogToolCall("fill_rect(0,0,4,4)");
        r.LogToolCall("draw_line(0,0,3,3)");
        r.RenderFrame(px, 4, "status");
        r.End(px, 4);
        r.End(px, 4);

        var output = w.ToString();
        output.Should().Contain("fill_rect(0,0,4,4)\n").And.Contain("draw_line(0,0,3,3)\n");
        output.IndexOf("fill_rect").Should().BeLessThan(output.IndexOf("draw_line"));
        Count(output, "▀").Should().Be(8);
        output.IndexOf("draw_line").Should().BeLessThan(output.IndexOf("▀"));
    }

    [Fact]
    public void LogToolCall_ShouldTreatBracketsLiterally_WhenDescriptionHasMarkup()
    {
        var w = new StringWriter();
        var console = PaintTestSupport.CreateConsole(w, width: 80, height: 40, interactive: false);
        using var r = new SpectrePaintRenderer(console, false);
        r.LogToolCall("set_pixels([red]x[/]) [bold");
        w.ToString().Should().Contain("set_pixels([red]x[/]) [bold");
    }

    [Fact]
    public void Interactive_ShouldDrawEachFrameAndNotDuplicateOnEnd_WhenFramesRendered()
    {
        var w = new StringWriter();
        var console = PaintTestSupport.CreateConsole(w, width: 12, height: 4);
        using var r = new SpectrePaintRenderer(console, interactive: true);
        var px = White(4);

        r.Begin();
        r.RenderFrame(px, 4, "Step 1/3 [x]");
        Count(w.ToString(), "▀").Should().Be(8);
        w.ToString().Should().Contain("Step 1/3 [x]");
        r.RenderFrame(px, 4, "Step 2/3");
        Count(w.ToString(), "▀").Should().Be(16);
        r.End(px, 4);
        Count(w.ToString(), "▀").Should().Be(16);
    }

    [Fact]
    public void Interactive_ShouldPrintFinalFrameOnce_WhenNoFrameRendered()
    {
        var w = new StringWriter();
        var console = PaintTestSupport.CreateConsole(w, width: 4, height: 4);
        using var r = new SpectrePaintRenderer(console, interactive: true);
        r.End(White(4), 4);
        r.End(White(4), 4);
        Count(w.ToString(), "▀").Should().Be(8);
    }

    [Fact]
    public void Interactive_ShouldDownscaleToGrid_WhenConsoleIsSmallerThanCanvas()
    {
        var w = new StringWriter();
        var console = PaintTestSupport.CreateConsole(w, width: 2, height: 10);
        using var r = new SpectrePaintRenderer(console, interactive: true);
        r.RenderFrame(White(4), 4, "s");
        Count(w.ToString(), "▀").Should().Be(2);
    }
}
