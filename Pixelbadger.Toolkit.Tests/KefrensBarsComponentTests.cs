using FluentAssertions;
using Pixelbadger.Toolkit.Components;

namespace Pixelbadger.Toolkit.Tests;

public class KefrensBarsComponentTests
{
    private readonly KefrensBarsComponent _component = new();

    [Fact]
    public void RenderFrame_ShouldSetSomeNonBlackPixels_WhenCalled()
    {
        var buffer = new PixelBuffer(80, 48);

        _component.RenderFrame(buffer, 0);

        bool hasNonBackground = false;
        for (int y = 0; y < buffer.Height && !hasNonBackground; y++)
            for (int x = 0; x < buffer.Width && !hasNonBackground; x++)
            {
                var p = buffer.GetPixel(x, y);
                if (p.R > 20 || p.G > 20 || p.B > 20)
                    hasNonBackground = true;
            }

        hasNonBackground.Should().BeTrue();
    }

    [Fact]
    public void RenderFrame_ShouldProduceDifferentOutput_ForDifferentFrames()
    {
        var buffer0 = new PixelBuffer(80, 48);
        var buffer10 = new PixelBuffer(80, 48);

        _component.RenderFrame(buffer0, 0);
        _component.RenderFrame(buffer10, 10);

        bool differs = false;
        for (int y = 0; y < buffer0.Height && !differs; y++)
            for (int x = 0; x < buffer0.Width && !differs; x++)
                if (buffer0.GetPixel(x, y) != buffer10.GetPixel(x, y))
                    differs = true;

        differs.Should().BeTrue();
    }

    [Fact]
    public void RenderFrame_ShouldNotThrow_WhenSmallBufferUsed()
    {
        var buffer = new PixelBuffer(10, 10);

        var act = () => _component.RenderFrame(buffer, 0);

        act.Should().NotThrow();
    }

    [Fact]
    public void RenderFrame_ShouldClearPreviousContent_OnEachCall()
    {
        var buffer = new PixelBuffer(80, 48);
        _component.RenderFrame(buffer, 0);
        var firstRender = buffer.GetPixel(0, 0);

        // Fill with a sentinel value
        buffer.SetPixel(0, 0, new PixelColor(255, 255, 255));
        _component.RenderFrame(buffer, 0);

        // Should be restored to what frame 0 produces
        buffer.GetPixel(0, 0).Should().Be(firstRender);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(200)]
    public void RenderFrame_ShouldNotThrow_ForVariousFrameNumbers(int frame)
    {
        var buffer = new PixelBuffer(80, 48);

        var act = () => _component.RenderFrame(buffer, frame);

        act.Should().NotThrow();
    }
}
