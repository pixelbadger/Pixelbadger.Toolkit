using System.CommandLine;
using System.Text.Json;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Models;

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

                Console.WriteLine($"Public key written to '{publicKeyFile}'");
                Console.WriteLine($"Private key written to '{privateKeyFile}'");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
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
                Console.WriteLine($"Encrypted number written to '{outFile}'");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
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
                var encrypted = JsonSerializer.Deserialize<EncryptedNumber>(encryptedJson)
                    ?? throw new InvalidOperationException("Failed to deserialize encrypted number.");

                var keyJson = await File.ReadAllTextAsync(privateKeyFile);
                var keyPair = JsonSerializer.Deserialize<PaillierKeyPair>(keyJson)
                    ?? throw new InvalidOperationException("Failed to deserialize private key.");

                var component = new HomomorphicEncryptionComponent();
                var plaintext = component.Decrypt(encrypted, keyPair);
                Console.WriteLine(plaintext.ToString());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
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
                Console.WriteLine($"Encrypted sum written to '{outFile}'");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
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
                Console.WriteLine($"Encrypted difference written to '{outFile}'");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
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
                Console.WriteLine($"Encrypted product written to '{outFile}'");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        });

        return command;
    }

    private static Command CreateEncryptStringCommand()
    {
        var command = new Command("encrypt-string", "Encrypt a UTF-8 string (max 100 characters) as an array of homomorphically encrypted code points");

        var stringOption = new Option<string>("--string") { Description = "The plaintext string to encrypt (max 100 characters)", Required = true };
        var publicKeyFileOption = new Option<string>("--public-key-file") { Description = "Path to the public key JSON file", Required = true };
        var outFileOption = new Option<string>("--out-file") { Description = "Path to write the encrypted string JSON file", Required = true };

        command.Add(stringOption);
        command.Add(publicKeyFileOption);
        command.Add(outFileOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            try
            {
                var str = parseResult.GetValue(stringOption)!;
                var publicKeyFile = parseResult.GetValue(publicKeyFileOption)!;
                var outFile = parseResult.GetValue(outFileOption)!;

                var keyJson = await File.ReadAllTextAsync(publicKeyFile);
                var publicKey = JsonSerializer.Deserialize<PaillierPublicKey>(keyJson)
                    ?? throw new InvalidOperationException("Failed to deserialize public key.");

                var component = new HomomorphicEncryptionComponent();
                var encrypted = component.EncryptString(str, publicKey);

                await File.WriteAllTextAsync(outFile, JsonSerializer.Serialize(encrypted, JsonOptions));
                Console.WriteLine($"Encrypted string ({encrypted.Characters.Length} characters) written to '{outFile}'");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
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
                var encrypted = JsonSerializer.Deserialize<EncryptedString>(encJson)
                    ?? throw new InvalidOperationException("Failed to deserialize encrypted string.");

                var keyJson = await File.ReadAllTextAsync(privateKeyFile);
                var keyPair = JsonSerializer.Deserialize<PaillierKeyPair>(keyJson)
                    ?? throw new InvalidOperationException("Failed to deserialize private key.");

                var component = new HomomorphicEncryptionComponent();
                Console.WriteLine(component.DecryptString(encrypted, keyPair));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        });

        return command;
    }

    private static Command CreateReplaceCommand()
    {
        var command = new Command("replace", "Replace characters at a known position in an encrypted string without decrypting it");

        var inFileOption = new Option<string>("--in-file") { Description = "Path to the encrypted string JSON file", Required = true };
        var startOption = new Option<int>("--start") { Description = "Zero-based index of the first character to replace", Required = true };
        var replacementOption = new Option<string>("--replacement") { Description = "Plaintext replacement characters", Required = true };
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
                var replacement = parseResult.GetValue(replacementOption)!;
                var outFile = parseResult.GetValue(outFileOption)!;

                var encJson = await File.ReadAllTextAsync(inFile);
                var encrypted = JsonSerializer.Deserialize<EncryptedString>(encJson)
                    ?? throw new InvalidOperationException("Failed to deserialize encrypted string.");

                var component = new HomomorphicEncryptionComponent();
                var updated = component.ReplaceInString(encrypted, start, replacement);

                await File.WriteAllTextAsync(outFile, JsonSerializer.Serialize(updated, JsonOptions));
                Console.WriteLine($"Updated encrypted string written to '{outFile}'");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        });

        return command;
    }

    private static Command CreateSubstringCommand()
    {
        var command = new Command("substring", "Extract a slice of an encrypted string by position without decrypting it");

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
                Console.WriteLine($"Encrypted substring ({substring.Characters.Length} characters) written to '{outFile}'");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        });

        return command;
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
