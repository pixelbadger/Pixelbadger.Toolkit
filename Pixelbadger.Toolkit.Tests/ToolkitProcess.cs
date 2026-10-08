using System.Diagnostics;

namespace Pixelbadger.Toolkit.Tests;

internal static class ToolkitProcess
{
    // AppContext.BaseDirectory is <project>/bin/<Configuration>/<tfm>/, so the configuration
    // the tests were built with is the name of the parent directory.
    private static readonly string BuildConfiguration =
        new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)).Parent!.Name;

    private static string ProjectPath { get; } =
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../Pixelbadger.Toolkit/Pixelbadger.Toolkit.csproj"));

    /// <summary>
    /// Starts the toolkit with a fresh, empty per-user config home (XDG_CONFIG_HOME and APPDATA) that is deleted
    /// when the process exits.
    /// </summary>
    public static Process Start(string? workingDirectory, params string[] args)
    {
        var configHome = Path.Combine(Path.GetTempPath(), "pbtk-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(configHome);

        var process = StartWithConfigHome(workingDirectory, configHome, args);
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => DeleteQuietly(configHome);
        if (process.HasExited)
            DeleteQuietly(configHome);
        return process;
    }

    /// <summary>Starts the toolkit using the caller-supplied config home, which is left in place for reuse across calls.</summary>
    public static Process StartWithConfigHome(string? workingDirectory, string configHome, params string[] args)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.Environment["XDG_CONFIG_HOME"] = configHome;
        startInfo.Environment["APPDATA"] = configHome;

        if (workingDirectory is not null)
            startInfo.WorkingDirectory = workingDirectory;

        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(ProjectPath);
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(BuildConfiguration);
        startInfo.ArgumentList.Add("--no-build");
        startInfo.ArgumentList.Add("--");

        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        return Process.Start(startInfo)!;
    }

    public static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunAsync(
        string? workingDirectory,
        params string[] args)
    {
        var configHome = Path.Combine(Path.GetTempPath(), "pbtk-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(configHome);
        try
        {
            return await RunWithConfigHomeAsync(workingDirectory, configHome, args);
        }
        finally
        {
            DeleteQuietly(configHome);
        }
    }

    /// <summary>Runs the toolkit to completion using the caller-supplied config home, which is left in place for reuse across calls.</summary>
    public static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunWithConfigHomeAsync(
        string? workingDirectory,
        string configHome,
        params string[] args)
    {
        using var process = StartWithConfigHome(workingDirectory, configHome, args);
        var standardOutput = await process.StandardOutput.ReadToEndAsync();
        var standardError = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, standardOutput, standardError);
    }

    private static void DeleteQuietly(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
        catch (Exception)
        {
            // Best-effort cleanup of a temp directory.
        }
    }
}
