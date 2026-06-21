using System.Diagnostics;
using FluentAssertions;

namespace Pixelbadger.Toolkit.Tests;

public class MarkovCommandTests : IDisposable
{
    private readonly string _testDirectory;

    public MarkovCommandTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
            Directory.Delete(_testDirectory, true);
    }

    [Fact]
    public async Task Train_ShouldSucceed_WhenValidSourceFileProvided()
    {
        var sourceFile = Path.Combine(_testDirectory, "corpus.txt");
        await File.WriteAllTextAsync(sourceFile, "the cat sat on the mat the cat ran");

        var (exitCode, stdout, _) = await RunToolkitCommandAsync(_testDirectory, "markov", "train", "--source", sourceFile);

        exitCode.Should().Be(0);
        stdout.Should().Contain("Model trained successfully");
        stdout.Should().Contain(Path.GetFileName(sourceFile));
    }

    [Fact]
    public async Task Train_ShouldForwardSourceOptionToComponent()
    {
        var sourceFile = Path.Combine(_testDirectory, "input.txt");
        await File.WriteAllTextAsync(sourceFile, "hello world foo bar");

        var (exitCode, stdout, _) = await RunToolkitCommandAsync(_testDirectory, "markov", "train", "--source", sourceFile);

        exitCode.Should().Be(0);
        stdout.Should().Contain(sourceFile);
    }

    [Fact]
    public async Task Train_ShouldReturnFailure_WhenSourceFileDoesNotExist()
    {
        var missingFile = Path.Combine(_testDirectory, "missing.txt");

        var (exitCode, stdout, _) = await RunToolkitCommandAsync(_testDirectory, "markov", "train", "--source", missingFile);

        exitCode.Should().Be(1);
        stdout.Should().Contain("Error:");
    }

    [Fact]
    public async Task Train_ShouldReturnFailure_WhenSourceOptionIsMissing()
    {
        var (exitCode, stdout, stderr) = await RunToolkitCommandAsync(_testDirectory, "markov", "train");

        exitCode.Should().NotBe(0);
        (stdout + stderr).Should().Contain("--source");
    }

    [Fact]
    public async Task Complete_ShouldForwardTextAndCountOptionsToComponent()
    {
        var sourceFile = Path.Combine(_testDirectory, "corpus.txt");
        await File.WriteAllTextAsync(sourceFile, "the cat sat on the mat the cat ran away");

        await RunToolkitCommandAsync(_testDirectory, "markov", "train", "--source", sourceFile);

        var (exitCode, stdout, _) = await RunToolkitCommandAsync(_testDirectory, "markov", "complete", "--text", "the", "--count", "3");

        exitCode.Should().Be(0);
        stdout.Trim().Should().NotBeEmpty();
    }

    [Fact]
    public async Task Complete_ShouldUseDefaultCount_WhenCountNotSpecified()
    {
        var sourceFile = Path.Combine(_testDirectory, "corpus.txt");
        var words = string.Join(" ", Enumerable.Range(0, 200).Select(i => $"word{i % 10}"));
        await File.WriteAllTextAsync(sourceFile, words);

        await RunToolkitCommandAsync(_testDirectory, "markov", "train", "--source", sourceFile);

        var (exitCode, stdout, _) = await RunToolkitCommandAsync(_testDirectory, "markov", "complete", "--text", "word0");

        exitCode.Should().Be(0);
        stdout.Trim().Should().NotBeEmpty();
    }

    [Fact]
    public async Task Complete_ShouldReturnFailure_WhenModelDoesNotExist()
    {
        var (exitCode, stdout, _) = await RunToolkitCommandAsync(_testDirectory, "markov", "complete", "--text", "hello");

        exitCode.Should().Be(1);
        stdout.Should().Contain("Error:");
    }

    [Fact]
    public async Task Complete_ShouldReturnFailure_WhenTextOptionIsMissing()
    {
        var (exitCode, stdout, stderr) = await RunToolkitCommandAsync(_testDirectory, "markov", "complete");

        exitCode.Should().NotBe(0);
        (stdout + stderr).Should().Contain("--text");
    }

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunToolkitCommandAsync(
        string workingDirectory,
        params string[] args)
    {
        var projectPath = GetToolkitProjectPath();
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory
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
