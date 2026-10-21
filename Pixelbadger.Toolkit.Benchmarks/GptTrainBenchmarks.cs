using System.Text;
using BenchmarkDotNet.Attributes;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Benchmarks;

[MemoryDiagnoser]
public class GptTrainBenchmarks
{
    private string _corpus = null!;
    private string _directory = null!;
    private GptTrainOptions _options = null!;
    private GptTrainComponent _component = null!;

    [GlobalSetup]
    public void Setup()
    {
        _corpus = GenerateCorpus(20 * 1024);
        _directory = Path.Combine(Path.GetTempPath(), "pbtk-bench-gpttrain-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _options = new GptTrainOptions(
            Steps: 5, BatchSize: 4, BlockSize: 32, NEmbd: 64, NHead: 4, NLayer: 2, VocabSize: 256, Seed: 1337);
        _component = new GptTrainComponent(new NoOpCheckpointService(), new NoOpLossGraphService());
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, true);
    }

    [Benchmark]
    public Task<GptTrainResult> Train5Steps() => _component.TrainAsync(_corpus, _directory, _options);

    private static string GenerateCorpus(int targetBytes)
    {
        var rng = new Random(1337);
        var words = new string[200];
        for (int i = 0; i < words.Length; i++)
        {
            var chars = new char[rng.Next(2, 9)];
            for (int c = 0; c < chars.Length; c++)
                chars[c] = (char)('a' + rng.Next(26));
            words[i] = new string(chars);
        }

        var sb = new StringBuilder(targetBytes + 16);
        while (sb.Length < targetBytes)
        {
            sb.Append(words[rng.Next(words.Length)]);
            sb.Append(rng.Next(10) == 0 ? ". " : " ");
        }
        return sb.ToString();
    }

    private sealed class NoOpCheckpointService : ICheckpointService
    {
        public Task SaveAsync(string directory, GptConfig config, TokenizerState tokenizer, IReadOnlyList<Tensor> parameters)
            => Task.CompletedTask;

        public Task<GptCheckpoint> LoadAsync(string directory)
            => throw new NotSupportedException();

        public Task SaveOptimizerStateAsync(string directory, AdamWState state) => Task.CompletedTask;

        public Task<AdamWState?> TryLoadOptimizerStateAsync(string directory) => Task.FromResult<AdamWState?>(null);

        public Task SaveTrainingHistoryAsync(string directory, TrainingHistory history) => Task.CompletedTask;

        public Task<TrainingHistory?> TryLoadTrainingHistoryAsync(string directory) => Task.FromResult<TrainingHistory?>(null);
    }

    private sealed class NoOpLossGraphService : ILossGraphService
    {
        public Task WriteAsync(string outputPath, LossGraphReport report) => Task.CompletedTask;
    }
}
