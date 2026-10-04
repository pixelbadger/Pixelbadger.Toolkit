using FluentAssertions;
using Pixelbadger.Toolkit.Components;

namespace Pixelbadger.Toolkit.Tests;

public class IDemoEffectTests
{
    public static IEnumerable<object[]> AllEffects() =>
        Showcase.CreateEffects().Select(e => new object[] { e });

    [Theory]
    [MemberData(nameof(AllEffects))]
    public void RenderFrame_ShouldNotThrow_WhenCalledForManyFrames(IDemoEffect effect)
    {
        var buffer = new PixelBuffer(64, 32);

        var act = () =>
        {
            for (int frame = 0; frame < 40; frame++)
                effect.RenderFrame(buffer, frame);
        };

        act.Should().NotThrow();
    }
}
