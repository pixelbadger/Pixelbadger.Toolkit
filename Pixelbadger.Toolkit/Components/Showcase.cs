namespace Pixelbadger.Toolkit.Components;

public static class Showcase
{
    // Fresh effect instances in playback order, alternating Amiga and IBM PC classics.
    public static IReadOnlyList<IDemoEffect> CreateEffects() =>
    [
        new BoingBallComponent(),
        new PlasmaComponent(),
        new KefrensBarsComponent(),
        new FireComponent(),
        new CopperBarsComponent(),
    ];
}
