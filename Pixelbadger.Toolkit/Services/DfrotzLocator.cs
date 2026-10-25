namespace Pixelbadger.Toolkit.Services;

/// <summary>Finds the dfrotz executable.</summary>
public static class DfrotzLocator
{
    private const string FallbackPath = "/usr/games/dfrotz";

    public static string? Resolve(string? configured, string? pathVariable = null) =>
        Resolve(configured, pathVariable, FallbackPath);

    internal static string? Resolve(string? configured, string? pathVariable, string fallbackPath)
    {
        if (configured != null)
        {
            var full = Path.GetFullPath(configured);
            return File.Exists(full) ? full : null;
        }

        var path = pathVariable ?? Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), "dfrotz");
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }
            catch (ArgumentException)
            {
                // Malformed PATH entry; skip it.
            }
        }

        return File.Exists(fallbackPath) ? fallbackPath : null;
    }
}
