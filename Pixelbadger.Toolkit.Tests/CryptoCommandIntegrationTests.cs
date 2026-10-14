using System.Diagnostics;
using System.Numerics;
using System.Text;
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
        stdout.Trim().Should().Be(GenericError);
    }

    [Fact]
    public async Task EncryptString_ThenDecryptString_ShouldReturnOriginalString()
    {
        var publicKeyFile = Path.Combine(_testDirectory, "test.pub");
        var privateKeyFile = Path.Combine(_testDirectory, "test.key");
        var encFile = Path.Combine(_testDirectory, "msg.estr");

        await WriteKeyFilesAsync(publicKeyFile, privateKeyFile);
        await RunToolkitCommandAsync("crypto", "encrypt-string",
            "--string-file", await WriteTextFileAsync("in.txt", "hello world"), "--public-key-file", publicKeyFile, "--out-file", encFile);

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
            "--string-file", await WriteTextFileAsync("in.txt", "hello world"), "--public-key-file", publicKeyFile, "--out-file", encFile);
        await RunToolkitCommandAsync("crypto", "replace",
            "--in-file", encFile, "--start", "6", "--replacement-file", await WriteTextFileAsync("rep.txt", "there"), "--out-file", updatedFile);

        var (exitCode, stdout, _) = await RunToolkitCommandAsync(
            "crypto", "decrypt-string", "--in-file", updatedFile, "--private-key-file", privateKeyFile);

        exitCode.Should().Be(0);
        stdout.Trim().Should().Be("hello there");
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
            "--string-file", await WriteTextFileAsync("in.txt", "hi"), "--public-key-file", publicKeyFile, "--out-file", encFile);

        var (exitCode, stdout, _) = await RunToolkitCommandAsync(
            "crypto", "replace", "--in-file", encFile, "--start", "99", "--replacement-file", await WriteTextFileAsync("rep.txt", "xy"), "--out-file", updatedFile);

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
            "crypto", "encrypt-string", "--string-file", await WriteTextFileAsync("in.txt", tooLong), "--public-key-file", publicKeyFile, "--out-file", encFile);

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
            "--string-file", await WriteTextFileAsync("in.txt", "hello world"), "--public-key-file", publicKeyFile, "--out-file", encFile);

        var (exitCode, stdout, _) = await RunToolkitCommandAsync(
            "crypto", "substring", "--in-file", encFile, "--start", "6", "--length", "5", "--out-file", subFile);

        exitCode.Should().Be(0);
        stdout.Should().Contain("Encrypted substring written to");

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
            "--string-file", await WriteTextFileAsync("in.txt", "hello world"), "--public-key-file", publicKeyFile, "--out-file", encFile);
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
            "--string-file", await WriteTextFileAsync("in.txt", "hi"), "--public-key-file", publicKeyFile, "--out-file", encFile);

        var (exitCode, stdout, _) = await RunToolkitCommandAsync(
            "crypto", "substring", "--in-file", encFile, "--start", "98", "--length", "5", "--out-file", subFile);

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

    private const string GenericError = "Error: Decryption failed: input is invalid or was not encrypted with this key.";

    private async Task<(string Pub, string Priv, string Enc)> EncryptFileAsync(string content)
    {
        var pub = Path.Combine(_testDirectory, "test.pub");
        var priv = Path.Combine(_testDirectory, "test.key");
        var enc = Path.Combine(_testDirectory, "msg.estr");
        await WriteKeyFilesAsync(pub, priv);
        var (exit, _, _) = await RunToolkitCommandAsync("crypto", "encrypt-string",
            "--string-file", await WriteTextFileAsync("in.txt", content), "--public-key-file", pub, "--out-file", enc);
        exit.Should().Be(0);
        return (pub, priv, enc);
    }

    private async Task<string> DecryptStringAsync(string enc, string priv)
    {
        var (exit, stdout, _) = await RunToolkitCommandAsync("crypto", "decrypt-string", "--in-file", enc, "--private-key-file", priv);
        exit.Should().Be(0);
        // The command ends its output with exactly one newline; remove only that one.
        return stdout.EndsWith(Environment.NewLine) ? stdout[..^Environment.NewLine.Length] : stdout;
    }

    [Theory]
    [InlineData("hello\n", "hello")]
    [InlineData("hello\r\n", "hello")]
    [InlineData("hello\n\n", "hello\n")]
    [InlineData("hello", "hello")]
    [InlineData(" hello ", " hello ")]
    public async Task EncryptString_ShouldStripExactlyOneTrailingLineEnding_WhenReadingFromFile(string content, string expected)
    {
        var (_, priv, enc) = await EncryptFileAsync(content);

        (await DecryptStringAsync(enc, priv)).Should().Be(expected);
    }

    [Fact]
    public async Task EncryptString_ShouldWriteExactly100Characters_WhenEncrypting()
    {
        var (_, _, enc) = await EncryptFileAsync("hello");

        var parsed = JsonSerializer.Deserialize<EncryptedString>(await File.ReadAllTextAsync(enc));
        parsed!.Characters.Should().HaveCount(100);
    }

    [Fact]
    public async Task EncryptString_ShouldRoundTrip_WhenReadingFromStdin()
    {
        var pub = Path.Combine(_testDirectory, "test.pub");
        var priv = Path.Combine(_testDirectory, "test.key");
        var enc = Path.Combine(_testDirectory, "msg.estr");
        await WriteKeyFilesAsync(pub, priv);

        var (exitCode, stdout, _) = await ToolkitProcess.RunWithStdinAsync(null, "h\u00e9llo w\u00f6rld\n",
            "crypto", "encrypt-string", "--string-file", "-", "--public-key-file", pub, "--out-file", enc);

        exitCode.Should().Be(0);
        stdout.Should().Contain("Encrypted string written to");
        stdout.Should().NotContain("characters");
        (await DecryptStringAsync(enc, priv)).Should().Be("h\u00e9llo w\u00f6rld");
    }

    [Fact]
    public async Task Replace_ShouldReadReplacementFromStdin_WhenReplacementFileIsDash()
    {
        var (_, priv, enc) = await EncryptFileAsync("hello world");
        var updated = Path.Combine(_testDirectory, "updated.estr");

        var (exitCode, _, _) = await ToolkitProcess.RunWithStdinAsync(null, "there\r\n",
            "crypto", "replace", "--in-file", enc, "--start", "6", "--replacement-file", "-", "--out-file", updated);

        exitCode.Should().Be(0);
        (await DecryptStringAsync(updated, priv)).Should().Be("hello there");
    }

    [Fact]
    public async Task Replace_ShouldExtendString_WhenReplacingIntoPadding()
    {
        var (_, priv, enc) = await EncryptFileAsync("hello");
        var updated = Path.Combine(_testDirectory, "updated.estr");

        var (exitCode, _, _) = await RunToolkitCommandAsync("crypto", "replace",
            "--in-file", enc, "--start", "7", "--replacement-file", await WriteTextFileAsync("rep.txt", "X"), "--out-file", updated);

        exitCode.Should().Be(0);
        (await DecryptStringAsync(updated, priv)).Should().Be("helloX");
    }

    [Fact]
    public async Task EncryptString_ShouldRejectRemovedStringOption_WhenStringFlagUsed()
    {
        var pub = Path.Combine(_testDirectory, "test.pub");
        var priv = Path.Combine(_testDirectory, "test.key");
        var enc = Path.Combine(_testDirectory, "msg.estr");
        await WriteKeyFilesAsync(pub, priv);

        var (exitCode, stdout, stderr) = await RunToolkitCommandAsync("crypto", "encrypt-string",
            "--string-file", await WriteTextFileAsync("in.txt", "hello"), "--string", "hello",
            "--public-key-file", pub, "--out-file", enc);

        exitCode.Should().NotBe(0);
        (stdout + stderr).Should().Contain("Unrecognized command or argument '--string'");
        File.Exists(enc).Should().BeFalse();
    }

    [Fact]
    public async Task Replace_ShouldRejectRemovedReplacementOption_WhenReplacementFlagUsed()
    {
        var (_, _, enc) = await EncryptFileAsync("hello");
        var updated = Path.Combine(_testDirectory, "updated.estr");

        var (exitCode, stdout, stderr) = await RunToolkitCommandAsync("crypto", "replace",
            "--in-file", enc, "--start", "0",
            "--replacement-file", await WriteTextFileAsync("rep.txt", "X"), "--replacement", "X",
            "--out-file", updated);

        exitCode.Should().NotBe(0);
        (stdout + stderr).Should().Contain("Unrecognized command or argument '--replacement'");
        File.Exists(updated).Should().BeFalse();
    }

    [Fact]
    public async Task Multiply_ShouldFailPromptly_WhenModulusIsNegative()
    {
        var negN = (-(BigInteger.One << 2048) - 1).ToString();
        var bad = Path.Combine(_testDirectory, "bad.enc");
        await File.WriteAllTextAsync(bad, JsonSerializer.Serialize(new EncryptedNumber { Ciphertext = "5", N = negN }));
        var outFile = Path.Combine(_testDirectory, "out.enc");

        var (exitCode, stdout, _) = await RunToolkitCommandAsync(
            "crypto", "multiply", "--in-file", bad, "--scalar", "3", "--out-file", outFile);

        exitCode.Should().Be(1);
        stdout.Should().Contain("Error:");
        File.Exists(outFile).Should().BeFalse();
    }

    [Fact]
    public async Task DecryptString_ShouldPrintExactHundredCharacters_WithoutWrapping()
    {
        var text = string.Concat(Enumerable.Repeat("abcdefghij", 10));
        var (_, priv, enc) = await EncryptFileAsync(text);

        var (exitCode, stdout, _) = await RunToolkitCommandAsync(
            "crypto", "decrypt-string", "--in-file", enc, "--private-key-file", priv);

        exitCode.Should().Be(0);
        stdout.Should().Be(text + Environment.NewLine);
    }

    [Fact]
    public async Task Decrypt_ShouldPrintExactNumber_WhenPlaintextIsLongMaxValue()
    {
        var pub = Path.Combine(_testDirectory, "test.pub");
        var priv = Path.Combine(_testDirectory, "test.key");
        var encFile = Path.Combine(_testDirectory, "number.enc");
        await WriteKeyFilesAsync(pub, priv);
        await RunToolkitCommandAsync("crypto", "encrypt", "--number", long.MaxValue.ToString(),
            "--public-key-file", pub, "--out-file", encFile);

        var (exitCode, stdout, _) = await RunToolkitCommandAsync(
            "crypto", "decrypt", "--in-file", encFile, "--private-key-file", priv);

        exitCode.Should().Be(0);
        stdout.Should().Be(long.MaxValue + Environment.NewLine);
    }

    [Fact]
    public async Task EncryptString_ShouldReturnFailure_WhenStringFileContainsNul()
    {
        var pub = Path.Combine(_testDirectory, "test.pub");
        var priv = Path.Combine(_testDirectory, "test.key");
        var enc = Path.Combine(_testDirectory, "msg.estr");
        await WriteKeyFilesAsync(pub, priv);

        var (exitCode, stdout, _) = await RunToolkitCommandAsync("crypto", "encrypt-string",
            "--string-file", await WriteTextFileAsync("nul.txt", "ab\0cd"), "--public-key-file", pub, "--out-file", enc);

        exitCode.Should().Be(1);
        stdout.Should().Contain("Error:");
        File.Exists(enc).Should().BeFalse();
    }

    [Fact]
    public async Task DecryptString_ShouldPrintGenericError_WhenWrongPrivateKeyUsed()
    {
        var (_, _, enc) = await EncryptFileAsync("hello");
        var otherPriv = Path.Combine(_testDirectory, "other.key");
        var otherKey = new HomomorphicEncryptionComponent().GenerateKey();
        await File.WriteAllTextAsync(otherPriv, JsonSerializer.Serialize(otherKey, JsonOptions));

        var (exitCode, stdout, _) = await RunToolkitCommandAsync(
            "crypto", "decrypt-string", "--in-file", enc, "--private-key-file", otherPriv);

        exitCode.Should().Be(1);
        stdout.Trim().Should().Be(GenericError);
    }

    [Fact]
    public async Task Decrypt_ShouldPrintGenericError_WhenWrongPrivateKeyUsed()
    {
        var pub = Path.Combine(_testDirectory, "test.pub");
        var priv = Path.Combine(_testDirectory, "test.key");
        var encFile = Path.Combine(_testDirectory, "number.enc");
        await WriteKeyFilesAsync(pub, priv);
        await RunToolkitCommandAsync("crypto", "encrypt", "--number", "42", "--public-key-file", pub, "--out-file", encFile);
        var otherPriv = Path.Combine(_testDirectory, "other.key");
        await File.WriteAllTextAsync(otherPriv, JsonSerializer.Serialize(new HomomorphicEncryptionComponent().GenerateKey(), JsonOptions));

        var (exitCode, stdout, _) = await RunToolkitCommandAsync(
            "crypto", "decrypt", "--in-file", encFile, "--private-key-file", otherPriv);

        exitCode.Should().Be(1);
        stdout.Trim().Should().Be(GenericError);
    }

    [Fact]
    public async Task DecryptString_ShouldPrintGenericError_WhenInputFileIsMalformedJson()
    {
        var pub = Path.Combine(_testDirectory, "test.pub");
        var priv = Path.Combine(_testDirectory, "test.key");
        await WriteKeyFilesAsync(pub, priv);
        var bad = await WriteTextFileAsync("bad.estr", "{ not valid json }");

        var (exitCode, stdout, _) = await RunToolkitCommandAsync(
            "crypto", "decrypt-string", "--in-file", bad, "--private-key-file", priv);

        exitCode.Should().Be(1);
        stdout.Trim().Should().Be(GenericError);
    }

    [Fact]
    public async Task DecryptString_ShouldPrintSpecificError_WhenInputFileIsMissing()
    {
        var pub = Path.Combine(_testDirectory, "test.pub");
        var priv = Path.Combine(_testDirectory, "test.key");
        await WriteKeyFilesAsync(pub, priv);

        var (exitCode, stdout, _) = await RunToolkitCommandAsync(
            "crypto", "decrypt-string", "--in-file", Path.Combine(_testDirectory, "missing.estr"), "--private-key-file", priv);

        exitCode.Should().Be(1);
        stdout.Should().Contain("Error:");
        stdout.Should().Contain("missing.estr");
        stdout.Should().NotContain("Decryption failed");
    }

    private async Task<string> WriteTextFileAsync(string name, string content)
    {
        var path = Path.Combine(_testDirectory, name);
        await File.WriteAllBytesAsync(path, new UTF8Encoding(false).GetBytes(content));
        return path;
    }

    private static Task<(int ExitCode, string StandardOutput, string StandardError)> RunToolkitCommandAsync(params string[] args) =>
        ToolkitProcess.RunAsync(null, args);

    private static async Task WriteKeyFilesAsync(string publicKeyFile, string privateKeyFile)
    {
        var key = TestKey.Value;
        var publicKey = new PaillierPublicKey { N = key.N };

        await File.WriteAllTextAsync(publicKeyFile, JsonSerializer.Serialize(publicKey, JsonOptions));
        await File.WriteAllTextAsync(privateKeyFile, JsonSerializer.Serialize(key, JsonOptions));
    }
}
