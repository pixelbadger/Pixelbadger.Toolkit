using BenchmarkDotNet.Attributes;
using Pixelbadger.Toolkit.Components;

namespace Pixelbadger.Toolkit.Benchmarks;

[MemoryDiagnoser]
public class DemosceneEffectBenchmarks
{
    private const int Width = 120;
    private const int Height = 60;
    private const int Frame = 5;

    private PixelBuffer _buffer = null!;
    private PlasmaComponent _plasma = null!;
    private FireComponent _fire = null!;
    private CopperBarsComponent _copperBars = null!;
    private KefrensBarsComponent _kefrensBars = null!;
    private BoingBallComponent _boingBall = null!;

    [GlobalSetup]
    public void Setup()
    {
        _buffer = new PixelBuffer(Width, Height);
        _plasma = new PlasmaComponent();
        _fire = new FireComponent();
        _copperBars = new CopperBarsComponent();
        _kefrensBars = new KefrensBarsComponent();
        _boingBall = new BoingBallComponent();
    }

    [Benchmark]
    public void Render_Plasma() => _plasma.RenderFrame(_buffer, Frame);

    [Benchmark]
    public void Render_Fire() => _fire.RenderFrame(_buffer, Frame);

    [Benchmark]
    public void Render_CopperBars() => _copperBars.RenderFrame(_buffer, Frame);

    [Benchmark]
    public void Render_KefrensBars() => _kefrensBars.RenderFrame(_buffer, Frame);

    [Benchmark]
    public void Render_BoingBall() => _boingBall.RenderFrame(_buffer, Frame);
}
