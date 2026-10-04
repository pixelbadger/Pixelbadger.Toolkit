using FluentAssertions;
using Pixelbadger.Toolkit.Components;

namespace Pixelbadger.Toolkit.Tests;

public class ShowcaseTests
{
    [Fact]
    public void CreateEffects_ShouldContainAllFiveEffects_InOrder()
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
    public void CreateEffects_ShouldReturnFreshInstances_OnEachCall()
    {
        var first = Showcase.CreateEffects();
        var second = Showcase.CreateEffects();

        first.Zip(second).Should().OnlyContain(pair => !ReferenceEquals(pair.First, pair.Second));
    }
}
