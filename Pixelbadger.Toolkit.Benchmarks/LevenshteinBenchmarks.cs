using BenchmarkDotNet.Attributes;
using Pixelbadger.Toolkit.Components;

namespace Pixelbadger.Toolkit.Benchmarks;

[MemoryDiagnoser]
public class LevenshteinBenchmarks
{
    private LevenshteinCalculator _calculator = null!;

    private const string ShortA = "kitten";
    private const string ShortB = "sitting";
    private const string MediumA = "The quick brown fox jumps over the lazy dog";
    private const string MediumB = "The slow white cat fell into the muddy pit";
    private const string LongA = "Lorem ipsum dolor sit amet consectetur adipiscing elit sed do eiusmod tempor incididunt ut labore et dolore magna aliqua ut enim ad minim veniam quis nostrud exercitation ullamco laboris nisi";
    private const string LongB = "Lorem ipsum dolor sit amet consectetur adipiscing elit sed do eiusmod tempor incididunt ut labore et dolore magna aliqua ut enim ad minim veniam quis nostrud exercitation ullamco laboris nisl";

    [GlobalSetup]
    public void Setup()
    {
        _calculator = new LevenshteinCalculator();
    }

    [Benchmark]
    public async Task<int> Distance_ShortStrings() =>
        await _calculator.CalculateDistanceAsync(ShortA, ShortB);

    [Benchmark]
    public async Task<int> Distance_MediumStrings() =>
        await _calculator.CalculateDistanceAsync(MediumA, MediumB);

    [Benchmark]
    public async Task<int> Distance_LongStrings() =>
        await _calculator.CalculateDistanceAsync(LongA, LongB);

    [Benchmark]
    public async Task<int> Distance_IdenticalStrings() =>
        await _calculator.CalculateDistanceAsync(LongA, LongA);

    [Benchmark]
    public async Task<int> Distance_EmptyVsLong() =>
        await _calculator.CalculateDistanceAsync("", LongA);
}
