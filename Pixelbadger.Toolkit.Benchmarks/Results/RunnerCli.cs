using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using Microsoft.Data.Sqlite;

namespace Pixelbadger.Toolkit.Benchmarks.Results;

public static class RunnerCli
{
    public static string DefaultResultsDir(string cwd) =>
        GitInfo.FindRepoRoot(cwd) is { } root ? Path.Combine(root, "benchmarks", "results") : "";

    /// <summary>Resolves --results-dir or the repo default. Returns null (after writing to stderr) if neither is available.</summary>
    public static string? ResolveResultsDir(string? option, string cwd, TextWriter stderr)
    {
        if (option is not null) return Path.GetFullPath(option);
        var def = DefaultResultsDir(cwd);
        if (def.Length > 0) return def;
        stderr.WriteLine("Could not find Pixelbadger.Toolkit.sln in any parent directory; pass --results-dir.");
        return null;
    }

    /// <summary>Parses "--results-dir <dir>" (and nothing else) from args.</summary>
    private static bool TryParseResultsDirOnly(IReadOnlyList<string> args, out string? dir, TextWriter stderr)
    {
        dir = null;
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] == "--results-dir" && i + 1 < args.Count) dir = args[++i];
            else { stderr.WriteLine($"Unexpected argument '{args[i]}'"); return false; }
        }
        return true;
    }

    public static int Validate(IReadOnlyList<string> args, string cwd, TextWriter stdout, TextWriter stderr)
    {
        if (!TryParseResultsDirOnly(args, out var option, stderr)) return 1;
        var dir = ResolveResultsDir(option, cwd, stderr);
        if (dir is null) return 1;
        var problems = ResultsValidator.Validate(dir);
        foreach (var p in problems) stdout.WriteLine(p);
        if (problems.Count > 0) return 1;
        stdout.WriteLine("OK");
        return 0;
    }

    public static int Query(IReadOnlyList<string> args, string cwd, TextWriter stdout, TextWriter stderr)
    {
        if (args.Count == 0 || args[0].StartsWith("--", StringComparison.Ordinal))
        {
            stderr.WriteLine("query requires a SQL string");
            return 1;
        }
        var sql = args[0];
        if (!TryParseResultsDirOnly(args.Skip(1).ToList(), out var option, stderr)) return 1;
        var dir = ResolveResultsDir(option, cwd, stderr);
        if (dir is null) return 1;
        try
        {
            stdout.Write(QueryEngine.Execute(ResultStore.LoadAll(dir), sql));
            return 0;
        }
        catch (InvalidDataException ex)
        {
            stderr.WriteLine($"{ex.Message}\nRun 'validate' to list all problems.");
            return 1;
        }
        catch (SqliteException ex)
        {
            stderr.WriteLine($"SQL error: {ex.Message}");
            return 1;
        }
    }

    public static int Report(IReadOnlyList<string> args, string cwd, TextWriter stdout, TextWriter stderr)
    {
        string? resultsOption = null, outOption = null;
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] == "--results-dir" && i + 1 < args.Count) resultsOption = args[++i];
            else if (args[i] == "--out" && i + 1 < args.Count) outOption = args[++i];
            else { stderr.WriteLine($"Unexpected argument '{args[i]}'"); return 1; }
        }

        string outPath;
        if (outOption is not null) outPath = Path.GetFullPath(outOption);
        else if (GitInfo.FindRepoRoot(cwd) is { } root) outPath = Path.Combine(root, "docs", "benchmark-report.html");
        else
        {
            stderr.WriteLine("Could not find Pixelbadger.Toolkit.sln in any parent directory; pass --out.");
            return 1;
        }

        var dir = ResolveResultsDir(resultsOption, cwd, stderr);
        if (dir is null) return 1;

        IReadOnlyList<StoredRun> runs;
        try
        {
            runs = Directory.Exists(dir) ? ResultStore.LoadAll(dir) : Array.Empty<StoredRun>();
        }
        catch (InvalidDataException ex)
        {
            stderr.WriteLine($"{ex.Message}\nRun 'validate' to list all problems.");
            return 1;
        }

        try
        {
            var parent = Path.GetDirectoryName(outPath);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            File.WriteAllText(outPath, ReportGenerator.Render(runs), new System.Text.UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            stderr.WriteLine($"Could not write report to {outPath}: {ex.Message}");
            return 1;
        }

        stdout.WriteLine($"Wrote {outPath}");
        return 0;
    }

    public static Job JobFor(string name) => name switch
    {
        "short" => Job.ShortRun,
        "medium" => Job.MediumRun,
        _ => Job.Default,
    };

    public static JobInfo JobInfoFor(string name)
    {
        var job = JobFor(name);
        return new JobInfo(name,
            job.Run.HasValue(RunMode.LaunchCountCharacteristic) ? job.Run.LaunchCount : 0,
            job.Run.HasValue(RunMode.WarmupCountCharacteristic) ? job.Run.WarmupCount : 0,
            job.Run.HasValue(RunMode.IterationCountCharacteristic) ? job.Run.IterationCount : 0);
    }

    public static int Run(IReadOnlyList<string> args, string cwd, TextWriter stdout, TextWriter stderr, Assembly benchmarkAssembly)
    {
        var options = RunOptions.Parse(args, out var error);
        if (options is null)
        {
            stderr.WriteLine(error);
            return 1;
        }
        if (options.Device is not null && !DeviceIdentity.IsValidDeviceName(options.Device))
        {
            stderr.WriteLine($"Invalid --device '{options.Device}': must match ^[a-z0-9][a-z0-9-]{{0,62}}$");
            return 2;
        }
        var resultsDir = ResolveResultsDir(options.ResultsDir, cwd, stderr);
        if (resultsDir is null) return 1;

        var fingerprint = DeviceIdentity.GatherFingerprint();
        var device = new DeviceRecord(SchemaInfo.CurrentVersion, options.Device ?? DeviceIdentity.BuildSlug(fingerprint), fingerprint);
        try
        {
            ResultStore.CheckDevice(resultsDir, device);
        }
        catch (DeviceMismatchException ex)
        {
            stderr.WriteLine(ex.Message);
            return 2;
        }
        catch (InvalidDataException ex)
        {
            stderr.WriteLine(ex.Message);
            return 1;
        }

        try
        {
            ResultStore.EnsureWritable(resultsDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            stderr.WriteLine($"Results directory is not writable: {resultsDir} ({ex.Message})");
            return 1;
        }

        var types = TierSelection.Select(TierSelection.BenchmarkTypes(benchmarkAssembly), options.Slow);
        var tier = TierSelection.TierName(options.Slow);
        if (types.Count == 0)
        {
            stderr.WriteLine($"No benchmark classes in the {tier} tier.");
            return 1;
        }

        var jobName = options.EffectiveJob;
        var config = ManualConfig.Create(DefaultConfig.Instance).AddJob(JobFor(jobName));
        var bdnArgs = new List<string> { "--filter" };
        if (options.Filters.Count == 0) bdnArgs.Add("*");
        else bdnArgs.AddRange(options.Filters);

        stdout.WriteLine($"Device: {device.Slug}  Tier: {tier}  Job: {jobName}  Classes: {types.Count}");
        var sw = Stopwatch.StartNew();
        var summaries = BenchmarkSwitcher.FromTypes(types.ToArray()).Run(bdnArgs.ToArray(), config).ToList();
        sw.Stop();

        var results = summaries.SelectMany(s => s.Reports.Select(ToRaw)).Select(MapResult).ToList();
        var critical = summaries.Any(s => s.HasCriticalValidationErrors);

        return FinishRun(critical, results, resultsDir, device, () =>
        {
            var git = GitInfo.Read(GitInfo.FindRepoRoot(cwd) ?? cwd, resultsDir);
            var toolkitVersion = ResultStore.CleanVersion(
                typeof(Pixelbadger.Toolkit.Components.StringReverser).Assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? typeof(Pixelbadger.Toolkit.Components.StringReverser).Assembly.GetName().Version?.ToString() ?? "unknown");
            var bdnVersion = typeof(BenchmarkRunner).Assembly.GetName().Version?.ToString(3) ?? "unknown";
            var capturedAt = DateTime.UtcNow;
            capturedAt = new DateTime(capturedAt.Ticks - capturedAt.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);

            return new RunRecord(
                SchemaInfo.CurrentVersion, toolkitVersion, git.Sha, git.Dirty, capturedAt, tier,
                JobInfoFor(jobName), options.Filters.ToList(),
                new RuntimeInfo(RuntimeInformation.FrameworkDescription, bdnVersion, RuntimeInformation.OSDescription),
                Math.Round(sw.Elapsed.TotalSeconds, 1), results);
        }, stdout, stderr);
    }

    /// <summary>BDN-free: decides what to persist after the benchmarks have run, saves the run file and returns the exit code.</summary>
    public static int FinishRun(
        bool criticalValidationErrors, IReadOnlyList<BenchmarkResult> results, string resultsDir, DeviceRecord device,
        Func<RunRecord> buildRun, TextWriter stdout, TextWriter stderr)
    {
        if (criticalValidationErrors)
        {
            stderr.WriteLine("BenchmarkDotNet reported critical validation errors; no run file written.");
            return 1;
        }

        if (results.Count == 0)
        {
            stderr.WriteLine("No benchmarks were run (filter matched nothing in this tier); no run file written.");
            return 1;
        }

        var run = buildRun();
        string relative;
        try
        {
            relative = ResultStore.SaveRun(resultsDir, device, run);
        }
        catch (RunFileExistsException ex)
        {
            stderr.WriteLine(ex.Message);
            return 1;
        }
        catch (DeviceMismatchException ex)
        {
            stderr.WriteLine(ex.Message);
            return 2;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            stderr.WriteLine($"Could not write results under {resultsDir}: {ex.Message}");
            return 1;
        }

        stdout.WriteLine($"Wrote {Path.Combine(resultsDir, relative)}");
        var failed = results.Count(r => !r.Success);
        if (failed > 0)
        {
            stderr.WriteLine($"{failed} benchmark(s) failed.");
            return 1;
        }
        return 0;
    }

    /// <summary>Summary statistics of one successful benchmark, as read from a BDN report.</summary>
    public sealed record RawStats(double Mean, double Median, double StdDev, double Error, double Min, double Max, int N);

    /// <summary>The BDN-free input of <see cref="MapResult"/>. Stats is null when the benchmark failed or produced no statistics.</summary>
    public sealed record RawReport(
        string Class, string Method, string Parameters, bool Success, RawStats? Stats,
        bool HasMemoryDiagnoser, long AllocatedBytesPerOperation,
        long Gen0Collections, long Gen1Collections, long Gen2Collections, long TotalOperations);

    /// <summary>Pure mapping from a raw report to a result row.</summary>
    public static BenchmarkResult MapResult(RawReport r)
    {
        if (!r.Success || r.Stats is null)
            return new BenchmarkResult(r.Class, r.Method, r.Parameters, false, 0, 0, 0, 0, 0, 0, 0, null, 0, 0, 0);

        var s = r.Stats;
        var m = r.HasMemoryDiagnoser;
        return new BenchmarkResult(
            r.Class, r.Method, r.Parameters, true,
            s.Mean, s.Median, s.StdDev, s.Error, s.Min, s.Max, s.N,
            m ? r.AllocatedBytesPerOperation : null,
            m ? PerThousand(r.Gen0Collections, r.TotalOperations) : 0,
            m ? PerThousand(r.Gen1Collections, r.TotalOperations) : 0,
            m ? PerThousand(r.Gen2Collections, r.TotalOperations) : 0);
    }

    private static double PerThousand(long collections, long operations) =>
        operations > 0 ? collections / (double)operations * 1000 : 0;

    /// <summary>Thin adapter from a BDN report; deliberately logic-free.</summary>
    private static RawReport ToRaw(BenchmarkReport report)
    {
        var bc = report.BenchmarkCase;
        var st = report.ResultStatistics;
        var hasMemory = bc.Config.GetDiagnosers().Any(d => d is BenchmarkDotNet.Diagnosers.MemoryDiagnoser);
        var gc = report.GcStats;
        return new RawReport(
            bc.Descriptor.Type.Name, bc.Descriptor.WorkloadMethod.Name, bc.Parameters.DisplayInfo ?? "",
            report.Success,
            st is null ? null : new RawStats(st.Mean, st.Median, st.StandardDeviation, st.ConfidenceInterval.Margin, st.Min, st.Max, st.N),
            hasMemory, hasMemory ? gc.GetBytesAllocatedPerOperation(bc) ?? 0 : 0,
            gc.Gen0Collections, gc.Gen1Collections, gc.Gen2Collections, gc.TotalOperations);
    }
}
