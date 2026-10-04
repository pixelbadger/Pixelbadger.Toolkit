using FluentAssertions;
using Pixelbadger.Toolkit.Components;

namespace Pixelbadger.Toolkit.Tests;

public class FireComponentTests
{
    private static List<PixelColor> Snapshot(PixelBuffer buffer)
    {
        var list = new List<PixelColor>();
        for (int y = 0; y < buffer.Height; y++)
            for (int x = 0; x < buffer.Width; x++)
                list.Add(buffer.GetPixel(x, y));
        return list;
    }

    [Fact]
    public void RenderFrame_ShouldDrawNonBlackPixels_WhenCalledRepeatedly()
    {
        var buffer = new PixelBuffer(80, 48);
        var effect = new FireComponent();

        for (int frame = 0; frame < 30; frame++)
            effect.RenderFrame(buffer, frame);

        Snapshot(buffer).Should().Contain(p => p.R > 20 || p.G > 20 || p.B > 20);
    }

    [Fact]
    public void RenderFrame_ShouldProduceDifferentOutput_ForDifferentFrames()
    {
        var buffer = new PixelBuffer(80, 48);
        var effect = new FireComponent();

        effect.RenderFrame(buffer, 0);
        var first = Snapshot(buffer);
        effect.RenderFrame(buffer, 25);

        Snapshot(buffer).Should().NotEqual(first);
    }

    [Fact]
    public void RenderFrame_ShouldNotThrow_WhenSmallBufferUsed()
    {
        var buffer = new PixelBuffer(1, 2);

        var act = () => new FireComponent().RenderFrame(buffer, 5);

        act.Should().NotThrow();
    }

    [Fact]
    public void RenderFrame_ShouldBeHotterAtBottomThanTop_WhenSettled()
    {
        var buffer = new PixelBuffer(60, 40);
        var fire = new FireComponent();

        for (int frame = 0; frame < 60; frame++)
            fire.RenderFrame(buffer, frame);

        int Sum(int y) => Enumerable.Range(0, buffer.Width).Sum(x => buffer.GetPixel(x, y).R);
        Sum(buffer.Height - 1).Should().BeGreaterThan(Sum(0));
    }

    [Fact]
    public void RenderFrame_ShouldHandleBufferResize_WhenBufferSizeChanges()
    {
        var fire = new FireComponent();

        fire.RenderFrame(new PixelBuffer(40, 20), 0);
        var act = () => fire.RenderFrame(new PixelBuffer(30, 30), 1);

        act.Should().NotThrow();
    }

    [Fact]
    public void RenderFrame_ShouldNotThrow_WhenBufferHasMinimumHeight()
    {
        var act = () => new FireComponent().RenderFrame(new PixelBuffer(5, 1), 0);

        act.Should().NotThrow();
    }
}
