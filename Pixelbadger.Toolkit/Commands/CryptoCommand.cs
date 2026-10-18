using System.CommandLine;
using System.Text;
using System.Text.Json;
using Pixelbadger.Toolkit.CommandLine;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Models;
using Spectre.Console;

namespace Pixelbadger.Toolkit.Commands;

public static class CryptoCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static Command Create()
    {
        var command = new Command("crypto", "Homomorphic encryption utilities using the Paillier cryptosystem");

        command.Add(CreateGenerateKeyCommand());
        command.Add(CreateEncryptCommand());
        command.Add(CreateDecryptCommand());
        command.Add(CreateAddCommand());
        command.Add(CreateSubtractCommand());
        command.Add(CreateMultiplyCommand());
        command.Add(CreateEncryptStringCommand());
        command.Add(CreateDecryptStringCommand());
        command.Add(CreateReplaceCommand());
        command.Add(CreateSubstringCommand());

        return command;
    }

    private static Command CreateGenerateKeyCommand()
    {
        var command = new Command("generate-key", "Generate a Paillier key pair and save public and private key files");

        var publicKeyFileOption = new Option<string>("--public-key-file") { Description = "Path to write the public key JSON file (contains N; safe to share)", Required = true };
        var privateKeyFileOption = new Option<string>("--private-key-file") { Description = "Path to write the private key JSON file (contains N, Lambda, Mu; keep secret)", Required = true };

        command.Add(publicKeyFileOption);
        command.Add(privateKeyFileOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            try
            {
                var publicKeyFile = parseResult.GetValue(publicKeyFileOption)!;
                var privateKeyFile = parseResult.GetValue(privateKeyFileOption)!;

                var component = new HomomorphicEncryptionComponent();
                var keyPair = component.GenerateKey();
                var publicKey = new PaillierPublicKey { N = keyPair.N };

                await File.WriteAllTextAsync(publicKeyFile, JsonSerializer.Serialize(publicKey, JsonOptions));
                await WriteOwnerOnlyTextAsync(privateKeyFile, JsonSerializer.Serialize(keyPair, JsonOptions));

                AnsiConsole.MarkupLine($"[green]Public key written to '{Markup.Escape(publicKeyFile)}'[/]");
                AnsiConsole.MarkupLine($"[green]Private key written to '{Markup.Escape(privateKeyFile)}'[/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
                return 1;
            }
            return 0;
        });

        return command;
    }

    private static Command CreateEncryptCommand()
    {
        var command = new Command("encrypt", "Encrypt a number using a Paillier public key");

        var numberOption = new Option<long>("--number") { Description = "The non-negative integer to encrypt", Required = true };
        var publicKeyFileOption = new Option<string>("--public-key-file") { Description = "Path to the public key JSON file", Required = true };
        var outFileOption = new Option<string>("--out-file") { Description = "Path to write the encrypted number JSON file", Required = true };

        command.Add(numberOption);
        command.Add(publicKeyFileOption);
        command.Add(outFileOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            try
            {
                var number = parseResult.GetValue(numberOption);
                var publicKeyFile = parseResult.GetValue(publicKeyFileOption)!;
                var outFile = parseResult.GetValue(outFileOption)!;

                var keyJson = await File.ReadAllTextAsync(publicKeyFile);
                var publicKey = JsonSerializer.Deserialize<PaillierPublicKey>(keyJson)
                    ?? throw new InvalidOperationException("Failed to deserialize public key.");

                var component = new HomomorphicEncryptionComponent();
                var encrypted = component.Encrypt(number, publicKey);

                var json = JsonSerializer.Serialize(encrypted, JsonOptions);
                await File.WriteAllTextAsync(outFile, json);
                AnsiConsole.MarkupLine($"[green]Encrypted number written to '{Markup.Escape(outFile)}'[/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
                return 1;
            }
            return 0;
        });

        return command;
    }

    private static Command CreateDecryptCommand()
    {
        var command = new Command("decrypt", "Decrypt an encrypted number using a Paillier private key");

        var inFileOption = new Option<string>("--in-file") { Description = "Path to the encrypted number JSON file", Required = true };
        var privateKeyFileOption = new Option<string>("--private-key-file") { Description = "Path to the private key JSON file", Required = true };

        command.Add(inFileOption);
        command.Add(privateKeyFileOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            try
            {
                var inFile = parseResult.GetValue(inFileOption)!;
                var privateKeyFile = parseResult.GetValue(privateKeyFileOption)!;

                var encryptedJson = await File.ReadAllTextAsync(inFile);
                var keyJson = await File.ReadAllTextAsync(privateKeyFile);

                var encrypted = JsonSerializer.Deserialize<EncryptedNumber>(encryptedJson)
                    ?? throw new InvalidOperationException("Failed to deserialize encrypted number.");
                var keyPair = JsonSerializer.Deserialize<PaillierKeyPair>(keyJson)
                    ?? throw new InvalidOperationException("Failed to deserialize private key.");

                var component = new HomomorphicEncryptionComponent();
                var plaintext = component.Decrypt(encrypted, keyPair);
                ToolkitConsole.Out.WriteLine(plaintext.ToString());
            }
            catch (Exception ex)
            {
                return ReportDecryptionError(ex);
            }
            return 0;
        });

        return command;
    }

    private static Command CreateAddCommand()
    {
        var command = new Command("add", "Homomorphically add two encrypted numbers without decrypting them");

        var inFile1Option = new Option<string>("--in-file1") { Description = "Path to the first encrypted number JSON file", Required = true };
        var inFile2Option = new Option<string>("--in-file2") { Description = "Path to the second encrypted number JSON file", Required = true };
        var outFileOption = new Option<string>("--out-file") { Description = "Path to write the encrypted sum JSON file", Required = true };

        command.Add(inFile1Option);
        command.Add(inFile2Option);
        command.Add(outFileOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            try
            {
                var inFile1 = parseResult.GetValue(inFile1Option)!;
                var inFile2 = parseResult.GetValue(inFile2Option)!;
                var outFile = parseResult.GetValue(outFileOption)!;

                var enc1Json = await File.ReadAllTextAsync(inFile1);
                var encrypted1 = JsonSerializer.Deserialize<EncryptedNumber>(enc1Json)
                    ?? throw new InvalidOperationException("Failed to deserialize first encrypted number.");

                var enc2Json = await File.ReadAllTextAsync(inFile2);
                var encrypted2 = JsonSerializer.Deserialize<EncryptedNumber>(enc2Json)
                    ?? throw new InvalidOperationException("Failed to deserialize second encrypted number.");

                var component = new HomomorphicEncryptionComponent();
                var sum = component.AddEncrypted(encrypted1, encrypted2);

                var json = JsonSerializer.Serialize(sum, JsonOptions);
                await File.WriteAllTextAsync(outFile, json);
                AnsiConsole.MarkupLine($"[green]Encrypted sum written to '{Markup.Escape(outFile)}'[/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
                return 1;
            }
            return 0;
        });

        return command;
    }

    private static Command CreateSubtractCommand()
    {
        var command = new Command("subtract", "Homomorphically subtract two encrypted numbers without decrypting them");

        var inFile1Option = new Option<string>("--in-file1") { Description = "Path to the first encrypted number JSON file (minuend)", Required = true };
        var inFile2Option = new Option<string>("--in-file2") { Description = "Path to the second encrypted number JSON file (subtrahend)", Required = true };
        var outFileOption = new Option<string>("--out-file") { Description = "Path to write the encrypted difference JSON file", Required = true };

        command.Add(inFile1Option);
        command.Add(inFile2Option);
        command.Add(outFileOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            try
            {
                var inFile1 = parseResult.GetValue(inFile1Option)!;
                var inFile2 = parseResult.GetValue(inFile2Option)!;
                var outFile = parseResult.GetValue(outFileOption)!;

                var enc1Json = await File.ReadAllTextAsync(inFile1);
                var encrypted1 = JsonSerializer.Deserialize<EncryptedNumber>(enc1Json)
                    ?? throw new InvalidOperationException("Failed to deserialize first encrypted number.");

                var enc2Json = await File.ReadAllTextAsync(inFile2);
                var encrypted2 = JsonSerializer.Deserialize<EncryptedNumber>(enc2Json)
                    ?? throw new InvalidOperationException("Failed to deserialize second encrypted number.");

                var component = new HomomorphicEncryptionComponent();
                var difference = component.SubtractEncrypted(encrypted1, encrypted2);

                var json = JsonSerializer.Serialize(difference, JsonOptions);
                await File.WriteAllTextAsync(outFile, json);
                AnsiConsole.MarkupLine($"[green]Encrypted difference written to '{Markup.Escape(outFile)}'[/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
                return 1;
            }
            return 0;
        });

        return command;
    }

    private static Command CreateMultiplyCommand()
    {
        var command = new Command("multiply", "Homomorphically multiply an encrypted number by a plaintext scalar without decrypting it");

        var inFileOption = new Option<string>("--in-file") { Description = "Path to the encrypted number JSON file", Required = true };
        var scalarOption = new Option<long>("--scalar") { Description = "The non-negative plaintext integer to multiply by", Required = true };
        var outFileOption = new Option<string>("--out-file") { Description = "Path to write the encrypted product JSON file", Required = true };

        command.Add(inFileOption);
        command.Add(scalarOption);
        command.Add(outFileOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            try
            {
                var inFile = parseResult.GetValue(inFileOption)!;
                var scalar = parseResult.GetValue(scalarOption);
                var outFile = parseResult.GetValue(outFileOption)!;

                var encJson = await File.ReadAllTextAsync(inFile);
                var encrypted = JsonSerializer.Deserialize<EncryptedNumber>(encJson)
                    ?? throw new InvalidOperationException("Failed to deserialize encrypted number.");

                var component = new HomomorphicEncryptionComponent();
                var product = component.MultiplyEncrypted(encrypted, scalar);

                var json = JsonSerializer.Serialize(product, JsonOptions);
                await File.WriteAllTextAsync(outFile, json);
                AnsiConsole.MarkupLine($"[green]Encrypted product written to '{Markup.Escape(outFile)}'[/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
                return 1;
            }
            return 0;
        });

        return command;
    }

    private static Command CreateEncryptStringCommand()
    {
        var command = new Command("encrypt-string", "Encrypt a UTF-8 string (max 100 characters; NUL not allowed) read from a file or stdin. The result is always padded to 100 encrypted characters");

        var stringOption = new Option<string>("--string-file") { Description = "Path to a UTF-8 text file containing the plaintext to encrypt (max 100 characters, no NUL), or '-' to read stdin. One trailing line ending is stripped", Required = true };
        var publicKeyFileOption = new Option<string>("--public-key-file") { Description = "Path to the public key JSON file", Required = true };
        var outFileOption = new Option<string>("--out-file") { Description = "Path to write the encrypted string JSON file", Required = true };

        command.Add(stringOption);
        command.Add(publicKeyFileOption);
        command.Add(outFileOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            try
            {
                var str = await ReadTextInputAsync(parseResult.GetValue(stringOption)!, cancellationToken);
                var publicKeyFile = parseResult.GetValue(publicKeyFileOption)!;
                var outFile = parseResult.GetValue(outFileOption)!;

                var keyJson = await File.ReadAllTextAsync(publicKeyFile);
                var publicKey = JsonSerializer.Deserialize<PaillierPublicKey>(keyJson)
                    ?? throw new InvalidOperationException("Failed to deserialize public key.");

                var component = new HomomorphicEncryptionComponent();
                var encrypted = component.EncryptString(str, publicKey);

                await File.WriteAllTextAsync(outFile, JsonSerializer.Serialize(encrypted, JsonOptions));
                AnsiConsole.MarkupLine($"[green]Encrypted string written to '{Markup.Escape(outFile)}'[/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
                return 1;
            }
            return 0;
        });

        return command;
    }

    private static Command CreateDecryptStringCommand()
    {
        var command = new Command("decrypt-string", "Decrypt a homomorphically encrypted string and print the plaintext");

        var inFileOption = new Option<string>("--in-file") { Description = "Path to the encrypted string JSON file", Required = true };
        var privateKeyFileOption = new Option<string>("--private-key-file") { Description = "Path to the private key JSON file", Required = true };

        command.Add(inFileOption);
        command.Add(privateKeyFileOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            try
            {
                var inFile = parseResult.GetValue(inFileOption)!;
                var privateKeyFile = parseResult.GetValue(privateKeyFileOption)!;

                var encJson = await File.ReadAllTextAsync(inFile);
                var keyJson = await File.ReadAllTextAsync(privateKeyFile);

                var encrypted = JsonSerializer.Deserialize<EncryptedString>(encJson)
                    ?? throw new InvalidOperationException("Failed to deserialize encrypted string.");
                var keyPair = JsonSerializer.Deserialize<PaillierKeyPair>(keyJson)
                    ?? throw new InvalidOperationException("Failed to deserialize private key.");

                var component = new HomomorphicEncryptionComponent();
                ToolkitConsole.Out.WriteLine(component.DecryptString(encrypted, keyPair));
            }
            catch (Exception ex)
            {
                return ReportDecryptionError(ex);
            }
            return 0;
        });

        return command;
    }

    private static Command CreateReplaceCommand()
    {
        var command = new Command("replace", "Replace characters at a known position in an encrypted string (padded to 100 characters) without decrypting it; replacement is read from a file or stdin (NUL not allowed)");

        var inFileOption = new Option<string>("--in-file") { Description = "Path to the encrypted string JSON file", Required = true };
        var startOption = new Option<int>("--start") { Description = "Zero-based index of the first character to replace", Required = true };
        var replacementOption = new Option<string>("--replacement-file") { Description = "Path to a UTF-8 text file containing the plaintext replacement (no NUL), or '-' to read stdin. One trailing line ending is stripped", Required = true };
        var outFileOption = new Option<string>("--out-file") { Description = "Path to write the updated encrypted string JSON file", Required = true };

        command.Add(inFileOption);
        command.Add(startOption);
        command.Add(replacementOption);
        command.Add(outFileOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            try
            {
                var inFile = parseResult.GetValue(inFileOption)!;
                var start = parseResult.GetValue(startOption);
                var replacement = await ReadTextInputAsync(parseResult.GetValue(replacementOption)!, cancellationToken);
                var outFile = parseResult.GetValue(outFileOption)!;

                var encJson = await File.ReadAllTextAsync(inFile);
                var encrypted = JsonSerializer.Deserialize<EncryptedString>(encJson)
                    ?? throw new InvalidOperationException("Failed to deserialize encrypted string.");

                var component = new HomomorphicEncryptionComponent();
                var updated = component.ReplaceInString(encrypted, start, replacement);

                await File.WriteAllTextAsync(outFile, JsonSerializer.Serialize(updated, JsonOptions));
                AnsiConsole.MarkupLine($"[green]Updated encrypted string written to '{Markup.Escape(outFile)}'[/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
                return 1;
            }
            return 0;
        });

        return command;
    }

    private static Command CreateSubstringCommand()
    {
        var command = new Command("substring", "Extract a slice of an encrypted string (padded to 100 characters) by position without decrypting it");

        var inFileOption = new Option<string>("--in-file") { Description = "Path to the encrypted string JSON file", Required = true };
        var startOption = new Option<int>("--start") { Description = "Zero-based index of the first character to include", Required = true };
        var lengthOption = new Option<int?>("--length") { Description = "Number of characters to include (defaults to remainder of string)" };
        var outFileOption = new Option<string>("--out-file") { Description = "Path to write the encrypted substring JSON file", Required = true };

        command.Add(inFileOption);
        command.Add(startOption);
        command.Add(lengthOption);
        command.Add(outFileOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            try
            {
                var inFile = parseResult.GetValue(inFileOption)!;
                var start = parseResult.GetValue(startOption);
                var length = parseResult.GetValue(lengthOption);
                var outFile = parseResult.GetValue(outFileOption)!;

                var encJson = await File.ReadAllTextAsync(inFile);
                var encrypted = JsonSerializer.Deserialize<EncryptedString>(encJson)
                    ?? throw new InvalidOperationException("Failed to deserialize encrypted string.");

                var component = new HomomorphicEncryptionComponent();
                var substring = component.SubstringEncrypted(encrypted, start, length);

                await File.WriteAllTextAsync(outFile, JsonSerializer.Serialize(substring, JsonOptions));
                AnsiConsole.MarkupLine($"[green]Encrypted substring written to '{Markup.Escape(outFile)}'[/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
                return 1;
            }
            return 0;
        });

        return command;
    }

    private const string GenericDecryptionError = "Decryption failed: input is invalid or was not encrypted with this key.";

    // File access problems keep their specific message; every other failure (bad JSON, wrong key, ...)
    // is reported with one fixed message so nothing about the failure mode leaks.
    private static int ReportDecryptionError(Exception ex)
    {
        var message = ex is IOException or UnauthorizedAccessException ? ex.Message : GenericDecryptionError;
        AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(message)}");
        return 1;
    }

    private static async Task<string> ReadTextInputAsync(string path, CancellationToken cancellationToken)
    {
        string text;
        if (path == "-")
        {
            using var reader = new StreamReader(ToolkitConsole.OpenStandardInput(), new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
            text = await reader.ReadToEndAsync(cancellationToken);
        }
        else
        {
            text = await File.ReadAllTextAsync(path, new UTF8Encoding(false), cancellationToken);
        }

        if (text.EndsWith("\r\n", StringComparison.Ordinal))
            return text[..^2];
        if (text.EndsWith('\n'))
            return text[..^1];
        return text;
    }

    private static async Task WriteOwnerOnlyTextAsync(string path, string contents)
    {
        if (!OperatingSystem.IsWindows() && File.Exists(path))
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        await File.WriteAllTextAsync(path, contents);

        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}
