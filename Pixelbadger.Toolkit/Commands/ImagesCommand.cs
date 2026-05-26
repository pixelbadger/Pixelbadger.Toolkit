using System.CommandLine;
using Pixelbadger.Toolkit.Components;

namespace Pixelbadger.Toolkit.Commands;

public static class ImagesCommand
{
    public static Command Create()
    {
        var command = new Command("images", "Image processing and manipulation utilities");

        command.Add(CreateSteganographyCommand());

        return command;
    }

    private static Command CreateSteganographyCommand()
    {
        var command = new Command("steganography", "Encode or decode hidden messages in images using LSB steganography");

        var modeOption = new Option<string>("--mode") { Description = "Operation mode: 'encode' or 'decode'", Required = true };
        var imageOption = new Option<string>("--image") { Description = "Input image file path", Required = true };
        var messageOption = new Option<string?>("--message") { Description = "Message to encode (required for encode mode)" };
        var outputOption = new Option<string?>("--output") { Description = "Output image file path (required for encode mode)" };

        command.Add(modeOption);
        command.Add(imageOption);
        command.Add(messageOption);
        command.Add(outputOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            try
            {
                var mode = parseResult.GetValue(modeOption)!;
                var image = parseResult.GetValue(imageOption)!;
                var message = parseResult.GetValue(messageOption);
                var output = parseResult.GetValue(outputOption);
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
        });

        return command;
    }
}
