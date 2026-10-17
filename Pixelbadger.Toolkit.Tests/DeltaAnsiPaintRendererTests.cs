using FluentAssertions;
using Pixelbadger.Toolkit.Components;
using Spectre.Console;

namespace Pixelbadger.Toolkit.Tests;

public class DeltaAnsiPaintRendererTests : IDisposable
{
    private const string E = "\u001b";
    private const string Begin = E + "[?1049h" + E + "[?25l" + E + "[?7l" + E + "[2J" + E + "[H";
    private const string Restore = E + "[0m" + E + "[?7h" + E + "[?25h" + E + "[?1049l";
    private const string SyncOn = E + "[?2026h";
    private const string SyncOff = E + "[?2026l";
    private const string White = E + "[48;2;255;255;255m";
    private const string Status = E + "[3;1H" + E + "[0m    ";

    private readonly StringWriter _w = new();
    private readonly IAnsiConsole _console;
    private readonly DeltaAnsiPaintRenderer _r;

    public DeltaAnsiPaintRendererTests()
    {
        _console = PaintTestSupport.CreateConsole(_w, width: 4, height: 4);
        _r = new DeltaAnsiPaintRenderer(_console);
    }

    public void Dispose() => _r.Dispose();

    private static PixelColor[] Canvas() => Enumerable.Repeat(PixelColor.White, 16).ToArray();

    private string Take()
    {
        var s = _w.ToString();
        _w.GetStringBuilder().Clear();
        return s;
    }

    private string Delta(PixelColor[] px)
    {
        _r.RenderFrame(px, 4, "");
        return Take();
    }

    private void Prime()
    {
        _r.Begin();
        _r.RenderFrame(Canvas(), 4, "");
        Take();
    }

    [Fact]
    public void Begin_ShouldWriteAlternateScreenSequenceInOrder_WhenCalled()
    {
        _r.Begin();
        Take().Should().Be(Begin);
    }

    [Fact]
    public void RenderFrame_ShouldWriteExactFullFrame_WhenFirstFrame()
    {
        _r.Begin();
        Take();
        _r.RenderFrame(Canvas(), 4, "");
        var expectedCanvas = SyncOn + E + "[2J" + E + "[1;1H" + White + "    " + E + "[2;1H" + "    " + SyncOff;
        Take().Should().Be(expectedCanvas + Status);
        _r.LastFrameStats.Should().Be(new PaintFrameStats(8, System.Text.Encoding.UTF8.GetByteCount(expectedCanvas)));
    }

    [Fact]
    public void RenderFrame_ShouldEmitNothingForCanvas_WhenFrameIdentical()
    {
        Prime();
        var output = Delta(Canvas());
        output.Should().Be(Status);
        output.Should().NotContain(SyncOn);
        _r.LastFrameStats.Should().Be(new PaintFrameStats(0, 0));
    }

    [Fact]
    public void RenderFrame_ShouldReemitOnlyChangedCellWithCup_WhenOnePixelChanges()
    {
        Prime();
        var px = Canvas();
        px[1] = PixelColor.Red; // top of cell row 0, col 1
        var expected = SyncOn + E + "[1;2H" + E + "[38;2;255;0;0;48;2;255;255;255m▀" + SyncOff;
        Delta(px).Should().Be(expected + Status);
        _r.LastFrameStats.Should().Be(new PaintFrameStats(1, System.Text.Encoding.UTF8.GetByteCount(expected)));
    }

    [Fact]
    public void RenderFrame_ShouldShareOneCupAndElideSgr_WhenAdjacentCellsMatch()
    {
        Prime();
        var px = Canvas();
        px[1] = PixelColor.Red;
        px[2] = PixelColor.Red;
        Delta(px).Should().Be(SyncOn + E + "[1;2H" + E + "[38;2;255;0;0;48;2;255;255;255m▀▀" + SyncOff + Status);
        _r.LastFrameStats!.ChangedCells.Should().Be(2);
    }

    [Fact]
    public void RenderFrame_ShouldEmitOnlyChangedHalf_WhenAdjacentCellsDifferInForeground()
    {
        Prime();
        var px = Canvas();
        px[1] = PixelColor.Red;
        px[2] = new PixelColor(0, 0, 255);
        Delta(px).Should().Be(SyncOn + E + "[1;2H" + E + "[38;2;255;0;0;48;2;255;255;255m▀"
            + E + "[38;2;0;0;255m▀" + SyncOff + Status);
    }

    [Fact]
    public void RenderFrame_ShouldNotRepeatSgr_WhenSameColourContinuesAcrossRowBoundary()
    {
        Prime();
        var px = Canvas();
        px[3] = PixelColor.Red; // cell (0,3) top
        px[8] = PixelColor.Red; // cell (1,0) top
        Delta(px).Should().Be(SyncOn + E + "[1;4H" + E + "[38;2;255;0;0;48;2;255;255;255m▀"
            + E + "[2;1H▀" + SyncOff + Status);
    }

    [Fact]
    public void RenderFrame_ShouldWriteSpaceWithBackgroundOnly_WhenCellIsUniform()
    {
        Prime();
        var px = Canvas();
        px[0] = PixelColor.Black;
        px[4] = PixelColor.Black;
        Delta(px).Should().Be(SyncOn + E + "[1;1H" + E + "[48;2;0;0;0m " + SyncOff + Status);
    }

    [Fact]
    public void RenderFrame_ShouldWrapCanvasInSynchronizedUpdate_WhenCellsChange()
    {
        _r.Begin();
        Take();
        _r.RenderFrame(Canvas(), 4, "");
        var o = Take();
        o.Should().StartWith(SyncOn);
        o.IndexOf(SyncOff).Should().BeGreaterThan(o.IndexOf(SyncOn));
    }

    [Fact]
    public void RenderFrame_ShouldClearAndRepaintEverything_WhenConsoleResized()
    {
        Prime();
        _console.Profile.Width = 5;
        var o = Delta(Canvas());
        o.Should().Contain(E + "[2J");
        _r.LastFrameStats!.ChangedCells.Should().Be(8);
    }

    [Fact]
    public void RenderFrame_ShouldDownscaleToGrid_WhenConsoleSmallerThanCanvas()
    {
        var w = new StringWriter();
        var console = PaintTestSupport.CreateConsole(w, width: 100, height: 4); // D = 4
        var r = new DeltaAnsiPaintRenderer(console);
        r.Begin();
        r.RenderFrame(Enumerable.Repeat(PixelColor.White, 64).ToArray(), 8, "");
        r.LastFrameStats!.ChangedCells.Should().Be(8);
    }

    [Fact]
    public void RenderFrame_ShouldTruncateAndPadStatusToWidth_WhenStatusIsLong()
    {
        Prime();
        _r.RenderFrame(Canvas(), 4, "abcdefgh\n");
        Take().Should().Be(E + "[3;1H" + E + "[0m" + "abcd");
    }

    [Fact]
    public void LogToolCall_ShouldWriteNothing_WhenCalled()
    {
        _r.Begin();
        Take();
        _r.LogToolCall("fill_rect");
        Take().Should().BeEmpty();
    }

    [Fact]
    public void End_ShouldRestoreTerminalThenPrintFinalFrameOnce_WhenCalledTwice()
    {
        Prime();
        _r.End(Canvas(), 4);
        var o = Take();
        o.Should().StartWith(Restore);
        o.Substring(Restore.Length).Count(ch => ch == '▀').Should().Be(8);
        _r.End(Canvas(), 4);
        Take().Should().BeEmpty();
        _r.Dispose();
        Take().Should().BeEmpty();
    }

    [Fact]
    public void Dispose_ShouldRestoreTerminalAndPrintLastFrame_WhenBeginWithoutEnd()
    {
        Prime();
        _r.Dispose();
        var o = Take();
        o.Should().StartWith(Restore);
        o.Count(ch => ch == '▀').Should().Be(8);
        _r.Dispose();
        Take().Should().BeEmpty();
    }

    [Fact]
    public void Dispose_ShouldRestoreWithoutFrame_WhenNoFrameRendered()
    {
        _r.Begin();
        Take();
        _r.Dispose();
        Take().Should().Be(Restore);
    }

    [Fact]
    public void Dispose_ShouldWriteNothing_WhenBeginNeverCalled()
    {
        _r.Dispose();
        Take().Should().BeEmpty();
    }
}
