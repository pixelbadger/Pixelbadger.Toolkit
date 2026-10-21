using BenchmarkDotNet.Attributes;
using FluentAssertions;
using Pixelbadger.Toolkit.Benchmarks;
using Pixelbadger.Toolkit.Benchmarks.Results;

namespace Pixelbadger.Toolkit.Tests.Benchmarks;

public class TierSelectionTests
{
    public class PlainBench { [Benchmark] public int A() => 1; }

    [BenchmarkCategory("slow")]
    public class SlowBench { [Benchmark] public int A() => 1; }

    [BenchmarkCategory("other", "Slow")]
    public class SlowMixedBench { [Benchmark] public int A() => 1; }

    [BenchmarkCategory("fast")]
    public class OtherCategoryBench { [Benchmark] public int A() => 1; }

    public class NoBenchmarks { public int A() => 1; }

    public abstract class AbstractBench { [Benchmark] public int A() => 1; }

    private static readonly Type[] Candidates =
        { typeof(PlainBench), typeof(SlowBench), typeof(SlowMixedBench), typeof(OtherCategoryBench) };

    [Fact]
    public void Select_ShouldExcludeSlowClasses_WhenStandardTier()
    {
        TierSelection.Select(Candidates, slow: false).Should().BeEquivalentTo(new[] { typeof(PlainBench), typeof(OtherCategoryBench) });
    }

    [Fact]
    public void Select_ShouldReturnOnlySlowClasses_WhenSlowTier()
    {
        TierSelection.Select(Candidates, slow: true).Should().BeEquivalentTo(new[] { typeof(SlowBench), typeof(SlowMixedBench) });
    }

    [Fact]
    public void BenchmarkTypes_ShouldSkipAbstractAndBenchmarklessClasses()
    {
        var types = TierSelection.BenchmarkTypes(typeof(TierSelectionTests).Assembly);

        types.Should().Contain(typeof(PlainBench)).And.Contain(typeof(SlowBench));
        types.Should().NotContain(typeof(NoBenchmarks)).And.NotContain(typeof(AbstractBench));
    }

    [Fact]
    public void RealAssembly_ShouldPutOnlyHomomorphicStringBenchmarksInSlowTier()
    {
        var all = TierSelection.BenchmarkTypes(typeof(HomomorphicStringBenchmarks).Assembly);

        TierSelection.Select(all, slow: true).Should().Equal(typeof(HomomorphicStringBenchmarks));
        TierSelection.Select(all, slow: false).Should().NotContain(typeof(HomomorphicStringBenchmarks)).And.NotBeEmpty();
    }

    [Fact]
    public void HomomorphicStringBenchmarks_ShouldHaveNoParams_AndExposeNewMethods()
    {
        var type = typeof(HomomorphicStringBenchmarks);

        type.GetProperty("ShortPlaintext")!.GetCustomAttributes(typeof(ParamsAttribute), true).Should().BeEmpty();
        type.GetMethod("GenerateKey").Should().NotBeNull();
        type.GetMethod("MultiplyEncrypted_ByScalar").Should().NotBeNull();
        type.GetMethod("MultiplyEncrypted_ByZero").Should().NotBeNull();
        new HomomorphicStringBenchmarks().ShortPlaintext.Should().Be("hello");
    }

    [Theory]
    [InlineData("short", 1, 3, 3)]
    [InlineData("medium", 2, 10, 15)]
    public void JobInfoFor_ShouldMatchBdnPresets(string name, int launch, int warmup, int iterations)
    {
        RunnerCli.JobInfoFor(name).Should().Be(new JobInfo(name, launch, warmup, iterations));
    }

    [Fact]
    public void JobInfoFor_ShouldReturnZeroCounts_ForDefaultJob()
    {
        RunnerCli.JobInfoFor("default").Should().Be(new JobInfo("default", 0, 0, 0));
    }
}
