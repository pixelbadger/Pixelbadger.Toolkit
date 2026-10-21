using BenchmarkDotNet.Attributes;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Benchmarks;

[MemoryDiagnoser]
public class TensorBenchmarks
{
    private Tensor _a = null!;          // 64x128
    private Tensor _b = null!;          // 128x128
    private Tensor _ln = null!;         // 64x128
    private Tensor _gamma = null!;      // 1x128
    private Tensor _beta = null!;       // 1x128
    private Tensor _softmaxIn = null!;  // 64x64
    private Tensor _geluIn = null!;     // 64x512
    private Tensor _gradA = null!;      // 64x128, requires grad
    private Tensor _gradB = null!;      // 128x128, requires grad

    [GlobalSetup]
    public void Setup()
    {
        var rng = new Random(1337);
        _a = Random2D(rng, 64, 128);
        _b = Random2D(rng, 128, 128);
        _ln = Random2D(rng, 64, 128);
        _gamma = Tensor.FromData(1, 128, Enumerable.Repeat(1f, 128).ToArray());
        _beta = new Tensor(1, 128);
        _softmaxIn = Random2D(rng, 64, 64);
        _geluIn = Random2D(rng, 64, 512);
        _gradA = Random2D(rng, 64, 128, requiresGrad: true);
        _gradB = Random2D(rng, 128, 128, requiresGrad: true);
    }

    [Benchmark]
    public Tensor MatMul() => Tensor.MatMul(_a, _b);

    [Benchmark]
    public Tensor LayerNorm() => Tensor.LayerNorm(_ln, _gamma, _beta);

    [Benchmark]
    public Tensor SoftmaxRows() => Tensor.SoftmaxRows(_softmaxIn);

    [Benchmark]
    public Tensor Gelu() => Tensor.Gelu(_geluIn);

    [Benchmark]
    public float MatMul_ForwardBackward()
    {
        _gradA.ZeroGrad();
        _gradB.ZeroGrad();
        var product = Tensor.MatMul(_gradA, _gradB);
        // Reduce to a 1x1 scalar loss with public ops: mean cross-entropy against fixed targets.
        var loss = Tensor.CrossEntropy(product, new int[product.Rows]);
        loss.Backward();
        return loss.Data[0];
    }

    private static Tensor Random2D(Random rng, int rows, int cols, bool requiresGrad = false)
    {
        var data = new float[rows * cols];
        for (int i = 0; i < data.Length; i++)
            data[i] = (float)(rng.NextDouble() * 2 - 1);
        return Tensor.FromData(rows, cols, data, requiresGrad);
    }
}
