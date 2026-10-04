using FluentAssertions;
using Pixelbadger.Toolkit.Components;

namespace Pixelbadger.Toolkit.Tests;

public class PixelBufferTests
{
    [Fact]
    public void Constructor_ShouldSetWidthAndHeight_WhenValidDimensionsProvided()
    {
        var buffer = new PixelBuffer(80, 48);

        buffer.Width.Should().Be(80);
        buffer.Height.Should().Be(48);
    }

    [Fact]
    public void Constructor_ShouldRoundUpHeight_WhenOddHeightProvided()
    {
        var buffer = new PixelBuffer(80, 47);

        buffer.Height.Should().Be(48);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenWidthIsZero()
    {
        var act = () => new PixelBuffer(0, 10);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenHeightIsZero()
    {
        var act = () => new PixelBuffer(10, 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SetPixel_ShouldStoreColor_WhenValidCoordinatesProvided()
    {
        var buffer = new PixelBuffer(10, 10);
        var color = new PixelColor(255, 0, 0);

        buffer.SetPixel(5, 5, color);

        buffer.GetPixel(5, 5).Should().Be(color);
    }

    [Fact]
    public void SetPixel_ShouldIgnore_WhenCoordinatesOutOfBounds()
    {
        var buffer = new PixelBuffer(10, 10);
        var color = new PixelColor(255, 0, 0);

        var act = () => buffer.SetPixel(-1, 0, color);

        act.Should().NotThrow();
    }

    [Fact]
    public void GetPixel_ShouldThrow_WhenCoordinatesOutOfBounds()
    {
        var buffer = new PixelBuffer(10, 10);

        var act = () => buffer.GetPixel(10, 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void GetPixel_ShouldReturnBlack_WhenBufferIsNew()
    {
        var buffer = new PixelBuffer(10, 10);

        var pixel = buffer.GetPixel(0, 0);

        pixel.Should().Be(PixelColor.Black);
    }

    [Fact]
    public void Clear_ShouldFillAllPixelsWithColor()
    {
        var buffer = new PixelBuffer(4, 4);
        var color = new PixelColor(100, 150, 200);

        buffer.Clear(color);

        for (int y = 0; y < buffer.Height; y++)
            for (int x = 0; x < buffer.Width; x++)
                buffer.GetPixel(x, y).Should().Be(color);
    }

    [Fact]
    public void Render_ShouldContainHalfBlockCharacter()
    {
        var buffer = new PixelBuffer(4, 4);

        var result = buffer.Render();

        result.Should().Contain("▀");
    }

    [Fact]
    public void Render_ShouldContainAnsiResetCode()
    {
        var buffer = new PixelBuffer(4, 4);

        var result = buffer.Render();

        result.Should().Contain("\x1b[0m");
    }

    [Fact]
    public void Render_ShouldProduceCorrectNumberOfLines()
    {
        var buffer = new PixelBuffer(4, 8);

        var result = buffer.Render();

        // Each terminal row covers 2 pixel rows, so 8 pixel rows → 4 terminal rows
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Length.Should().Be(4);
    }

    [Fact]
    public void Render_ShouldEmbedForegroundColorFromTopPixel()
    {
        var buffer = new PixelBuffer(2, 2);
        var topColor = new PixelColor(10, 20, 30);
        buffer.SetPixel(0, 0, topColor);

        var result = buffer.Render();

        result.Should().Contain($"\x1b[38;2;{topColor.R};{topColor.G};{topColor.B}m");
    }

    [Fact]
    public void Render_ShouldEmbedBackgroundColorFromBottomPixel()
    {
        var buffer = new PixelBuffer(2, 2);
        var bottomColor = new PixelColor(40, 50, 60);
        buffer.SetPixel(0, 1, bottomColor);

        var result = buffer.Render();

        result.Should().Contain($"\x1b[48;2;{bottomColor.R};{bottomColor.G};{bottomColor.B}m");
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(9, 9)]
    [InlineData(0, 9)]
    [InlineData(9, 0)]
    public void SetPixel_ShouldStoreAndRetrieveColor_AtCorners(int x, int y)
    {
        var buffer = new PixelBuffer(10, 10);
        var color = new PixelColor(123, 45, 67);

        buffer.SetPixel(x, y, color);

        buffer.GetPixel(x, y).Should().Be(color);
    }

    [Fact]
    public void Render_ShouldReemitColourCodes_WhenAdjacentPixelsDiffer()
    {
        var buffer = new PixelBuffer(2, 2);
        buffer.SetPixel(0, 0, new PixelColor(1, 2, 3));
        buffer.SetPixel(1, 0, new PixelColor(4, 5, 6));
        buffer.SetPixel(0, 1, new PixelColor(7, 8, 9));
        buffer.SetPixel(1, 1, new PixelColor(10, 11, 12));

        var result = buffer.Render();

        result.Should().Contain("38;2;1;2;3m").And.Contain("38;2;4;5;6m")
            .And.Contain("48;2;7;8;9m").And.Contain("48;2;10;11;12m");
    }

    [Fact]
    public void Render_ShouldOmitRedundantColourCodes_WhenAdjacentPixelsMatch()
    {
        var buffer = new PixelBuffer(10, 2);
        buffer.Clear(new PixelColor(1, 2, 3));

        var result = buffer.Render();

        System.Text.RegularExpressions.Regex.Matches(result, "38;2;1;2;3m").Count.Should().Be(1);
        System.Text.RegularExpressions.Regex.Matches(result, "48;2;1;2;3m").Count.Should().Be(1);
    }
}
