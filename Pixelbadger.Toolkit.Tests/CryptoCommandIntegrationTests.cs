using System.Diagnostics;
using FluentAssertions;

namespace Pixelbadger.Toolkit.Tests;

public class CryptoCommandIntegrationTests : IDisposable
{
    private readonly string _testDirectory;

    public CryptoCommandIntegrationTests()
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
    public async Task GenerateKey_ShouldCreateKeyFile_WhenKeyFileOptionProvided()
    {
        var keyFile = Path.Combine(_testDirectory, "test.key");

        var (exitCode, stdout, _) = await RunToolkitCommandAsync(
            "crypto", "generate-key", "--key-file", keyFile);

        exitCode.Should().Be(0);
        stdout.Should().Contain("Key pair written to");
        File.Exists(keyFile).Should().BeTrue();
    }

    [Fact]
    public async Task Encrypt_ShouldCreateEncryptedFile_WhenValidOptionsProvided()
    {
        var keyFile = Path.Combine(_testDirectory, "test.key");
        var encFile = Path.Combine(_testDirectory, "number.enc");

        await RunToolkitCommandAsync("crypto", "generate-key", "--key-file", keyFile);

        var (exitCode, stdout, _) = await RunToolkitCommandAsync(
            "crypto", "encrypt", "--number", "42", "--key-file", keyFile, "--out-file", encFile);

        exitCode.Should().Be(0);
        stdout.Should().Contain("Encrypted number written to");
        File.Exists(encFile).Should().BeTrue();
    }

    [Fact]
    public async Task Decrypt_ShouldReturnOriginalPlaintext_WhenDecryptingEncryptedNumber()
    {
        var keyFile = Path.Combine(_testDirectory, "test.key");
        var encFile = Path.Combine(_testDirectory, "number.enc");

        await RunToolkitCommandAsync("crypto", "generate-key", "--key-file", keyFile);
        await RunToolkitCommandAsync("crypto", "encrypt", "--number", "99", "--key-file", keyFile, "--out-file", encFile);

        var (exitCode, stdout, _) = await RunToolkitCommandAsync(
            "crypto", "decrypt", "--in-file", encFile, "--key-file", keyFile);

        exitCode.Should().Be(0);
        stdout.Trim().Should().Be("99");
    }

    [Fact]
    public async Task Add_ShouldProduceEncryptedSumThatDecryptsToCorrectValue()
    {
        var keyFile = Path.Combine(_testDirectory, "test.key");
        var encFile1 = Path.Combine(_testDirectory, "a.enc");
        var encFile2 = Path.Combine(_testDirectory, "b.enc");
        var sumFile = Path.Combine(_testDirectory, "sum.enc");

        await RunToolkitCommandAsync("crypto", "generate-key", "--key-file", keyFile);
        await RunToolkitCommandAsync("crypto", "encrypt", "--number", "37", "--key-file", keyFile, "--out-file", encFile1);
        await RunToolkitCommandAsync("crypto", "encrypt", "--number", "5", "--key-file", keyFile, "--out-file", encFile2);
        await RunToolkitCommandAsync("crypto", "add", "--in-file1", encFile1, "--in-file2", encFile2, "--out-file", sumFile);

        var (exitCode, stdout, _) = await RunToolkitCommandAsync(
            "crypto", "decrypt", "--in-file", sumFile, "--key-file", keyFile);

        exitCode.Should().Be(0);
        stdout.Trim().Should().Be("42");
    }

    [Fact]
    public async Task GenerateKey_ShouldReturnFailure_WhenKeyFileOptionIsMissing()
    {
        var (exitCode, stdout, stderr) = await RunToolkitCommandAsync("crypto", "generate-key");

        exitCode.Should().NotBe(0);
        (stdout + stderr).Should().Contain("--key-file");
    }

    [Fact]
    public async Task Encrypt_ShouldReturnFailure_WhenKeyFileDoesNotExist()
    {
        var missingKeyFile = Path.Combine(_testDirectory, "missing.key");
        var encFile = Path.Combine(_testDirectory, "number.enc");

        var (exitCode, stdout, _) = await RunToolkitCommandAsync(
            "crypto", "encrypt", "--number", "42", "--key-file", missingKeyFile, "--out-file", encFile);

        exitCode.Should().Be(1);
        stdout.Should().Contain("Error:");
    }

    [Fact]
    public async Task Decrypt_ShouldReturnFailure_WhenKeyFileContainsMalformedJson()
    {
        var keyFile = Path.Combine(_testDirectory, "bad.key");
        var encFile = Path.Combine(_testDirectory, "number.enc");

        await File.WriteAllTextAsync(keyFile, "{ not valid json }");
        await File.WriteAllTextAsync(encFile, "{ \"Ciphertext\": \"12345\", \"N\": \"99999\" }");

        var (exitCode, stdout, _) = await RunToolkitCommandAsync(
            "crypto", "decrypt", "--in-file", encFile, "--key-file", keyFile);

        exitCode.Should().Be(1);
        stdout.Should().Contain("Error:");
    }

    [Fact]
    public async Task Add_ShouldReturnFailure_WhenFirstInputFileDoesNotExist()
    {
        var keyFile = Path.Combine(_testDirectory, "test.key");
        var missingFile = Path.Combine(_testDirectory, "missing.enc");
        var encFile = Path.Combine(_testDirectory, "number.enc");
        var sumFile = Path.Combine(_testDirectory, "sum.enc");

        await RunToolkitCommandAsync("crypto", "generate-key", "--key-file", keyFile);
        await RunToolkitCommandAsync("crypto", "encrypt", "--number", "5", "--key-file", keyFile, "--out-file", encFile);

        var (exitCode, stdout, _) = await RunToolkitCommandAsync(
            "crypto", "add", "--in-file1", missingFile, "--in-file2", encFile, "--out-file", sumFile);

        exitCode.Should().Be(1);
        stdout.Should().Contain("Error:");
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
