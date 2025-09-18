using System.CommandLine;
using Pixelbadger.Toolkit.Components;

namespace Pixelbadger.Toolkit.Commands;

public static class ImagesCommand
{
    public static Command Create()
    {
        var command = new Command("images", "Image processing and manipulation utilities");

        command.AddCommand(CreateSteganographyCommand());

        return command;
    }

    private static Command CreateSteganographyCommand()
    {
        var command = new Command("steganography", "Encode or decode hidden messages in images using LSB steganography");

        var modeOption = new Option<string>(
            aliases: ["--mode"],
            description: "Operation mode: 'encode' or 'decode'")
        {
            IsRequired = true
        };

        var imageOption = new Option<string>(
            aliases: ["--image"],
            description: "Input image file path")
        {
            IsRequired = true
        };

        var messageOption = new Option<string?>(
            aliases: ["--message"],
            description: "Message to encode (required for encode mode)")
        {
            IsRequired = false
        };

        var outputOption = new Option<string?>(
            aliases: ["--output"],
            description: "Output image file path (required for encode mode)")
        {
            IsRequired = false
        };

        command.AddOption(modeOption);
        command.AddOption(imageOption);
        command.AddOption(messageOption);
        command.AddOption(outputOption);

        command.SetHandler(async (string mode, string image, string? message, string? output) =>
        {
            try
            {
                var steganography = new ImageSteganography();

                if (mode.ToLower() == "encode")
                {
                    if (string.IsNullOrEmpty(message))
                    {
                        Console.WriteLine("Error: --message is required for encode mode");
                        Environment.Exit(1);
                        return;
                    }

                    if (string.IsNullOrEmpty(output))
                    {
                        Console.WriteLine("Error: --output is required for encode mode");
                        Environment.Exit(1);
                        return;
                    }

                    await steganography.EncodeMessageAsync(image, message, output);
                    Console.WriteLine($"Message encoded successfully in '{output}'");
                }
                else if (mode.ToLower() == "decode")
                {
                    var decodedMessage = await steganography.DecodeMessageAsync(image);
                    Console.WriteLine($"Decoded message: {decodedMessage}");
                }
                else
                {
                    Console.WriteLine("Error: Mode must be 'encode' or 'decode'");
                    Environment.Exit(1);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        }, modeOption, imageOption, messageOption, outputOption);

        return command;
    }

}