using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Pixelbadger.Toolkit.Benchmarks.Results;

public sealed class DeviceMismatchException(string slug, IReadOnlyList<string> fields)
    : Exception($"Device '{slug}' has a different fingerprint than recorded in device.json (differing: {string.Join(", ", fields)}). " +
                "Use --device <new-name> to record this machine under a separate device.")
{
    public IReadOnlyList<string> Fields { get; } = fields;
}

public sealed class RunFileExistsException(string path)
    : IOException($"Run file already exists: {path}");

public static class ResultStore
{
    public const string DeviceFileName = "device.json";

    public static readonly Regex RunFileNamePattern = new(
        @"^(?<ts>\d{8}T\d{6}Z)-(?<tier>standard|slow)-(?<sha>[0-9a-f]{7}|nogit)(?<dirty>-dirty)?\.json$", RegexOptions.Compiled);

    public static string RunFileName(DateTime capturedAtUtc, string tier, string? gitSha, bool? gitDirty)
    {
        var sha7 = gitSha is { Length: >= 7 } ? gitSha[..7].ToLowerInvariant() : "nogit";
        var dirty = gitDirty == true ? "-dirty" : "";
        var ts = capturedAtUtc.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        return $"{ts}-{tier}-{sha7}{dirty}.json";
    }

    public static string RunRelativePath(string deviceSlug, string version, string fileName) =>
        $"{deviceSlug}/{version}/{fileName}";

    /// <summary>Toolkit version with any "+..." metadata suffix removed.</summary>
    public static string CleanVersion(string informationalVersion)
    {
        var plus = informationalVersion.IndexOf('+');
        return plus >= 0 ? informationalVersion[..plus] : informationalVersion;
    }

    public static string DevicePath(string resultsDir, string slug) => Path.Combine(resultsDir, slug, DeviceFileName);

    /// <summary>Reads an existing device.json for the slug, or null when none exists.</summary>
    public static DeviceRecord? ReadDevice(string resultsDir, string slug)
    {
        var path = DevicePath(resultsDir, slug);
        if (!File.Exists(path)) return null;
        return ReadJson<DeviceRecord>(path);
    }

    /// <summary>Throws <see cref="DeviceMismatchException"/> when device.json exists and its fingerprint differs. Writes nothing.</summary>
    public static void CheckDevice(string resultsDir, DeviceRecord device)
    {
        var existing = ReadDevice(resultsDir, device.Slug);
        if (existing is null) return;
        var diffs = DeviceIdentity.DifferingFields(existing.Fingerprint, device.Fingerprint);
        if (diffs.Count > 0) throw new DeviceMismatchException(device.Slug, diffs);
    }

    /// <summary>
    /// Writes device.json if absent, then the run file (temp file + rename). Never overwrites: a name collision throws
    /// <see cref="RunFileExistsException"/>. Returns the run file's relative path.
    /// </summary>
    public static string SaveRun(string resultsDir, DeviceRecord device, RunRecord run)
    {
        CheckDevice(resultsDir, device);
        var fileName = RunFileName(run.CapturedAtUtc, run.Tier, run.GitSha, run.GitDirty);
        var relative = RunRelativePath(device.Slug, run.ToolkitVersion, fileName);
        var versionDir = Path.Combine(resultsDir, device.Slug, run.ToolkitVersion);
        Directory.CreateDirectory(versionDir);

        var target = Path.Combine(versionDir, fileName);
        if (File.Exists(target)) throw new RunFileExistsException(target);

        if (ReadDevice(resultsDir, device.Slug) is null)
            WriteNew(DevicePath(resultsDir, device.Slug), ResultsJson.Serialize(device) + "\n", overwrite: false);

        WriteNew(target, ResultsJson.Serialize(run) + "\n", overwrite: false);
        return relative;
    }

    /// <summary>Creates the results directory and proves it is writable. Throws IOException/UnauthorizedAccessException otherwise.</summary>
    public static void EnsureWritable(string resultsDir)
    {
        Directory.CreateDirectory(resultsDir);
        var probe = Path.Combine(resultsDir, $".probe-{Guid.NewGuid():N}.tmp");
        File.WriteAllText(probe, "");
        File.Delete(probe);
    }

    private static void WriteNew(string path, string content, bool overwrite)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temp, content, new System.Text.UTF8Encoding(false));
            try { File.Move(temp, path, overwrite); }
            catch (IOException) when (!overwrite && File.Exists(path)) { throw new RunFileExistsException(path); }
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    public static T ReadJson<T>(string path)
    {
        try
        {
            var value = JsonSerializer.Deserialize<T>(File.ReadAllText(path), ResultsJson.Options);
            return value ?? throw new InvalidDataException($"{path}: file is empty or null");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{path}: {ex.Message}", ex);
        }
    }

    /// <summary>Candidate run files as (device slug, version, absolute path), ordered ordinal by relative path. Ignores *.tmp files.</summary>
    internal static IEnumerable<(string Slug, string Version, string FullPath, string Relative)> EnumerateRunFiles(string resultsDir)
    {
        if (!Directory.Exists(resultsDir)) yield break;
        var items = new List<(string, string, string, string)>();
        foreach (var deviceDir in Directory.GetDirectories(resultsDir))
        {
            var slug = Path.GetFileName(deviceDir);
            foreach (var versionDir in Directory.GetDirectories(deviceDir))
            {
                var version = Path.GetFileName(versionDir);
                foreach (var file in Directory.GetFiles(versionDir, "*.json"))
                    items.Add((slug, version, file, $"{slug}/{version}/{Path.GetFileName(file)}"));
            }
        }
        foreach (var item in items.OrderBy(i => i.Item4, StringComparer.Ordinal)) yield return item;
    }

    public static IReadOnlyList<StoredRun> LoadAll(string resultsDir)
    {
        var devices = new Dictionary<string, DeviceRecord>(StringComparer.Ordinal);
        var runs = new List<StoredRun>();
        foreach (var (slug, version, fullPath, relative) in EnumerateRunFiles(resultsDir))
        {
            if (!devices.TryGetValue(slug, out var device))
            {
                var devicePath = DevicePath(resultsDir, slug);
                if (!File.Exists(devicePath))
                    throw new InvalidDataException($"{slug}/{DeviceFileName}: device.json is missing");
                device = ReadJson<DeviceRecord>(devicePath);
                devices[slug] = device;
            }
            var run = ReadJson<RunRecord>(fullPath);
            runs.Add(new StoredRun(relative, slug, version, device, run));
        }
        return runs;
    }
}
