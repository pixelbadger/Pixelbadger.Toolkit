using FluentAssertions;
using Pixelbadger.Toolkit.Components;

namespace Pixelbadger.Toolkit.Tests;

public class CanvasDownscalerTests
{
    private static PixelColor[] Fill(int size, PixelColor c) => Enumerable.Repeat(c, size * size).ToArray();

    [Fact]
    public void Downscale_ShouldReturnEqualCopy_WhenTargetEqualsSource()
    {
        var src = Fill(4, PixelColor.Red);
        var result = CanvasDownscaler.Downscale(src, 4, 4);
        result.Should().Equal(src);
        result.Should().NotBeSameAs(src);
    }

    [Fact]
    public void Downscale_ShouldAverageBlocks_WhenHalvingSize()
    {
        var src = Fill(4, PixelColor.Black);
        src[0] = new PixelColor(255, 100, 0);
        src[1] = new PixelColor(0, 100, 0);
        src[4] = new PixelColor(0, 0, 0);
        src[5] = new PixelColor(1, 0, 0);
        // top-left block: r = 256/4 = 64, g = 200/4 = 50, b = 0
        var result = CanvasDownscaler.Downscale(src, 4, 2);
        result[0].Should().Be(new PixelColor(64, 50, 0));
        result[1].Should().Be(PixelColor.Black);
        result[2].Should().Be(PixelColor.Black);
        result[3].Should().Be(PixelColor.Black);
    }

    [Fact]
    public void Downscale_ShouldRoundHalfUp_WhenAverageIsFractional()
    {
        var src = Fill(2, PixelColor.Black);
        src[0] = new PixelColor(1, 0, 0);
        src[1] = new PixelColor(1, 0, 0);
        // 2/4 = 0.5 -> 1
        CanvasDownscaler.Downscale(src, 2, 1)[0].Should().Be(new PixelColor(1, 0, 0));
    }

    [Fact]
    public void Downscale_ShouldUseIntegerBounds_WhenTargetDoesNotDivideSource()
    {
        // 3 -> 2: target 0 covers [0,1), target 1 covers [1,3)
        var src = new PixelColor[9];
        for (var i = 0; i < 9; i++) src[i] = new PixelColor((byte)(i * 10), 0, 0);
        var result = CanvasDownscaler.Downscale(src, 3, 2);
        result[0].Should().Be(new PixelColor(0, 0, 0));
        result[1].Should().Be(new PixelColor(15, 0, 0));   // pixels 1,2 -> (10+20)/2
        result[2].Should().Be(new PixelColor(45, 0, 0));   // pixels 3,6 -> (30+60)/2
        result[3].Should().Be(new PixelColor(60, 0, 0));   // pixels 4,5,7,8 -> 240/4
    }

    [Fact]
    public void Downscale_ShouldPreserveUniformColour_WhenSourceIsUniform()
    {
        var c = new PixelColor(12, 34, 56);
        CanvasDownscaler.Downscale(Fill(256, c), 256, 100).Should().OnlyContain(p => p == c);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(5)]
    public void Downscale_ShouldThrow_WhenTargetOutOfRange(int target)
    {
        var act = () => CanvasDownscaler.Downscale(Fill(4, PixelColor.White), 4, target);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
