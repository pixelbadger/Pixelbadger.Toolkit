using BenchmarkDotNet.Attributes;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Benchmarks;

[MemoryDiagnoser]
public class GptCompleteBenchmarks
{
    private const string Prompt = "the quick brown ";

    private GptCompleteComponent _component = null!;

    [GlobalSetup]
    public void Setup()
    {
        const string corpus = "the quick brown fox jumps over the lazy dog. pack my box with five dozen liquor jugs.";
        var tokenizer = CharTokenizer.Build(corpus);
        var config = new GptConfig(tokenizer.VocabSize, BlockSize: 32, NEmbd: 64, NHead: 4, NLayer: 2);
        var model = new GptModel(config);
        model.InitWeights(1337);

        var weights = model.Parameters().Select(p => (float[])p.Data.Clone()).ToArray();
        var checkpoint = new GptCheckpoint(config, tokenizer.ExportState(), weights);
        _component = new GptCompleteComponent(new InMemoryCheckpointService(checkpoint));
    }

    [Benchmark]
    public Task<GptCompleteResult> Complete50Tokens()
        => _component.CompleteAsync("in-memory", Prompt, maxTokens: 50, temperature: 0.8f, seed: 42);

    private sealed class InMemoryCheckpointService(GptCheckpoint checkpoint) : ICheckpointService
    {
        public Task<GptCheckpoint> LoadAsync(string directory) => Task.FromResult(checkpoint);

        public Task SaveAsync(string directory, GptConfig config, TokenizerState tokenizer, IReadOnlyList<Tensor> parameters)
            => throw new NotSupportedException();

        public Task SaveOptimizerStateAsync(string directory, AdamWState state) => throw new NotSupportedException();

        public Task<AdamWState?> TryLoadOptimizerStateAsync(string directory) => throw new NotSupportedException();

        public Task SaveTrainingHistoryAsync(string directory, TrainingHistory history) => throw new NotSupportedException();

        public Task<TrainingHistory?> TryLoadTrainingHistoryAsync(string directory) => throw new NotSupportedException();
    }
}
