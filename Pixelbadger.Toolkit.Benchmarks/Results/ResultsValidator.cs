using System.Globalization;

namespace Pixelbadger.Toolkit.Benchmarks.Results;

public static class ResultsValidator
{
    /// <summary>One message per problem, each starting with the offending relative path.</summary>
    public static IReadOnlyList<string> Validate(string resultsDir)
    {
        var problems = new List<string>();
        if (!Directory.Exists(resultsDir))
        {
            problems.Add($"{resultsDir}: results directory not found");
            return problems;
        }

        foreach (var deviceDir in Directory.GetDirectories(resultsDir).OrderBy(d => d, StringComparer.Ordinal))
        {
            var slug = Path.GetFileName(deviceDir);
            ValidateDevice(resultsDir, deviceDir, slug, problems);

            foreach (var entry in Directory.GetFileSystemEntries(deviceDir).OrderBy(e => e, StringComparer.Ordinal))
            {
                var name = Path.GetFileName(entry);
                if (Directory.Exists(entry))
                {
                    ValidateVersionDir(entry, slug, name, problems);
                }
                else if (name != ResultStore.DeviceFileName && !name.EndsWith(".tmp", StringComparison.Ordinal))
                {
                    problems.Add($"{slug}/{name}: unexpected file in device folder");
                }
            }
        }

        foreach (var tmp in Directory.GetFiles(resultsDir, "*.tmp", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
            problems.Add($"{Path.GetRelativePath(resultsDir, tmp).Replace('\\', '/')}: stray temp file from an interrupted run; delete it");

        foreach (var file in Directory.GetFiles(resultsDir))
        {
            if (!Path.GetFileName(file).EndsWith(".tmp", StringComparison.Ordinal))
                problems.Add($"{Path.GetFileName(file)}: unexpected file in results folder");
        }
        return problems;
    }

    private static void ValidateDevice(string resultsDir, string deviceDir, string slug, List<string> problems)
    {
        var rel = $"{slug}/{ResultStore.DeviceFileName}";
        if (!DeviceIdentity.IsValidDeviceName(slug))
            problems.Add($"{slug}: device folder name is not a valid device slug");
        var path = Path.Combine(deviceDir, ResultStore.DeviceFileName);
        if (!File.Exists(path))
        {
            problems.Add($"{rel}: device.json is missing");
            return;
        }
        try
        {
            var device = ResultStore.ReadJson<DeviceRecord>(path);
            if (device.SchemaVersion != SchemaInfo.CurrentVersion)
                problems.Add($"{rel}: unsupported schemaVersion {device.SchemaVersion}");
            if (device.Slug != slug)
                problems.Add($"{rel}: slug '{device.Slug}' does not match folder '{slug}'");
        }
        catch (InvalidDataException ex)
        {
            problems.Add($"{rel}: {StripPath(ex.Message, path)}");
        }
    }

    private static void ValidateVersionDir(string versionDir, string slug, string version, List<string> problems)
    {
        foreach (var file in Directory.GetFiles(versionDir).OrderBy(f => f, StringComparer.Ordinal))
        {
            var name = Path.GetFileName(file);
            if (name.EndsWith(".tmp", StringComparison.Ordinal)) continue;
            var rel = $"{slug}/{version}/{name}";
            if (!name.EndsWith(".json", StringComparison.Ordinal))
            {
                problems.Add($"{rel}: unexpected file in version folder");
                continue;
            }
            ValidateRunFile(file, rel, version, problems);
        }
        foreach (var dir in Directory.GetDirectories(versionDir))
            problems.Add($"{slug}/{version}/{Path.GetFileName(dir)}: unexpected folder in version folder");
    }

    private static void ValidateRunFile(string path, string rel, string folderVersion, List<string> problems)
    {
        var name = Path.GetFileName(path);
        var match = ResultStore.RunFileNamePattern.Match(name);
        if (!match.Success)
            problems.Add($"{rel}: file name does not match <yyyyMMddTHHmmssZ>-<tier>-<sha7>[-dirty].json");

        RunRecord run;
        try
        {
            run = ResultStore.ReadJson<RunRecord>(path);
        }
        catch (InvalidDataException ex)
        {
            problems.Add($"{rel}: {StripPath(ex.Message, path)}");
            return;
        }

        if (run.SchemaVersion != SchemaInfo.CurrentVersion)
            problems.Add($"{rel}: unsupported schemaVersion {run.SchemaVersion}");
        if (run.ToolkitVersion != folderVersion)
            problems.Add($"{rel}: toolkitVersion '{run.ToolkitVersion}' does not match folder '{folderVersion}'");
        if (!SchemaInfo.Tiers.Contains(run.Tier))
            problems.Add($"{rel}: tier '{run.Tier}' is not standard or slow");
        if (!SchemaInfo.JobNames.Contains(run.Job.Name))
            problems.Add($"{rel}: job name '{run.Job.Name}' is not short, medium or default");
        if (run.Results.Count == 0)
            problems.Add($"{rel}: results is empty");

        if (match.Success)
        {
            var ts = run.CapturedAtUtc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
            if (match.Groups["ts"].Value != ts)
                problems.Add($"{rel}: file name timestamp does not match capturedAtUtc {ts}");
            if (match.Groups["tier"].Value != run.Tier)
                problems.Add($"{rel}: file name tier does not match tier '{run.Tier}'");
            var sha = match.Groups["sha"].Value;
            var expectedSha = run.GitSha is { Length: >= 7 } ? run.GitSha[..7] : "nogit";
            if (sha != expectedSha)
                problems.Add($"{rel}: file name sha '{sha}' does not match gitSha");
            if (match.Groups["dirty"].Success != (run.GitDirty == true))
                problems.Add($"{rel}: file name dirty marker does not match gitDirty");
        }

        foreach (var r in run.Results)
        {
            var id = $"{r.Class}.{r.Method}({r.Parameters})";
            if (string.IsNullOrWhiteSpace(r.Class) || string.IsNullOrWhiteSpace(r.Method))
                problems.Add($"{rel}: result with empty class or method");
            var stats = new[] { r.MeanNs, r.MedianNs, r.StdDevNs, r.ErrorNs, r.MinNs, r.MaxNs, r.Gen0, r.Gen1, r.Gen2 };
            if (stats.Any(v => double.IsNaN(v) || double.IsInfinity(v) || v < 0))
                problems.Add($"{rel}: result {id} has a negative or non-finite statistic");
        }
    }

    private static string StripPath(string message, string path) =>
        message.StartsWith(path + ": ", StringComparison.Ordinal) ? message[(path.Length + 2)..] : message;
}
