using FluentAssertions;
using Pixelbadger.Toolkit.Components;

namespace Pixelbadger.Toolkit.Tests;

public class BoingBallComponentTests
{
    private readonly BoingBallComponent _component = new();

    [Fact]
    public void RenderFrame_ShouldSetBallPixels_WhenCalled()
    {
        var buffer = new PixelBuffer(80, 48);

        _component.RenderFrame(buffer, 0);

        // Ball pixels are either red or white; background is grey
        bool hasBallPixel = false;
        for (int y = 0; y < buffer.Height && !hasBallPixel; y++)
            for (int x = 0; x < buffer.Width && !hasBallPixel; x++)
            {
                var p = buffer.GetPixel(x, y);
                // Ball white is (240,240,240), ball red is (220,40,40)
                bool isWhite = p.R > 200 && p.G > 200 && p.B > 200;
                bool isRed = p.R > 150 && p.G < 100 && p.B < 100;
                if (isWhite || isRed)
                    hasBallPixel = true;
            }

        hasBallPixel.Should().BeTrue();
    }

    [Fact]
    public void RenderFrame_ShouldProduceDifferentBallPosition_ForDifferentFrames()
    {
        var buffer0 = new PixelBuffer(80, 48);
        var buffer50 = new PixelBuffer(80, 48);

        _component.RenderFrame(buffer0, 0);
        _component.RenderFrame(buffer50, 50);

        bool differs = false;
        for (int y = 0; y < buffer0.Height && !differs; y++)
            for (int x = 0; x < buffer0.Width && !differs; x++)
                if (buffer0.GetPixel(x, y) != buffer50.GetPixel(x, y))
                    differs = true;

        differs.Should().BeTrue();
    }

    [Fact]
    public void RenderFrame_ShouldNotThrow_ForVariousFrameNumbers()
    {
        var buffer = new PixelBuffer(80, 48);

        for (int frame = 0; frame < 200; frame += 20)
        {
            var act = () => _component.RenderFrame(buffer, frame);
            act.Should().NotThrow();
        }
    }

    [Fact]
    public void ComputeBallState_ShouldKeepBallWithinBounds()
    {
        int width = 80;
        int height = 48;
        int radius = Math.Min(width, height) / 5;

        for (int frame = 0; frame < 500; frame++)
        {
            var (cx, cy, _) = BoingBallComponent.ComputeBallState(width, height, radius, frame);

            cx.Should().BeGreaterThanOrEqualTo(radius, $"cx at frame {frame}");
            cx.Should().BeLessThanOrEqualTo(width - radius, $"cx at frame {frame}");
            cy.Should().BeGreaterThanOrEqualTo(radius, $"cy at frame {frame}");
            cy.Should().BeLessThanOrEqualTo(height - radius, $"cy at frame {frame}");
        }
    }

    [Fact]
    public void ComputeBallState_ShouldReturnPositiveRotation_AsFrameIncreases()
    {
        var (_, _, rot0) = BoingBallComponent.ComputeBallState(80, 48, 9, 0);
        var (_, _, rot10) = BoingBallComponent.ComputeBallState(80, 48, 9, 10);

        rot10.Should().BeGreaterThan(rot0);
    }

    [Fact]
    public void RenderFrame_ShouldNotThrow_WhenSmallBufferUsed()
    {
        var buffer = new PixelBuffer(20, 20);

        var act = () => _component.RenderFrame(buffer, 0);

        act.Should().NotThrow();
    }
}
