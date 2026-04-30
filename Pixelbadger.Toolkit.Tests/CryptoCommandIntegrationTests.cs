using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using FluentAssertions;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Models;

namespace Pixelbadger.Toolkit.Tests;

public class CryptoCommandIntegrationTests : IDisposable
{
    private readonly string _testDirectory;
    private static readonly Lazy<PaillierKeyPair> TestKey = new(() => new HomomorphicEncryptionComponent().GenerateKey());
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

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
    public async Task GenerateKey_ShouldCreatePublicAndPrivateKeyFiles_WhenBothOptionsProvided()
    {
        var publicKeyFile = Path.Combine(_testDirectory, "test.pub");
        var privateKeyFile = Path.Combine(_testDirectory, "test.key");

        var (exitCode, stdout, _) = await RunToolkitCommandAsync(
            "crypto", "generate-key",
            "--public-key-file", publicKeyFile,
            "--private-key-file", privateKeyFile);

        exitCode.Should().Be(0);
        stdout.Should().Contain("Public key written to");
        stdout.Should().Contain("Private key written to");
        File.Exists(publicKeyFile).Should().BeTrue();
        File.Exists(privateKeyFile).Should().BeTrue();

        var publicKey = JsonSerializer.Deserialize<PaillierPublicKey>(await File.ReadAllTextAsync(publicKeyFile));
        BigInteger.Parse(publicKey!.N).GetBitLength().Should().BeGreaterThanOrEqualTo(HomomorphicEncryptionComponent.MinimumKeyBitLength);

        if (!OperatingSystem.IsWindows())
        {
            File.GetUnixFileMode(privateKeyFile).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact]
    public async Task Encrypt_ShouldCreateEncryptedFile_WhenValidOptionsProvided()
    {
        var publicKeyFile = Path.Combine(_testDirectory, "test.pub");
        var privateKeyFile = Path.Combine(_testDirectory, "test.key");
        var encFile = Path.Combine(_testDirectory, "number.enc");

        await WriteKeyFilesAsync(publicKeyFile, privateKeyFile);

        var (exitCode, stdout, _) = await RunToolkitCommandAsync(
            "crypto", "encrypt", "--number", "42", "--public-key-file", publicKeyFile, "--out-file", encFile);

        exitCode.Should().Be(0);
        stdout.Should().Contain("Encrypted number written to");
        File.Exists(encFile).Should().BeTrue();
    }

    [Fact]
    public async Task Decrypt_ShouldReturnOriginalPlaintext_WhenDecryptingEncryptedNumber()
    {
        var publicKeyFile = Path.Combine(_testDirectory, "test.pub");
        var privateKeyFile = Path.Combine(_testDirectory, "test.key");
        var encFile = Path.Combine(_testDirectory, "number.enc");

        await WriteKeyFilesAsync(publicKeyFile, privateKeyFile);
        await RunToolkitCommandAsync("crypto", "encrypt",
            "--number", "99", "--public-key-file", publicKeyFile, "--out-file", encFile);

        var (exitCode, stdout, _) = await RunToolkitCommandAsync(
            "crypto", "decrypt", "--in-file", encFile, "--private-key-file", privateKeyFile);

        exitCode.Should().Be(0);
        stdout.Trim().Should().Be("99");
    }

    [Fact]
    public async Task Add_ShouldProduceEncryptedSumThatDecryptsToCorrectValue()
    {
        var publicKeyFile = Path.Combine(_testDirectory, "test.pub");
        var privateKeyFile = Path.Combine(_testDirectory, "test.key");
        var encFile1 = Path.Combine(_testDirectory, "a.enc");
        var encFile2 = Path.Combine(_testDirectory, "b.enc");
        var sumFile = Path.Combine(_testDirectory, "sum.enc");

        await WriteKeyFilesAsync(publicKeyFile, privateKeyFile);
        await RunToolkitCommandAsync("crypto", "encrypt",
            "--number", "37", "--public-key-file", publicKeyFile, "--out-file", encFile1);
        await RunToolkitCommandAsync("crypto", "encrypt",
            "--number", "5", "--public-key-file", publicKeyFile, "--out-file", encFile2);
        await RunToolkitCommandAsync("crypto", "add",
            "--in-file1", encFile1, "--in-file2", encFile2, "--out-file", sumFile);

        var (exitCode, stdout, _) = await RunToolkitCommandAsync(
            "crypto", "decrypt", "--in-file", sumFile, "--private-key-file", privateKeyFile);

        exitCode.Should().Be(0);
        stdout.Trim().Should().Be("42");
    }

    [Fact]
    public async Task GenerateKey_ShouldWriteOnlyNToPublicKeyFile_AndNotExposePrivateKeyMaterial()
    {
        var publicKeyFile = Path.Combine(_testDirectory, "test.pub");
        var privateKeyFile = Path.Combine(_testDirectory, "test.key");

        await WriteKeyFilesAsync(publicKeyFile, privateKeyFile);

        var publicJson = await File.ReadAllTextAsync(publicKeyFile);
        publicJson.Should().Contain("\"N\"");
        publicJson.Should().NotContain("\"Lambda\"");
        publicJson.Should().NotContain("\"Mu\"");
    }

    [Fact]
    public async Task GenerateKey_ShouldReturnFailure_WhenPublicKeyFileOptionIsMissing()
    {
        var privateKeyFile = Path.Combine(_testDirectory, "test.key");

        var (exitCode, stdout, stderr) = await RunToolkitCommandAsync(
            "crypto", "generate-key", "--private-key-file", privateKeyFile);

        exitCode.Should().NotBe(0);
        (stdout + stderr).Should().Contain("--public-key-file");
    }

    [Fact]
    public async Task GenerateKey_ShouldReturnFailure_WhenPrivateKeyFileOptionIsMissing()
    {
        var publicKeyFile = Path.Combine(_testDirectory, "test.pub");

        var (exitCode, stdout, stderr) = await RunToolkitCommandAsync(
            "crypto", "generate-key", "--public-key-file", publicKeyFile);

        exitCode.Should().NotBe(0);
        (stdout + stderr).Should().Contain("--private-key-file");
    }

    [Fact]
    public async Task Encrypt_ShouldReturnFailure_WhenPublicKeyFileDoesNotExist()
    {
        var missingPublicKeyFile = Path.Combine(_testDirectory, "missing.pub");
        var encFile = Path.Combine(_testDirectory, "number.enc");

        var (exitCode, stdout, _) = await RunToolkitCommandAsync(
            "crypto", "encrypt", "--number", "42", "--public-key-file", missingPublicKeyFile, "--out-file", encFile);

        exitCode.Should().Be(1);
        stdout.Should().Contain("Error:");
    }

    [Fact]
    public async Task Decrypt_ShouldReturnFailure_WhenPrivateKeyFileContainsMalformedJson()
    {
        var privateKeyFile = Path.Combine(_testDirectory, "bad.key");
        var encFile = Path.Combine(_testDirectory, "number.enc");

        await File.WriteAllTextAsync(privateKeyFile, "{ not valid json }");
        await File.WriteAllTextAsync(encFile, "{ \"Ciphertext\": \"12345\", \"N\": \"99999\" }");

        var (exitCode, stdout, _) = await RunToolkitCommandAsync(
            "crypto", "decrypt", "--in-file", encFile, "--private-key-file", privateKeyFile);

        exitCode.Should().Be(1);
        stdout.Should().Contain("Error:");
    }

    [Fact]
    public async Task EncryptString_ThenDecryptString_ShouldReturnOriginalString()
    {
        var publicKeyFile = Path.Combine(_testDirectory, "test.pub");
        var privateKeyFile = Path.Combine(_testDirectory, "test.key");
        var encFile = Path.Combine(_testDirectory, "msg.estr");

        await WriteKeyFilesAsync(publicKeyFile, privateKeyFile);
        await RunToolkitCommandAsync("crypto", "encrypt-string",
            "--string", "hello world", "--public-key-file", publicKeyFile, "--out-file", encFile);

        var (exitCode, stdout, _) = await RunToolkitCommandAsync(
            "crypto", "decrypt-string", "--in-file", encFile, "--private-key-file", privateKeyFile);

        exitCode.Should().Be(0);
        stdout.Trim().Should().Be("hello world");
    }

    [Fact]
    public async Task Replace_ShouldProduceUpdatedStringAfterDecryption()
    {
        var publicKeyFile = Path.Combine(_testDirectory, "test.pub");
        var privateKeyFile = Path.Combine(_testDirectory, "test.key");
        var encFile = Path.Combine(_testDirectory, "msg.estr");
        var updatedFile = Path.Combine(_testDirectory, "updated.estr");

        await WriteKeyFilesAsync(publicKeyFile, privateKeyFile);
        await RunToolkitCommandAsync("crypto", "encrypt-string",
            "--string", "hello world", "--public-key-file", publicKeyFile, "--out-file", encFile);
        await RunToolkitCommandAsync("crypto", "replace",
            "--in-file", encFile, "--start", "6", "--replacement", "there", "--out-file", updatedFile);

        var (exitCode, stdout, _) = await RunToolkitCommandAsync(
            "crypto", "decrypt-string", "--in-file", updatedFile, "--private-key-file", privateKeyFile);

        exitCode.Should().Be(0);
        stdout.Trim().Should().Be("hello there");
    }

    [Fact]
    public async Task Replace_ShouldShrinkString_WhenReplacementIsShorterThanLength()
    {
        var publicKeyFile = Path.Combine(_testDirectory, "test.pub");
        var privateKeyFile = Path.Combine(_testDirectory, "test.key");
        var encFile = Path.Combine(_testDirectory, "msg.estr");
        var updatedFile = Path.Combine(_testDirectory, "updated.estr");

        await WriteKeyFilesAsync(publicKeyFile, privateKeyFile);
        await RunToolkitCommandAsync("crypto", "encrypt-string",
            "--string", "the quick brown fox", "--public-key-file", publicKeyFile, "--out-file", encFile);

        await RunToolkitCommandAsync("crypto", "replace",
            "--in-file", encFile, "--start", "4", "--replacement", "slow", "--length", "5", "--out-file", updatedFile);

        var (exitCode, stdout, _) = await RunToolkitCommandAsync(
            "crypto", "decrypt-string", "--in-file", updatedFile, "--private-key-file", privateKeyFile);

        exitCode.Should().Be(0);
        stdout.Trim().Should().Be("the slow brown fox");
    }

    [Fact]
    public async Task Replace_ShouldReturnFailure_WhenRangeExceedsStringLength()
    {
        var publicKeyFile = Path.Combine(_testDirectory, "test.pub");
        var privateKeyFile = Path.Combine(_testDirectory, "test.key");
        var encFile = Path.Combine(_testDirectory, "msg.estr");
        var updatedFile = Path.Combine(_testDirectory, "updated.estr");

        await WriteKeyFilesAsync(publicKeyFile, privateKeyFile);
        await RunToolkitCommandAsync("crypto", "encrypt-string",
            "--string", "hi", "--public-key-file", publicKeyFile, "--out-file", encFile);

        var (exitCode, stdout, _) = await RunToolkitCommandAsync(
            "crypto", "replace", "--in-file", encFile, "--start", "1", "--replacement", "xyz", "--out-file", updatedFile);

        exitCode.Should().Be(1);
        stdout.Should().Contain("Error:");
    }

    [Fact]
    public async Task EncryptString_ShouldReturnFailure_WhenStringExceedsMaxLength()
    {
        var publicKeyFile = Path.Combine(_testDirectory, "test.pub");
        var privateKeyFile = Path.Combine(_testDirectory, "test.key");
        var encFile = Path.Combine(_testDirectory, "msg.estr");

        await WriteKeyFilesAsync(publicKeyFile, privateKeyFile);
        var tooLong = new string('a', 101);

        var (exitCode, stdout, _) = await RunToolkitCommandAsync(
            "crypto", "encrypt-string", "--string", tooLong, "--public-key-file", publicKeyFile, "--out-file", encFile);

        exitCode.Should().Be(1);
        stdout.Should().Contain("Error:");
    }

    [Fact]
    public async Task Multiply_ShouldProduceEncryptedProductThatDecryptsToCorrectValue()
    {
        var publicKeyFile = Path.Combine(_testDirectory, "test.pub");
        var privateKeyFile = Path.Combine(_testDirectory, "test.key");
        var encFile = Path.Combine(_testDirectory, "a.enc");
        var productFile = Path.Combine(_testDirectory, "product.enc");

        await WriteKeyFilesAsync(publicKeyFile, privateKeyFile);
        await RunToolkitCommandAsync("crypto", "encrypt",
            "--number", "7", "--public-key-file", publicKeyFile, "--out-file", encFile);

        var (exitCode, stdout, _) = await RunToolkitCommandAsync(
            "crypto", "multiply", "--in-file", encFile, "--scalar", "6", "--out-file", productFile);

        exitCode.Should().Be(0);
        stdout.Should().Contain("Encrypted product written to");

        var (decryptExitCode, decryptStdout, _) = await RunToolkitCommandAsync(
            "crypto", "decrypt", "--in-file", productFile, "--private-key-file", privateKeyFile);
        decryptExitCode.Should().Be(0);
        decryptStdout.Trim().Should().Be("42");
    }

    [Fact]
    public async Task Multiply_ShouldProduceEncryptedZero_WhenScalarIsZero()
    {
        var publicKeyFile = Path.Combine(_testDirectory, "test.pub");
        var privateKeyFile = Path.Combine(_testDirectory, "test.key");
        var encFile = Path.Combine(_testDirectory, "a.enc");
        var productFile = Path.Combine(_testDirectory, "product.enc");

        await WriteKeyFilesAsync(publicKeyFile, privateKeyFile);
        await RunToolkitCommandAsync("crypto", "encrypt",
            "--number", "99", "--public-key-file", publicKeyFile, "--out-file", encFile);
        await RunToolkitCommandAsync("crypto", "multiply",
            "--in-file", encFile, "--scalar", "0", "--out-file", productFile);

        var (exitCode, stdout, _) = await RunToolkitCommandAsync(
            "crypto", "decrypt", "--in-file", productFile, "--private-key-file", privateKeyFile);

        exitCode.Should().Be(0);
        stdout.Trim().Should().Be("0");
    }

    [Fact]
    public async Task Multiply_ShouldReturnFailure_WhenScalarIsNegative()
    {
        var publicKeyFile = Path.Combine(_testDirectory, "test.pub");
        var privateKeyFile = Path.Combine(_testDirectory, "test.key");
        var encFile = Path.Combine(_testDirectory, "a.enc");
        var productFile = Path.Combine(_testDirectory, "product.enc");

        await WriteKeyFilesAsync(publicKeyFile, privateKeyFile);
        await RunToolkitCommandAsync("crypto", "encrypt",
            "--number", "5", "--public-key-file", publicKeyFile, "--out-file", encFile);

        var (exitCode, stdout, _) = await RunToolkitCommandAsync(
            "crypto", "multiply", "--in-file", encFile, "--scalar", "-3", "--out-file", productFile);

        exitCode.Should().Be(1);
        stdout.Should().Contain("Error:");
    }

    [Fact]
    public async Task Subtract_ShouldProduceEncryptedDifferenceThatDecryptsToCorrectValue()
    {
        var publicKeyFile = Path.Combine(_testDirectory, "test.pub");
        var privateKeyFile = Path.Combine(_testDirectory, "test.key");
        var encFile1 = Path.Combine(_testDirectory, "a.enc");
        var encFile2 = Path.Combine(_testDirectory, "b.enc");
        var diffFile = Path.Combine(_testDirectory, "diff.enc");

        await WriteKeyFilesAsync(publicKeyFile, privateKeyFile);
        await RunToolkitCommandAsync("crypto", "encrypt",
            "--number", "100", "--public-key-file", publicKeyFile, "--out-file", encFile1);
        await RunToolkitCommandAsync("crypto", "encrypt",
            "--number", "58", "--public-key-file", publicKeyFile, "--out-file", encFile2);
        await RunToolkitCommandAsync("crypto", "subtract",
            "--in-file1", encFile1, "--in-file2", encFile2, "--out-file", diffFile);

        var (exitCode, stdout, _) = await RunToolkitCommandAsync(
            "crypto", "decrypt", "--in-file", diffFile, "--private-key-file", privateKeyFile);

        exitCode.Should().Be(0);
        stdout.Trim().Should().Be("42");
    }

    [Fact]
    public async Task Subtract_ShouldProduceEncryptedZero_WhenValuesAreEqual()
    {
        var publicKeyFile = Path.Combine(_testDirectory, "test.pub");
        var privateKeyFile = Path.Combine(_testDirectory, "test.key");
        var encFile1 = Path.Combine(_testDirectory, "a.enc");
        var encFile2 = Path.Combine(_testDirectory, "b.enc");
        var diffFile = Path.Combine(_testDirectory, "diff.enc");

        await WriteKeyFilesAsync(publicKeyFile, privateKeyFile);
        await RunToolkitCommandAsync("crypto", "encrypt",
            "--number", "77", "--public-key-file", publicKeyFile, "--out-file", encFile1);
        await RunToolkitCommandAsync("crypto", "encrypt",
            "--number", "77", "--public-key-file", publicKeyFile, "--out-file", encFile2);
        await RunToolkitCommandAsync("crypto", "subtract",
            "--in-file1", encFile1, "--in-file2", encFile2, "--out-file", diffFile);

        var (exitCode, stdout, _) = await RunToolkitCommandAsync(
            "crypto", "decrypt", "--in-file", diffFile, "--private-key-file", privateKeyFile);

        exitCode.Should().Be(0);
        stdout.Trim().Should().Be("0");
    }

    [Fact]
    public async Task Subtract_ShouldReturnFailure_WhenFirstInputFileDoesNotExist()
    {
        var publicKeyFile = Path.Combine(_testDirectory, "test.pub");
        var privateKeyFile = Path.Combine(_testDirectory, "test.key");
        var missingFile = Path.Combine(_testDirectory, "missing.enc");
        var encFile = Path.Combine(_testDirectory, "b.enc");
        var diffFile = Path.Combine(_testDirectory, "diff.enc");

        await WriteKeyFilesAsync(publicKeyFile, privateKeyFile);
        await RunToolkitCommandAsync("crypto", "encrypt",
            "--number", "5", "--public-key-file", publicKeyFile, "--out-file", encFile);

        var (exitCode, stdout, _) = await RunToolkitCommandAsync(
            "crypto", "subtract", "--in-file1", missingFile, "--in-file2", encFile, "--out-file", diffFile);

        exitCode.Should().Be(1);
        stdout.Should().Contain("Error:");
    }

    [Fact]
    public async Task Substring_ShouldProduceSliceThatDecryptsToCorrectValue()
    {
        var publicKeyFile = Path.Combine(_testDirectory, "test.pub");
        var privateKeyFile = Path.Combine(_testDirectory, "test.key");
        var encFile = Path.Combine(_testDirectory, "msg.estr");
        var subFile = Path.Combine(_testDirectory, "sub.estr");

        await WriteKeyFilesAsync(publicKeyFile, privateKeyFile);
        await RunToolkitCommandAsync("crypto", "encrypt-string",
            "--string", "hello world", "--public-key-file", publicKeyFile, "--out-file", encFile);

        var (exitCode, stdout, _) = await RunToolkitCommandAsync(
            "crypto", "substring", "--in-file", encFile, "--start", "6", "--length", "5", "--out-file", subFile);

        exitCode.Should().Be(0);
        stdout.Should().Contain("Encrypted substring (5 characters) written to");

        var (decryptExitCode, decryptStdout, _) = await RunToolkitCommandAsync(
            "crypto", "decrypt-string", "--in-file", subFile, "--private-key-file", privateKeyFile);
        decryptExitCode.Should().Be(0);
        decryptStdout.Trim().Should().Be("world");
    }

    [Fact]
    public async Task Substring_ShouldDecryptToRemainder_WhenLengthIsOmitted()
    {
        var publicKeyFile = Path.Combine(_testDirectory, "test.pub");
        var privateKeyFile = Path.Combine(_testDirectory, "test.key");
        var encFile = Path.Combine(_testDirectory, "msg.estr");
        var subFile = Path.Combine(_testDirectory, "sub.estr");

        await WriteKeyFilesAsync(publicKeyFile, privateKeyFile);
        await RunToolkitCommandAsync("crypto", "encrypt-string",
            "--string", "hello world", "--public-key-file", publicKeyFile, "--out-file", encFile);
        await RunToolkitCommandAsync("crypto", "substring",
            "--in-file", encFile, "--start", "6", "--out-file", subFile);

        var (exitCode, stdout, _) = await RunToolkitCommandAsync(
            "crypto", "decrypt-string", "--in-file", subFile, "--private-key-file", privateKeyFile);

        exitCode.Should().Be(0);
        stdout.Trim().Should().Be("world");
    }

    [Fact]
    public async Task Substring_ShouldReturnFailure_WhenRangeExceedsStringLength()
    {
        var publicKeyFile = Path.Combine(_testDirectory, "test.pub");
        var privateKeyFile = Path.Combine(_testDirectory, "test.key");
        var encFile = Path.Combine(_testDirectory, "msg.estr");
        var subFile = Path.Combine(_testDirectory, "sub.estr");

        await WriteKeyFilesAsync(publicKeyFile, privateKeyFile);
        await RunToolkitCommandAsync("crypto", "encrypt-string",
            "--string", "hi", "--public-key-file", publicKeyFile, "--out-file", encFile);

        var (exitCode, stdout, _) = await RunToolkitCommandAsync(
            "crypto", "substring", "--in-file", encFile, "--start", "1", "--length", "5", "--out-file", subFile);

        exitCode.Should().Be(1);
        stdout.Should().Contain("Error:");
    }

    [Fact]
    public async Task Substring_ShouldReturnFailure_WhenInputFileDoesNotExist()
    {
        var missingFile = Path.Combine(_testDirectory, "missing.estr");
        var subFile = Path.Combine(_testDirectory, "sub.estr");

        var (exitCode, stdout, _) = await RunToolkitCommandAsync(
            "crypto", "substring", "--in-file", missingFile, "--start", "0", "--out-file", subFile);

        exitCode.Should().Be(1);
        stdout.Should().Contain("Error:");
    }

    [Fact]
    public async Task Add_ShouldReturnFailure_WhenFirstInputFileDoesNotExist()
    {
        var publicKeyFile = Path.Combine(_testDirectory, "test.pub");
        var privateKeyFile = Path.Combine(_testDirectory, "test.key");
        var missingFile = Path.Combine(_testDirectory, "missing.enc");
        var encFile = Path.Combine(_testDirectory, "number.enc");
        var sumFile = Path.Combine(_testDirectory, "sum.enc");

        await RunToolkitCommandAsync("crypto", "generate-key",
            "--public-key-file", publicKeyFile, "--private-key-file", privateKeyFile);
        await RunToolkitCommandAsync("crypto", "encrypt",
            "--number", "5", "--public-key-file", publicKeyFile, "--out-file", encFile);

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

    private static async Task WriteKeyFilesAsync(string publicKeyFile, string privateKeyFile)
    {
        var key = TestKey.Value;
        var publicKey = new PaillierPublicKey { N = key.N };

        await File.WriteAllTextAsync(publicKeyFile, JsonSerializer.Serialize(publicKey, JsonOptions));
        await File.WriteAllTextAsync(privateKeyFile, JsonSerializer.Serialize(key, JsonOptions));
    }
}
