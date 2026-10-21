namespace Pixelbadger.Toolkit.Benchmarks.Results;

public sealed record DeviceFingerprint(string Os, string OsDescription, string Arch, string Cpu, int PhysicalCores, int LogicalCores, int MemoryGb, string? Hypervisor);

public sealed record DeviceRecord(int SchemaVersion, string Slug, DeviceFingerprint Fingerprint);

/// <summary>Name is "short", "medium" or "default". Counts are 0 where BenchmarkDotNet chooses them automatically.</summary>
public sealed record JobInfo(string Name, int LaunchCount, int WarmupCount, int IterationCount);

public sealed record RuntimeInfo(string Dotnet, string Bdn, string OsDescription);

public sealed record BenchmarkResult(
    string Class, string Method, string Parameters, bool Success,
    double MeanNs, double MedianNs, double StdDevNs, double ErrorNs, double MinNs, double MaxNs,
    int N, long? AllocatedBytes, double Gen0, double Gen1, double Gen2);

public sealed record RunRecord(
    int SchemaVersion, string ToolkitVersion, string? GitSha, bool? GitDirty, DateTime CapturedAtUtc,
    string Tier, JobInfo Job, IReadOnlyList<string> Filters, RuntimeInfo Runtime, double TotalSeconds,
    IReadOnlyList<BenchmarkResult> Results);

/// <summary>RelativePath uses '/' separators and is relative to the results directory.</summary>
public sealed record StoredRun(string RelativePath, string DeviceSlug, string Version, DeviceRecord Device, RunRecord Run);

public static class SchemaInfo
{
    public const int CurrentVersion = 1;
    public static readonly string[] Tiers = { "standard", "slow" };
    public static readonly string[] JobNames = { "short", "medium", "default" };
}
