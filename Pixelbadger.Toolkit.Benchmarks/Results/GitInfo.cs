using System.Diagnostics;

namespace Pixelbadger.Toolkit.Benchmarks.Results;

public sealed record GitState(string? Sha, bool? Dirty);

public static class GitInfo
{
    /// <summary>Nearest ancestor of startDir containing Pixelbadger.Toolkit.sln, or null.</summary>
    public static string? FindRepoRoot(string startDir)
    {
        var dir = new DirectoryInfo(startDir);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Pixelbadger.Toolkit.sln"))) return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    public static GitState Read(string workingDir, string resultsDir)
    {
        var sha = RunGit(workingDir, "rev-parse HEAD")?.Trim();
        if (string.IsNullOrEmpty(sha)) return new GitState(null, null);
        var top = RunGit(workingDir, "rev-parse --show-toplevel")?.Trim() ?? workingDir;
        var status = RunGit(workingDir, "status --porcelain --untracked-files=all");
        if (status is null) return new GitState(sha, null);
        return new GitState(sha, IsDirty(status, top, resultsDir));
    }

    /// <summary>Pure: true if any porcelain entry lies outside resultsDir.</summary>
    public static bool IsDirty(string porcelain, string repoTop, string resultsDir)
    {
        var results = Path.GetFullPath(resultsDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var line in porcelain.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length < 4) continue;
            var path = line[3..].Trim();
            var arrow = path.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrow >= 0) path = path[(arrow + 4)..];
            path = path.Trim('"');
            var full = Path.GetFullPath(Path.Combine(repoTop, path));
            var candidate = full.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!candidate.StartsWith(results, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private static string? RunGit(string workingDir, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo("git", arguments)
            {
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var p = Process.Start(psi);
            if (p is null) return null;
            var output = p.StandardOutput.ReadToEnd();
            p.StandardError.ReadToEnd();
            p.WaitForExit();
            return p.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }
}
