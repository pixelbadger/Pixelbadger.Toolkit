namespace Pixelbadger.Toolkit.Benchmarks.Results;

public sealed record RunOptions(string? Device, string? Job, bool Slow, IReadOnlyList<string> Filters, string? ResultsDir)
{
    /// <summary>The job name actually used: explicit --job, else short for --slow and medium otherwise.</summary>
    public string EffectiveJob => Job ?? (Slow ? "short" : "medium");

    /// <summary>Parses the args after "run". Returns null and an error message on a usage error.</summary>
    public static RunOptions? Parse(IReadOnlyList<string> args, out string? error)
    {
        string? device = null, job = null, resultsDir = null;
        var slow = false;
        var filters = new List<string>();
        error = null;

        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--slow":
                    slow = true;
                    break;
                case "--device" or "--job" or "--results-dir":
                    if (i + 1 >= args.Count) { error = $"{args[i]} requires a value"; return null; }
                    var value = args[++i];
                    if (args[i - 1] == "--device") device = value;
                    else if (args[i - 1] == "--job") job = value;
                    else resultsDir = value;
                    break;
                case "--filter":
                    var start = filters.Count;
                    while (i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                        filters.Add(args[++i]);
                    if (filters.Count == start) { error = "--filter requires at least one pattern"; return null; }
                    break;
                default:
                    error = $"Unknown option '{args[i]}' for run";
                    return null;
            }
        }

        if (job is not null && !SchemaInfo.JobNames.Contains(job))
        {
            error = $"--job must be one of: {string.Join(", ", SchemaInfo.JobNames)}";
            return null;
        }
        return new RunOptions(device, job, slow, filters, resultsDir);
    }
}
