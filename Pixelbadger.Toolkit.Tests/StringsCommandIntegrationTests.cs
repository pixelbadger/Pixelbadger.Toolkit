using FluentAssertions;
using System.Diagnostics;

namespace Pixelbadger.Toolkit.Tests;

public class StringsCommandIntegrationTests : IDisposable
{
    private readonly string _testDirectory;

    public StringsCommandIntegrationTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    [Fact]
    public async Task DotnetRun_ShouldReturnSuccess_WhenFleschReadingEaseInputFileIsProvided()
    {
        var inputFile = Path.Combine(_testDirectory, "input.txt");
        await File.WriteAllTextAsync(inputFile, "The cat sat on the mat.");

        var (exitCode, standardOutput, _) = await RunToolkitCommandAsync(
            "strings",
            "flesch-reading-ease",
            "--in-file",
            inputFile);

        exitCode.Should().Be(0);
        standardOutput.Should().Contain("Flesch Reading Ease:");
        standardOutput.Should().Contain("Readability:");
    }

    [Fact]
    public async Task DotnetRun_ShouldReturnFailure_WhenFleschReadingEaseInputFileOptionIsMissing()
    {
        var (exitCode, standardOutput, standardError) = await RunToolkitCommandAsync(
            "strings",
            "flesch-reading-ease");

        exitCode.Should().NotBe(0);
        (standardOutput + standardError).Should().Contain("Option '--in-file' is required");
    }

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunToolkitCommandAsync(params string[] args)
    {
        var projectPath = GetToolkitProjectPath();
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("Debug");
        startInfo.ArgumentList.Add("--no-build");
        startInfo.ArgumentList.Add("--");

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo)!;
        var standardOutput = await process.StandardOutput.ReadToEndAsync();
        var standardError = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        return (process.ExitCode, standardOutput, standardError);
    }

    private static string GetToolkitProjectPath()
    {
        return Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "../../../../Pixelbadger.Toolkit/Pixelbadger.Toolkit.csproj"));
    }
}
