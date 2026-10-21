using Pixelbadger.Toolkit.Benchmarks.Results;

namespace Pixelbadger.Toolkit.Tests.Benchmarks;

internal static class BenchmarkTestData
{
    public const string Slug = "linux-x64-intel-xeon-2-80ghz-4c-16gb";

    public static DeviceFingerprint Fingerprint(int logicalCores = 4) =>
        new("linux", "Ubuntu 24.04", "x64", "intel-xeon-2-80ghz", 2, logicalCores, 16, null);

    public static DeviceRecord Device(string slug = Slug, int logicalCores = 4) =>
        new(1, slug, Fingerprint(logicalCores));

    public static BenchmarkResult Result(string cls = "LevenshteinBenchmarks", string method = "Short", string parameters = "", double mean = 100.5, bool success = true) =>
        new(cls, method, parameters, success, mean, mean, 1.5, 0.5, mean - 2, mean + 2, 15, 128, 0.25, 0, 0);

    public static RunRecord Run(
        string version = "8.2.0", string tier = "standard", string? sha = "0123456789abcdef0123456789abcdef01234567",
        bool? dirty = false, DateTime? at = null, string job = "medium", params BenchmarkResult[] results) =>
        new(1, version, sha, dirty, at ?? new DateTime(2026, 10, 20, 12, 30, 45, DateTimeKind.Utc), tier,
            new JobInfo(job, 2, 10, 15), new[] { "*Levenshtein*" }, new RuntimeInfo(".NET 10.0.0", "0.15.8", "Ubuntu 24.04"), 12.3,
            results.Length > 0 ? results : new[] { Result() });

    public static string RunPath(string root, DeviceRecord device, RunRecord run) =>
        Path.Combine(root, device.Slug, run.ToolkitVersion,
            ResultStore.RunFileName(run.CapturedAtUtc, run.Tier, run.GitSha, run.GitDirty));

    /// <summary>Writes device.json and a run file by hand (bypassing SaveRun) and returns the run file path.</summary>
    public static string Write(string root, DeviceRecord device, RunRecord run)
    {
        var devicePath = ResultStore.DevicePath(root, device.Slug);
        Directory.CreateDirectory(Path.GetDirectoryName(devicePath)!);
        File.WriteAllText(devicePath, ResultsJson.Serialize(device));
        var path = RunPath(root, device, run);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, ResultsJson.Serialize(run));
        return path;
    }
}

public abstract class TempDirTestBase : IDisposable
{
    protected string Root { get; } = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    protected TempDirTestBase() => Directory.CreateDirectory(Root);

    public void Dispose()
    {
        if (Directory.Exists(Root)) Directory.Delete(Root, true);
    }
}
