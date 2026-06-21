using System.Diagnostics;
using FluentAssertions;

namespace Pixelbadger.Toolkit.Tests;

public class DemosceneCommandTests
{
    [Fact]
    public async Task Kefrens_ShouldSucceed_WhenFramesOptionForwarded()
    {
        var (exitCode, _, _) = await RunToolkitCommandAsync("demoscene", "kefrens", "--frames", "1");

        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task Boing_ShouldSucceed_WhenFramesOptionForwarded()
    {
        var (exitCode, _, _) = await RunToolkitCommandAsync("demoscene", "boing", "--frames", "1");

        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task Kefrens_ShouldShowDefaultFrameCount_InHelpText()
    {
        var (exitCode, stdout, stderr) = await RunToolkitCommandAsync("demoscene", "kefrens", "--help");

        exitCode.Should().Be(0);
        (stdout + stderr).Should().Contain("200");
    }

    [Fact]
    public async Task Boing_ShouldShowDefaultFrameCount_InHelpText()
    {
        var (exitCode, stdout, stderr) = await RunToolkitCommandAsync("demoscene", "boing", "--help");

        exitCode.Should().Be(0);
        (stdout + stderr).Should().Contain("200");
    }

    [Fact]
    public async Task Kefrens_ShouldReturnFailure_WhenFramesOptionIsInvalid()
    {
        var (exitCode, _, _) = await RunToolkitCommandAsync("demoscene", "kefrens", "--frames", "notanumber");

        exitCode.Should().NotBe(0);
    }

    [Fact]
    public async Task Boing_ShouldReturnFailure_WhenFramesOptionIsInvalid()
    {
        var (exitCode, _, _) = await RunToolkitCommandAsync("demoscene", "boing", "--frames", "notanumber");

        exitCode.Should().NotBe(0);
    }

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunToolkitCommandAsync(
        params string[] args)
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
        startInfo.ArgumentList.Add("Release");
        startInfo.ArgumentList.Add("--no-build");
        startInfo.ArgumentList.Add("--");

        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        using var process = Process.Start(startInfo)!;
        var standardOutput = await process.StandardOutput.ReadToEndAsync();
        var standardError = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, standardOutput, standardError);
    }

    private static string GetToolkitProjectPath() =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../Pixelbadger.Toolkit/Pixelbadger.Toolkit.csproj"));
}
