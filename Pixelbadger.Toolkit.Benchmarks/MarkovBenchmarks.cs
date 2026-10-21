using System.Text;
using BenchmarkDotNet.Attributes;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Benchmarks;

[MemoryDiagnoser]
public class MarkovBenchmarks
{
    private string _directory = null!;
    private string _corpusPath = null!;
    private string _modelDirectory = null!;
    private InMemoryMarkovModelService _trainService = null!;
    private InMemoryMarkovModelService _completeService = null!;
    private string _prompt = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), "pbtk-bench-markov-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _modelDirectory = Path.Combine(_directory, "model");

        var corpus = GenerateCorpus(50 * 1024);
        _corpusPath = Path.Combine(_directory, "corpus.txt");
        await File.WriteAllTextAsync(_corpusPath, corpus);

        _trainService = new InMemoryMarkovModelService();
        _completeService = new InMemoryMarkovModelService();
        await new MarkovTrainComponent(_completeService).TrainAsync(_corpusPath, _modelDirectory);
        _prompt = "w0 w1";
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, true);
    }

    [Benchmark]
    public Task<int> Train()
        => new MarkovTrainComponent(_trainService).TrainAsync(_corpusPath, _modelDirectory);

    [Benchmark]
    public Task<string> Complete50Words()
        => new MarkovCompleteComponent(_completeService, new Random(42)).CompleteAsync(_prompt, _modelDirectory, 50);

    // Seeded words drawn from a 300-word vocabulary so most words have successors.
    private static string GenerateCorpus(int targetBytes)
    {
        var rng = new Random(1337);
        var sb = new StringBuilder(targetBytes + 16);
        while (sb.Length < targetBytes)
        {
            sb.Append('w').Append(rng.Next(300));
            sb.Append(rng.Next(12) == 0 ? '\n' : ' ');
        }
        return sb.ToString();
    }

    private sealed class InMemoryMarkovModelService : IMarkovModelService
    {
        private Dictionary<string, List<string>> _model = new();

        public Task SaveModelAsync(string directory, Dictionary<string, List<string>> model)
        {
            _model = model;
            return Task.CompletedTask;
        }

        public Task<Dictionary<string, List<string>>> LoadModelAsync(string directory)
            => Task.FromResult(_model);
    }
}
