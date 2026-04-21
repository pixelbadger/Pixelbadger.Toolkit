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

        command.AddCommand(CreateGenerateKeyCommand());
        command.AddCommand(CreateEncryptCommand());
        command.AddCommand(CreateDecryptCommand());
        command.AddCommand(CreateAddCommand());

        return command;
    }

    private static Command CreateGenerateKeyCommand()
    {
        var command = new Command("generate-key", "Generate a Paillier key pair and save it to a file");

        var keyFileOption = new Option<string>(
            aliases: ["--key-file"],
            description: "Path to write the generated key pair JSON file")
        {
            IsRequired = true
        };

        command.AddOption(keyFileOption);

        command.SetHandler(async (string keyFile) =>
        {
            try
            {
                var component = new HomomorphicEncryptionComponent();
                var keyPair = component.GenerateKey();
                var json = JsonSerializer.Serialize(keyPair, JsonOptions);
                await File.WriteAllTextAsync(keyFile, json);
                Console.WriteLine($"Key pair written to '{keyFile}'");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        }, keyFileOption);

        return command;
    }

    private static Command CreateEncryptCommand()
    {
        var command = new Command("encrypt", "Encrypt a number using a Paillier public key");

        var numberOption = new Option<long>(
            aliases: ["--number"],
            description: "The non-negative integer to encrypt")
        {
            IsRequired = true
        };

        var keyFileOption = new Option<string>(
            aliases: ["--key-file"],
            description: "Path to the key pair JSON file")
        {
            IsRequired = true
        };

        var outFileOption = new Option<string>(
            aliases: ["--out-file"],
            description: "Path to write the encrypted number JSON file")
        {
            IsRequired = true
        };

        command.AddOption(numberOption);
        command.AddOption(keyFileOption);
        command.AddOption(outFileOption);

        command.SetHandler(async (long number, string keyFile, string outFile) =>
        {
            try
            {
                var keyJson = await File.ReadAllTextAsync(keyFile);
                var keyPair = JsonSerializer.Deserialize<PaillierKeyPair>(keyJson)
                    ?? throw new InvalidOperationException("Failed to deserialize key pair.");

                var component = new HomomorphicEncryptionComponent();
                var encrypted = component.Encrypt(number, keyPair);

                var json = JsonSerializer.Serialize(encrypted, JsonOptions);
                await File.WriteAllTextAsync(outFile, json);
                Console.WriteLine($"Encrypted number written to '{outFile}'");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        }, numberOption, keyFileOption, outFileOption);

        return command;
    }

    private static Command CreateDecryptCommand()
    {
        var command = new Command("decrypt", "Decrypt an encrypted number using a Paillier private key");

        var inFileOption = new Option<string>(
            aliases: ["--in-file"],
            description: "Path to the encrypted number JSON file")
        {
            IsRequired = true
        };

        var keyFileOption = new Option<string>(
            aliases: ["--key-file"],
            description: "Path to the key pair JSON file")
        {
            IsRequired = true
        };

        command.AddOption(inFileOption);
        command.AddOption(keyFileOption);

        command.SetHandler(async (string inFile, string keyFile) =>
        {
            try
            {
                var encryptedJson = await File.ReadAllTextAsync(inFile);
                var encrypted = JsonSerializer.Deserialize<EncryptedNumber>(encryptedJson)
                    ?? throw new InvalidOperationException("Failed to deserialize encrypted number.");

                var keyJson = await File.ReadAllTextAsync(keyFile);
                var keyPair = JsonSerializer.Deserialize<PaillierKeyPair>(keyJson)
                    ?? throw new InvalidOperationException("Failed to deserialize key pair.");

                var component = new HomomorphicEncryptionComponent();
                var plaintext = component.Decrypt(encrypted, keyPair);
                Console.WriteLine(plaintext.ToString());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        }, inFileOption, keyFileOption);

        return command;
    }

    private static Command CreateAddCommand()
    {
        var command = new Command("add", "Homomorphically add two encrypted numbers without decrypting them");

        var inFile1Option = new Option<string>(
            aliases: ["--in-file1"],
            description: "Path to the first encrypted number JSON file")
        {
            IsRequired = true
        };

        var inFile2Option = new Option<string>(
            aliases: ["--in-file2"],
            description: "Path to the second encrypted number JSON file")
        {
            IsRequired = true
        };

        var outFileOption = new Option<string>(
            aliases: ["--out-file"],
            description: "Path to write the encrypted sum JSON file")
        {
            IsRequired = true
        };

        command.AddOption(inFile1Option);
        command.AddOption(inFile2Option);
        command.AddOption(outFileOption);

        command.SetHandler(async (string inFile1, string inFile2, string outFile) =>
        {
            try
            {
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
        }, inFile1Option, inFile2Option, outFileOption);

        return command;
    }
}
