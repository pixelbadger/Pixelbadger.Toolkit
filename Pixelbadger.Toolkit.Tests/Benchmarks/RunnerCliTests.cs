using FluentAssertions;
using Pixelbadger.Toolkit.Benchmarks.Results;

namespace Pixelbadger.Toolkit.Tests.Benchmarks;

public class RunnerCliTests : TempDirTestBase
{
    private readonly StringWriter _out = new();
    private readonly StringWriter _err = new();

    [Fact]
    public void RunOptionsParse_ShouldDefaultJobToMedium_WhenStandard()
    {
        var o = RunOptions.Parse(Array.Empty<string>(), out var error)!;

        error.Should().BeNull();
        o.EffectiveJob.Should().Be("medium");
        o.Slow.Should().BeFalse();
    }

    [Fact]
    public void RunOptionsParse_ShouldDefaultJobToShort_WhenSlow()
    {
        RunOptions.Parse(new[] { "--slow" }, out _)!.EffectiveJob.Should().Be("short");
    }

    [Fact]
    public void RunOptionsParse_ShouldHonourExplicitJob_AndAllOptions()
    {
        var o = RunOptions.Parse(new[] { "--device", "box", "--job", "default", "--slow", "--filter", "*A*", "*B*", "--results-dir", "/x" }, out var error)!;

        error.Should().BeNull();
        o.Device.Should().Be("box");
        o.EffectiveJob.Should().Be("default");
        o.Filters.Should().Equal("*A*", "*B*");
        o.ResultsDir.Should().Be("/x");
    }

    [Theory]
    [InlineData("--job", "turbo")]
    [InlineData("--bogus")]
    [InlineData("--device")]
    [InlineData("--filter")]
    public void RunOptionsParse_ShouldReturnNullWithError_WhenUsageIsWrong(params string[] args)
    {
        RunOptions.Parse(args, out var error).Should().BeNull();
        error.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("Bad_Name")]
    [InlineData("-lead")]
    [InlineData("UPPER")]
    public void Run_ShouldExit2AndWriteNothing_WhenDeviceNameInvalid(string device)
    {
        var dir = Path.Combine(Root, "results");

        var code = RunnerCli.Run(new[] { "--device", device, "--results-dir", dir }, Root, _out, _err, typeof(RunnerCli).Assembly);

        code.Should().Be(2);
        _err.ToString().Should().Contain("--device");
        Directory.Exists(dir).Should().BeFalse();
    }

    [Fact]
    public void Run_ShouldExit2AndWriteNothing_WhenDeviceFingerprintMismatches()
    {
        var dir = Path.Combine(Root, "results");
        var live = DeviceIdentity.GatherFingerprint();
        var stored = new DeviceRecord(1, "my-box", live with { LogicalCores = live.LogicalCores + 1 });
        BenchmarkTestData.Write(dir, stored, BenchmarkTestData.Run());
        var before = Directory.GetFiles(dir, "*", SearchOption.AllDirectories).OrderBy(f => f).ToList();

        var code = RunnerCli.Run(new[] { "--device", "my-box", "--results-dir", dir }, Root, _out, _err, typeof(RunnerCli).Assembly);

        code.Should().Be(2);
        _err.ToString().Should().Contain("logicalCores").And.Contain("--device");
        Directory.GetFiles(dir, "*", SearchOption.AllDirectories).OrderBy(f => f).Should().Equal(before);
    }

    [Fact]
    public void Run_ShouldExit1_WhenNoSolutionFoundAndNoResultsDir()
    {
        RunnerCli.Run(Array.Empty<string>(), Root, _out, _err, typeof(RunnerCli).Assembly).Should().Be(1);

        _err.ToString().Should().Contain("--results-dir");
    }

    [Fact]
    public void ResolveResultsDir_ShouldUseSolutionAncestor_WhenNoOption()
    {
        File.WriteAllText(Path.Combine(Root, "Pixelbadger.Toolkit.sln"), "");
        var nested = Path.Combine(Root, "a", "b");
        Directory.CreateDirectory(nested);

        RunnerCli.ResolveResultsDir(null, nested, _err).Should().Be(Path.Combine(Root, "benchmarks", "results"));
    }

    [Fact]
    public void Validate_ShouldExit0_WhenClean_AndExit1PrintingPath_WhenCorrupt()
    {
        var path = BenchmarkTestData.Write(Root, BenchmarkTestData.Device(), BenchmarkTestData.Run());

        RunnerCli.Validate(new[] { "--results-dir", Root }, Root, _out, _err).Should().Be(0);

        File.WriteAllText(path, "{");
        var outer = new StringWriter();
        RunnerCli.Validate(new[] { "--results-dir", Root }, Root, outer, _err).Should().Be(1);
        outer.ToString().Should().Contain(Path.GetFileName(path));
    }

    [Fact]
    public void Query_ShouldPrintTable_WhenSqlValid()
    {
        BenchmarkTestData.Write(Root, BenchmarkTestData.Device(), BenchmarkTestData.Run());

        var code = RunnerCli.Query(new[] { "select class, method, mean_ns from results", "--results-dir", Root }, Root, _out, _err);

        code.Should().Be(0);
        _out.ToString().Should().Be("class\tmethod\tmean_ns\nLevenshteinBenchmarks\tShort\t100.5\n");
    }

    [Fact]
    public void Query_ShouldExit1WithMessageOnStderr_WhenSqlInvalid()
    {
        BenchmarkTestData.Write(Root, BenchmarkTestData.Device(), BenchmarkTestData.Run());

        var code = RunnerCli.Query(new[] { "selec bad", "--results-dir", Root }, Root, _out, _err);

        code.Should().Be(1);
        _err.ToString().Should().Contain("SQL error");
        _out.ToString().Should().BeEmpty();
    }

    [Fact]
    public void Query_ShouldExit1_WhenTreeHasCorruptFile()
    {
        var path = BenchmarkTestData.Write(Root, BenchmarkTestData.Device(), BenchmarkTestData.Run());
        File.WriteAllText(path, "{");

        var code = RunnerCli.Query(new[] { "select 1", "--results-dir", Root }, Root, _out, _err);

        code.Should().Be(1);
        _err.ToString().Should().Contain(Path.GetFileName(path)).And.Contain("validate");
    }

    [Fact]
    public void Query_ShouldExit1_WhenSqlMissing()
    {
        RunnerCli.Query(Array.Empty<string>(), Root, _out, _err).Should().Be(1);
    }

    [Fact]
    public void IsDirty_ShouldIgnorePathsUnderResultsDir()
    {
        var results = Path.Combine(Root, "benchmarks", "results");
        var porcelain = " M benchmarks/results/dev/8.2.0/x.json\n?? benchmarks/results/dev/device.json\n";

        GitInfo.IsDirty(porcelain, Root, results).Should().BeFalse();
    }

    [Fact]
    public void IsDirty_ShouldBeTrue_WhenAnyPathOutsideResultsDir()
    {
        var results = Path.Combine(Root, "benchmarks", "results");
        var porcelain = " M benchmarks/results/dev/x.json\n M src/Foo.cs\n";

        GitInfo.IsDirty(porcelain, Root, results).Should().BeTrue();
        GitInfo.IsDirty("R  old.cs -> benchmarks/results/new.json\n", Root, results).Should().BeFalse();
        GitInfo.IsDirty("", Root, results).Should().BeFalse();
    }

    [Fact]
    public void Report_ShouldWriteToOutPath_AndPrintWrote_WhenOutGiven()
    {
        BenchmarkTestData.Write(Root, BenchmarkTestData.Device(), BenchmarkTestData.Run());
        var outPath = Path.Combine(Root, "sub", "r.html");

        var code = RunnerCli.Report(new[] { "--results-dir", Root, "--out", outPath }, Root, _out, _err);

        code.Should().Be(0);
        _out.ToString().Should().Be($"Wrote {outPath}\n".Replace("\n", Environment.NewLine));
        var bytes = File.ReadAllBytes(outPath);
        bytes.Take(3).Should().NotEqual(new byte[] { 0xEF, 0xBB, 0xBF });
        System.Text.Encoding.UTF8.GetString(bytes).Should().Contain(BenchmarkTestData.Slug).And.Contain("LevenshteinBenchmarks");
    }

    [Fact]
    public void Report_ShouldDefaultOutToDocsUnderRepoRoot_WhenNoOut()
    {
        File.WriteAllText(Path.Combine(Root, "Pixelbadger.Toolkit.sln"), "");
        var results = Path.Combine(Root, "benchmarks", "results");
        BenchmarkTestData.Write(results, BenchmarkTestData.Device(), BenchmarkTestData.Run());
        var nested = Path.Combine(Root, "x");
        Directory.CreateDirectory(nested);

        var code = RunnerCli.Report(Array.Empty<string>(), nested, _out, _err);

        code.Should().Be(0);
        File.Exists(Path.Combine(Root, "docs", "benchmark-report.html")).Should().BeTrue();
    }

    [Fact]
    public void Report_ShouldExit1_WhenNoRepoRootAndNoOut()
    {
        var code = RunnerCli.Report(new[] { "--results-dir", Root }, Root, _out, _err);

        code.Should().Be(1);
        _err.ToString().Should().Contain("--out");
    }

    [Fact]
    public void Report_ShouldRenderEmptyPage_WhenResultsDirMissing()
    {
        var outPath = Path.Combine(Root, "r.html");

        var code = RunnerCli.Report(new[] { "--results-dir", Path.Combine(Root, "nope"), "--out", outPath }, Root, _out, _err);

        code.Should().Be(0);
        File.ReadAllText(outPath).Should().Contain("No benchmark results recorded yet");
    }

    [Fact]
    public void Report_ShouldExit1WithValidateHint_AndWriteNothing_WhenRunFileInvalid()
    {
        var path = BenchmarkTestData.Write(Root, BenchmarkTestData.Device(), BenchmarkTestData.Run());
        File.WriteAllText(path, "{");
        var outPath = Path.Combine(Root, "r.html");

        var code = RunnerCli.Report(new[] { "--results-dir", Root, "--out", outPath }, Root, _out, _err);

        code.Should().Be(1);
        _err.ToString().Should().Contain(Path.GetFileName(path)).And.Contain("validate");
        File.Exists(outPath).Should().BeFalse();
    }

    [Fact]
    public void Report_ShouldExit1_WhenArgumentUnknown()
    {
        RunnerCli.Report(new[] { "--bogus" }, Root, _out, _err).Should().Be(1);
    }

    // ---- post-BDN seam: MapResult / FinishRun ----

    private static RunnerCli.RawReport Raw(bool success = true, RunnerCli.RawStats? stats = null, bool memory = true,
        long allocated = 2048, long g0 = 5, long g1 = 2, long g2 = 1, long ops = 10000) =>
        new("Cls", "Meth", "n=3", success, success ? stats ?? new RunnerCli.RawStats(10, 9, 1, 0.5, 8, 12, 15) : null,
            memory, allocated, g0, g1, g2, ops);

    [Fact]
    public void MapResult_ShouldMapStatsAndComputeGenPerThousandOps_WhenMemoryDiagnoserPresent()
    {
        var r = RunnerCli.MapResult(Raw(g0: 5, g1: 2, g2: 1, ops: 10000));

        r.Should().Be(new BenchmarkResult("Cls", "Meth", "n=3", true, 10, 9, 1, 0.5, 8, 12, 15, 2048, 0.5, 0.2, 0.1));
    }

    [Fact]
    public void MapResult_ShouldLeaveAllocatedNullAndGenZero_WhenNoMemoryDiagnoser()
    {
        var r = RunnerCli.MapResult(Raw(memory: false));

        r.Success.Should().BeTrue();
        r.AllocatedBytes.Should().BeNull();
        (r.Gen0, r.Gen1, r.Gen2).Should().Be((0.0, 0.0, 0.0));
        r.MeanNs.Should().Be(10);
    }

    [Fact]
    public void MapResult_ShouldReturnGenZero_WhenNoOperations()
    {
        var r = RunnerCli.MapResult(Raw(ops: 0));

        (r.Gen0, r.Gen1, r.Gen2).Should().Be((0.0, 0.0, 0.0));
        r.AllocatedBytes.Should().Be(2048);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MapResult_ShouldYieldFailedRowWithZeroStats_WhenBenchmarkFailedOrHasNoStatistics(bool successButNoStats)
    {
        var raw = successButNoStats ? Raw() with { Stats = null } : Raw(success: false);

        var r = RunnerCli.MapResult(raw);

        r.Should().Be(new BenchmarkResult("Cls", "Meth", "n=3", false, 0, 0, 0, 0, 0, 0, 0, null, 0, 0, 0));
    }

    private int Finish(bool critical, IReadOnlyList<BenchmarkResult> results, string dir, DeviceRecord? device = null, RunRecord? run = null)
    {
        device ??= BenchmarkTestData.Device();
        return RunnerCli.FinishRun(critical, results, dir, device, () => run ?? BenchmarkTestData.Run(results: results.ToArray()), _out, _err);
    }

    [Fact]
    public void FinishRun_ShouldWriteFileAndExit0_WhenAllBenchmarksSucceed()
    {
        var dir = Path.Combine(Root, "results");

        Finish(false, new[] { BenchmarkTestData.Result() }, dir).Should().Be(0);

        Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories).Should().HaveCount(2); // device.json + run
        _out.ToString().Should().Contain("Wrote ");
    }

    [Fact]
    public void FinishRun_ShouldWriteFileWithFailedRowAndExit1_WhenABenchmarkFailed()
    {
        var dir = Path.Combine(Root, "results");
        var failed = RunnerCli.MapResult(Raw(success: false));

        Finish(false, new[] { BenchmarkTestData.Result(), failed }, dir).Should().Be(1);

        var runFile = Directory.GetFiles(dir, "2026*.json", SearchOption.AllDirectories).Should().ContainSingle().Subject;
        var stored = System.Text.Json.JsonSerializer.Deserialize<RunRecord>(File.ReadAllText(runFile), ResultsJson.Options)!;
        stored.Results.Should().Contain(r => !r.Success && r.MeanNs == 0 && r.AllocatedBytes == null);
        _err.ToString().Should().Contain("1 benchmark(s) failed");
    }

    [Fact]
    public void FinishRun_ShouldWriteNothingAndExit1_WhenCriticalValidationErrors()
    {
        var dir = Path.Combine(Root, "results");

        Finish(true, new[] { BenchmarkTestData.Result() }, dir).Should().Be(1);

        Directory.Exists(dir).Should().BeFalse();
        _err.ToString().Should().Contain("critical validation errors");
    }

    [Fact]
    public void FinishRun_ShouldWriteNothingAndExit1_WhenNoResults()
    {
        var dir = Path.Combine(Root, "results");

        Finish(false, Array.Empty<BenchmarkResult>(), dir).Should().Be(1);

        Directory.Exists(dir).Should().BeFalse();
        _err.ToString().Should().Contain("No benchmarks were run");
    }

    [Fact]
    public void FinishRun_ShouldExit1AndKeepFirstFileUnchanged_WhenRunFileCollides()
    {
        var dir = Path.Combine(Root, "results");
        var results = new[] { BenchmarkTestData.Result() };
        Finish(false, results, dir).Should().Be(0);
        var file = Directory.GetFiles(dir, "2026*.json", SearchOption.AllDirectories).Single();
        var before = File.ReadAllBytes(file);

        Finish(false, results, dir, run: BenchmarkTestData.Run(results: BenchmarkTestData.Result(mean: 999))).Should().Be(1);

        File.ReadAllBytes(file).Should().Equal(before);
        _err.ToString().Should().Contain(Path.GetFileName(file));
    }

    [Fact]
    public void FinishRun_ShouldExit1NamingDir_WhenResultsDirUnwritable()
    {
        var dir = Path.Combine(Root, "results");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, BenchmarkTestData.Slug), "blocks the device folder");

        Finish(false, new[] { BenchmarkTestData.Result() }, dir).Should().Be(1);

        _err.ToString().Should().Contain("Could not write results under").And.Contain(dir);
    }
}
