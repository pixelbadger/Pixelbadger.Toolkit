using FluentAssertions;
using Pixelbadger.Toolkit.Components;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Pixelbadger.Toolkit.Tests;

public class PaintCanvasTests
{
    private static readonly PixelColor Red = new(255, 0, 0);
    private static readonly PixelColor Blue = new(0, 0, 255);

    private static HashSet<(int X, int Y)> NonWhite(PaintCanvas canvas)
    {
        var set = new HashSet<(int, int)>();
        var snap = canvas.Snapshot();
        for (var y = 0; y < PaintCanvas.Size; y++)
            for (var x = 0; x < PaintCanvas.Size; x++)
                if (snap[y * PaintCanvas.Size + x] != PixelColor.White) set.Add((x, y));
        return set;
    }

    private static HashSet<(int X, int Y)> Set(params (int, int)[] pts) => pts.ToHashSet();

    [Fact]
    public void Constructor_ShouldFillCanvasWhite_WhenCreated()
    {
        var canvas = new PaintCanvas();
        var snap = canvas.Snapshot();
        snap.Length.Should().Be(256 * 256);
        snap.Should().OnlyContain(p => p == PixelColor.White);
        canvas.GetPixel(255, 255).Should().Be(PixelColor.White);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(256, 0)]
    [InlineData(0, 256)]
    public void GetPixel_ShouldThrow_WhenOutsideCanvas(int x, int y)
    {
        var act = () => new PaintCanvas().GetPixel(x, y);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Snapshot_ShouldReturnIndependentRowMajorCopy_WhenCalled()
    {
        var canvas = new PaintCanvas();
        canvas.SetPixels([(3, 2, Red)]);
        var snap = canvas.Snapshot();
        snap[2 * 256 + 3].Should().Be(Red);
        snap[2 * 256 + 3] = Blue;
        canvas.GetPixel(3, 2).Should().Be(Red);
    }

    [Fact]
    public void SetPixels_ShouldPaintAndSkipOutOfCanvas_WhenSomeEntriesOutside()
    {
        var canvas = new PaintCanvas();
        var n = canvas.SetPixels([(0, 0, Red), (255, 255, Blue), (-1, 0, Red), (0, 256, Red), (256, 5, Red), (5, -1, Red)]);
        n.Should().Be(2);
        NonWhite(canvas).Should().BeEquivalentTo(Set((0, 0), (255, 255)));
        canvas.GetPixel(255, 255).Should().Be(Blue);
    }

    [Fact]
    public void SetPixels_ShouldCountDistinctPixelsAndKeepLastColour_WhenDuplicates()
    {
        var canvas = new PaintCanvas();
        canvas.SetPixels([(1, 1, Red), (1, 1, Blue)]).Should().Be(1);
        canvas.GetPixel(1, 1).Should().Be(Blue);
    }

    [Fact]
    public void SetPixels_ShouldAccept4096AndThrowFor4097_WhenAtLimit()
    {
        var canvas = new PaintCanvas();
        var ok = Enumerable.Range(0, 4096).Select(i => (i % 256, i / 256, Red)).ToList();
        canvas.SetPixels(ok).Should().Be(4096);
        var tooMany = Enumerable.Range(0, 4097).Select(i => (i % 256, i / 256, Red)).ToList();
        var act = () => canvas.SetPixels(tooMany);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FillRect_ShouldPaintExactRectangle_WhenInside()
    {
        var canvas = new PaintCanvas();
        canvas.FillRect(2, 3, 3, 2, Red).Should().Be(6);
        NonWhite(canvas).Should().BeEquivalentTo(Set((2, 3), (3, 3), (4, 3), (2, 4), (3, 4), (4, 4)));
    }

    [Theory]
    [InlineData(-2, 0, 4, 1, 2)]      // left edge
    [InlineData(254, 0, 4, 1, 2)]     // right edge
    [InlineData(0, -1, 1, 3, 2)]      // top edge
    [InlineData(0, 255, 1, 5, 1)]     // bottom edge
    [InlineData(300, 300, 5, 5, 0)]   // fully off-canvas
    [InlineData(-10, -10, 5, 5, 0)]   // fully off-canvas negative
    [InlineData(-5, -5, 10, 10, 25)]  // corner
    public void FillRect_ShouldClipToCanvas_WhenPartiallyOrFullyOutside(int x, int y, int w, int h, int expected)
    {
        var canvas = new PaintCanvas();
        canvas.FillRect(x, y, w, h, Red).Should().Be(expected);
        NonWhite(canvas).Count.Should().Be(expected);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-1, 5)]
    public void FillRect_ShouldThrow_WhenSizeNotPositive(int w, int h)
    {
        var act = () => new PaintCanvas().FillRect(0, 0, w, h, Red);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void DrawLine_ShouldPaintSinglePixel_WhenEndpointsEqual()
    {
        var canvas = new PaintCanvas();
        canvas.DrawLine(7, 9, 7, 9, Red).Should().Be(1);
        NonWhite(canvas).Should().BeEquivalentTo(Set((7, 9)));
    }

    [Fact]
    public void DrawLine_ShouldPaintHorizontalLineInclusive_WhenSameY()
    {
        var canvas = new PaintCanvas();
        canvas.DrawLine(2, 4, 6, 4, Red).Should().Be(5);
        NonWhite(canvas).Should().BeEquivalentTo(Set((2, 4), (3, 4), (4, 4), (5, 4), (6, 4)));
    }

    [Fact]
    public void DrawLine_ShouldPaintVerticalLineRegardlessOfDirection_WhenSameX()
    {
        var canvas = new PaintCanvas();
        canvas.DrawLine(3, 6, 3, 2, Red).Should().Be(5);
        NonWhite(canvas).Should().BeEquivalentTo(Set((3, 2), (3, 3), (3, 4), (3, 5), (3, 6)));
    }

    [Fact]
    public void DrawLine_ShouldPaintDiagonal_WhenSlopeIsOne()
    {
        var canvas = new PaintCanvas();
        canvas.DrawLine(0, 0, 3, 3, Red).Should().Be(4);
        NonWhite(canvas).Should().BeEquivalentTo(Set((0, 0), (1, 1), (2, 2), (3, 3)));
    }

    [Fact]
    public void DrawLine_ShouldPaintBresenhamPoints_WhenShallowSlope()
    {
        var canvas = new PaintCanvas();
        canvas.DrawLine(0, 0, 4, 2, Red).Should().Be(5);
        NonWhite(canvas).Should().BeEquivalentTo(Set((0, 0), (1, 1), (2, 1), (3, 2), (4, 2)));
    }

    [Fact]
    public void DrawLine_ShouldPaintThreeBySquarePerPoint_WhenThicknessThree()
    {
        var canvas = new PaintCanvas();
        canvas.DrawLine(5, 5, 7, 5, Red, 3).Should().Be(15);
        var expected = new HashSet<(int, int)>();
        for (var x = 4; x <= 8; x++)
            for (var y = 4; y <= 6; y++) expected.Add((x, y));
        NonWhite(canvas).Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void DrawLine_ShouldUseTopLeftBiasedSquare_WhenThicknessTwo()
    {
        var canvas = new PaintCanvas();
        canvas.DrawLine(5, 5, 5, 5, Red, 2).Should().Be(4);
        NonWhite(canvas).Should().BeEquivalentTo(Set((5, 5), (6, 5), (5, 6), (6, 6)));
    }

    [Fact]
    public void DrawLine_ShouldClipAtAllEdges_WhenLineLeavesCanvas()
    {
        var canvas = new PaintCanvas();
        canvas.DrawLine(-3, 0, 2, 0, Red).Should().Be(3);          // left
        canvas.DrawLine(254, 1, 258, 1, Red).Should().Be(2);       // right
        canvas.DrawLine(10, -2, 10, 1, Red).Should().Be(2);        // top
        canvas.DrawLine(20, 254, 20, 258, Red).Should().Be(2);     // bottom
        NonWhite(canvas).Count.Should().Be(9);
    }

    [Fact]
    public void DrawLine_ShouldPaintNothing_WhenFullyOffCanvas()
    {
        var canvas = new PaintCanvas();
        canvas.DrawLine(-20, -20, -5, -5, Red, 4).Should().Be(0);
        NonWhite(canvas).Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    [InlineData(-1)]
    public void DrawLine_ShouldThrow_WhenThicknessOutOfRange(int t)
    {
        var act = () => new PaintCanvas().DrawLine(0, 0, 1, 1, Red, t);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(16)]
    public void DrawLine_ShouldAccept_WhenThicknessAtBounds(int t)
    {
        new PaintCanvas().DrawLine(50, 50, 60, 50, Red, t).Should().BeGreaterThan(0);
    }

    [Fact]
    public void DrawLine_ShouldThrow_WhenCoordinatesAbsurdlyLarge()
    {
        var act = () => new PaintCanvas().DrawLine(0, 0, int.MaxValue, int.MaxValue, Red);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FillEllipse_ShouldPaintSinglePixel_WhenRadiiZero()
    {
        var canvas = new PaintCanvas();
        canvas.FillEllipse(10, 12, 0, 0, Red).Should().Be(1);
        NonWhite(canvas).Should().BeEquivalentTo(Set((10, 12)));
    }

    [Fact]
    public void FillEllipse_ShouldPaintExactPixelSet_WhenRx2Ry1()
    {
        var canvas = new PaintCanvas();
        canvas.FillEllipse(10, 10, 2, 1, Red).Should().Be(11);
        NonWhite(canvas).Should().BeEquivalentTo(Set(
            (8, 10), (9, 10), (10, 10), (11, 10), (12, 10),
            (9, 9), (10, 9), (11, 9),
            (9, 11), (10, 11), (11, 11)));
    }

    [Fact]
    public void FillEllipse_ShouldPaintPlusShape_WhenRadiusOneCircle()
    {
        var canvas = new PaintCanvas();
        // (dx^2+dy^2)/2.25 <= 1 : corners (2/2.25) = 0.89 <= 1 -> included, so full 3x3
        canvas.FillEllipse(5, 5, 1, 1, Red).Should().Be(9);
    }

    [Fact]
    public void FillEllipse_ShouldClipAtAllEdges_WhenCentredOnCorners()
    {
        var canvas = new PaintCanvas();
        canvas.FillEllipse(0, 0, 1, 1, Red).Should().Be(4);
        canvas.FillEllipse(255, 0, 1, 1, Red).Should().Be(4);
        canvas.FillEllipse(0, 255, 1, 1, Red).Should().Be(4);
        canvas.FillEllipse(255, 255, 1, 1, Red).Should().Be(4);
        NonWhite(canvas).Count.Should().Be(16);
    }

    [Fact]
    public void FillEllipse_ShouldPaintNothing_WhenFullyOffCanvas()
    {
        var canvas = new PaintCanvas();
        canvas.FillEllipse(-50, -50, 3, 3, Red).Should().Be(0);
        canvas.FillEllipse(400, 10, 3, 3, Red).Should().Be(0);
        NonWhite(canvas).Should().BeEmpty();
    }

    [Fact]
    public void FillEllipse_ShouldCoverWholeCanvas_WhenHugeRadius()
    {
        var canvas = new PaintCanvas();
        canvas.FillEllipse(128, 128, 100000, 100000, Red).Should().Be(256 * 256);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    public void FillEllipse_ShouldThrow_WhenRadiusNegative(int rx, int ry)
    {
        var act = () => new PaintCanvas().FillEllipse(5, 5, rx, ry, Red);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ReadRegion_ShouldReturnExactText_WhenInside()
    {
        var canvas = new PaintCanvas();
        canvas.SetPixels([(1, 2, Red), (2, 3, new PixelColor(0x0a, 0xB0, 0xff))]);
        canvas.ReadRegion(1, 2, 2, 2).Should().Be(
            "Region x=1 y=2 width=2 height=2 (rows top to bottom, pixels left to right)\n" +
            "y=2: #FF0000 #FFFFFF\n" +
            "y=3: #FFFFFF #0AB0FF");
    }

    [Fact]
    public void ReadRegion_ShouldAcceptMaxRegionAtBottomRightCorner_WhenExactlyFits()
    {
        var text = new PaintCanvas().ReadRegion(224, 224, 32, 32);
        text.Split('\n').Length.Should().Be(33);
        text.Should().EndWith("#FFFFFF").And.NotEndWith("\n");
    }

    [Theory]
    [InlineData(0, 0, 0, 1)]
    [InlineData(0, 0, 1, 0)]
    [InlineData(0, 0, 33, 1)]
    [InlineData(0, 0, 1, 33)]
    [InlineData(-1, 0, 1, 1)]
    [InlineData(0, -1, 1, 1)]
    [InlineData(255, 0, 2, 1)]
    [InlineData(0, 255, 1, 2)]
    [InlineData(300, 300, 1, 1)]
    public void ReadRegion_ShouldThrow_WhenSizeOrBoundsInvalid(int x, int y, int w, int h)
    {
        var act = () => new PaintCanvas().ReadRegion(x, y, w, h);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("#FF0000", 255, 0, 0)]
    [InlineData("ff0000", 255, 0, 0)]
    [InlineData("#aBcDeF", 0xAB, 0xCD, 0xEF)]
    [InlineData("0a0B0c", 0x0A, 0x0B, 0x0C)]
    [InlineData("#000000", 0, 0, 0)]
    public void TryParseColor_ShouldAccept_WhenValid(string text, int r, int g, int b)
    {
        PaintCanvas.TryParseColor(text, out var c).Should().BeTrue();
        c.Should().Be(new PixelColor((byte)r, (byte)g, (byte)b));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#")]
    [InlineData("#FFF")]
    [InlineData("#FFFFFFF")]
    [InlineData("#GG0000")]
    [InlineData("blue")]
    [InlineData("##FF0000")]
    [InlineData(" #FF0000")]
    [InlineData("#FF 000")]
    [InlineData("#+F0000")]
    public void TryParseColor_ShouldReject_WhenInvalid(string? text)
    {
        PaintCanvas.TryParseColor(text, out _).Should().BeFalse();
    }

    [Fact]
    public void FormatColor_ShouldReturnUppercaseHexWithHash_WhenCalled()
    {
        PaintCanvas.FormatColor(new PixelColor(0x0a, 0xbc, 0xff)).Should().Be("#0ABCFF");
        PaintCanvas.FormatColor(PixelColor.Black).Should().Be("#000000");
    }

    [Fact]
    public void ToPng_ShouldRoundTripPixels_WhenDecoded()
    {
        var canvas = new PaintCanvas();
        canvas.FillRect(10, 20, 5, 5, new PixelColor(1, 2, 3));
        canvas.SetPixels([(255, 255, Blue), (0, 255, Red)]);
        using var image = Image.Load<Rgb24>(canvas.ToPng());
        image.Width.Should().Be(256);
        image.Height.Should().Be(256);
        var snap = canvas.Snapshot();
        for (var y = 0; y < 256; y++)
            for (var x = 0; x < 256; x++)
            {
                var p = image[x, y];
                var c = snap[y * 256 + x];
                (p.R, p.G, p.B).Should().Be((c.R, c.G, c.B), $"pixel ({x},{y})");
            }
    }
}
