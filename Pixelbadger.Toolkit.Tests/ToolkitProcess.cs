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

    public static Process Start(string? workingDirectory, params string[] args)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

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
        using var process = Start(workingDirectory, args);
        var standardOutput = await process.StandardOutput.ReadToEndAsync();
        var standardError = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, standardOutput, standardError);
    }
}
