using FluentAssertions;
using Pixelbadger.Toolkit.Components;

namespace Pixelbadger.Toolkit.Tests;

public class DemoEffectsTests
{
    public static IEnumerable<object[]> NewEffects() =>
    [
        [new PlasmaComponent()],
        [new FireComponent()],
        [new CopperBarsComponent()],
    ];

    private static List<PixelColor> Snapshot(PixelBuffer buffer)
    {
        var list = new List<PixelColor>();
        for (int y = 0; y < buffer.Height; y++)
            for (int x = 0; x < buffer.Width; x++)
                list.Add(buffer.GetPixel(x, y));
        return list;
    }

    [Theory]
    [MemberData(nameof(NewEffects))]
    public void RenderFrame_ShouldDrawNonBlackPixels_WhenCalledRepeatedly(IDemoEffect effect)
    {
        var buffer = new PixelBuffer(80, 48);

        for (int frame = 0; frame < 30; frame++)
            effect.RenderFrame(buffer, frame);

        Snapshot(buffer).Should().Contain(p => p.R > 20 || p.G > 20 || p.B > 20);
    }

    [Theory]
    [MemberData(nameof(NewEffects))]
    public void RenderFrame_ShouldProduceDifferentOutput_ForDifferentFrames(IDemoEffect effect)
    {
        var buffer = new PixelBuffer(80, 48);

        effect.RenderFrame(buffer, 0);
        var first = Snapshot(buffer);
        effect.RenderFrame(buffer, 25);

        Snapshot(buffer).Should().NotEqual(first);
    }

    [Theory]
    [MemberData(nameof(NewEffects))]
    public void RenderFrame_ShouldNotThrow_WhenSmallBufferUsed(IDemoEffect effect)
    {
        var buffer = new PixelBuffer(1, 2);

        var act = () => effect.RenderFrame(buffer, 5);

        act.Should().NotThrow();
    }

    [Fact]
    public void Plasma_ShouldBeDeterministic_ForSameFrame()
    {
        var a = new PixelBuffer(40, 24);
        var b = new PixelBuffer(40, 24);

        new PlasmaComponent().RenderFrame(a, 7);
        new PlasmaComponent().RenderFrame(b, 7);

        Snapshot(a).Should().Equal(Snapshot(b));
    }

    [Fact]
    public void Fire_ShouldBeHotterAtBottomThanTop_WhenSettled()
    {
        var buffer = new PixelBuffer(60, 40);
        var fire = new FireComponent();

        for (int frame = 0; frame < 60; frame++)
            fire.RenderFrame(buffer, frame);

        int Sum(int y) => Enumerable.Range(0, buffer.Width).Sum(x => buffer.GetPixel(x, y).R);
        Sum(buffer.Height - 1).Should().BeGreaterThan(Sum(0));
    }

    [Fact]
    public void Fire_ShouldHandleBufferResize_WhenBufferSizeChanges()
    {
        var fire = new FireComponent();

        fire.RenderFrame(new PixelBuffer(40, 20), 0);
        var act = () => fire.RenderFrame(new PixelBuffer(30, 30), 1);

        act.Should().NotThrow();
    }

    [Fact]
    public void Showcase_ShouldContainAllFiveEffects_InOrder()
    {
        var effects = Showcase.CreateEffects();

        effects.Select(e => e.GetType()).Should().Equal(
            typeof(BoingBallComponent),
            typeof(PlasmaComponent),
            typeof(KefrensBarsComponent),
            typeof(FireComponent),
            typeof(CopperBarsComponent));
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
