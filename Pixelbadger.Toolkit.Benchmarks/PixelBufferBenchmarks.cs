using BenchmarkDotNet.Attributes;
using Pixelbadger.Toolkit.Components;

namespace Pixelbadger.Toolkit.Benchmarks;

[MemoryDiagnoser]
public class PixelBufferBenchmarks
{
    private PixelBuffer _flat = null!;
    private PixelBuffer _noisy = null!;

    [GlobalSetup]
    public void Setup()
    {
        _flat = new PixelBuffer(120, 60);
        _flat.Clear(new PixelColor(10, 20, 30));

        _noisy = new PixelBuffer(120, 60);
        new PlasmaComponent().RenderFrame(_noisy, 5);
    }

    [Benchmark]
    public string Render_Flat() => _flat.Render();

    [Benchmark]
    public string Render_Plasma() => _noisy.Render();
}
