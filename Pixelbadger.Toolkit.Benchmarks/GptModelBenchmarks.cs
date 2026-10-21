using BenchmarkDotNet.Attributes;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Benchmarks;

[MemoryDiagnoser]
public class GptModelBenchmarks
{
    private GptModel _model = null!;
    private AdamW _optimizer = null!;
    private int[][] _inputs = null!;
    private int[][] _targets = null!;

    [GlobalSetup]
    public void Setup()
    {
        var config = new GptConfig(VocabSize: 512, BlockSize: 64, NEmbd: 128, NHead: 4, NLayer: 3);
        _model = new GptModel(config);
        _model.InitWeights(1337);
        _optimizer = new AdamW(_model.Parameters());

        var rng = new Random(1337);
        _inputs = new int[4][];
        _targets = new int[4][];
        for (int b = 0; b < 4; b++)
        {
            _inputs[b] = new int[config.BlockSize];
            _targets[b] = new int[config.BlockSize];
            for (int t = 0; t < config.BlockSize; t++)
            {
                _inputs[b][t] = rng.Next(config.VocabSize);
                _targets[b][t] = rng.Next(config.VocabSize);
            }
        }

        // Populate real gradients so AdamW_Step works on non-zero values.
        _model.ZeroGrad();
        var (_, loss) = _model.Forward(_inputs, _targets);
        loss!.Backward();
    }

    [Benchmark]
    public Tensor Forward() => _model.Forward(_inputs).Logits;

    [Benchmark]
    public float ForwardBackward()
    {
        _model.ZeroGrad();
        var (_, loss) = _model.Forward(_inputs, _targets);
        loss!.Backward();
        return loss.Data[0];
    }

    [Benchmark]
    public void AdamW_Step()
    {
        // Uses the real gradients computed once in GlobalSetup (Step does not clear them, so every iteration sees the same values).
        _optimizer.Step();
    }
}
