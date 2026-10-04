using FluentAssertions;
using Pixelbadger.Toolkit.Components;

namespace Pixelbadger.Toolkit.Tests;

public class PlasmaComponentTests
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
        var effect = new PlasmaComponent();

        for (int frame = 0; frame < 30; frame++)
            effect.RenderFrame(buffer, frame);

        Snapshot(buffer).Should().Contain(p => p.R > 20 || p.G > 20 || p.B > 20);
    }

    [Fact]
    public void RenderFrame_ShouldProduceDifferentOutput_ForDifferentFrames()
    {
        var buffer = new PixelBuffer(80, 48);
        var effect = new PlasmaComponent();

        effect.RenderFrame(buffer, 0);
        var first = Snapshot(buffer);
        effect.RenderFrame(buffer, 25);

        Snapshot(buffer).Should().NotEqual(first);
    }

    [Fact]
    public void RenderFrame_ShouldNotThrow_WhenSmallBufferUsed()
    {
        var buffer = new PixelBuffer(1, 2);

        var act = () => new PlasmaComponent().RenderFrame(buffer, 5);

        act.Should().NotThrow();
    }

    [Fact]
    public void RenderFrame_ShouldBeDeterministic_ForSameFrame()
    {
        var a = new PixelBuffer(40, 24);
        var b = new PixelBuffer(40, 24);

        new PlasmaComponent().RenderFrame(a, 7);
        new PlasmaComponent().RenderFrame(b, 7);

        Snapshot(a).Should().Equal(Snapshot(b));
    }
}
