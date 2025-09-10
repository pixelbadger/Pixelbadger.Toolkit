using System.CommandLine;
using Pixelbadger.Toolkit.Components;

namespace Pixelbadger.Toolkit.Commands;

public static class SteganographyCommand
{
    public static Command Create()
    {
        var command = new Command("steganography", "Encode or decode hidden messages in images");
        
        var modeOption = new Option<string>(
            aliases: ["--mode"],
            description: "Operation mode: encode or decode")
        {
            IsRequired = true
        };
        modeOption.AddValidator(result =>
        {
            var mode = result.GetValueOrDefault<string>();
            if (mode != "encode" && mode != "decode")
            {
                result.ErrorMessage = "Mode must be either 'encode' or 'decode'";
            }
        });
        
        var imagePathOption = new Option<string>(
            aliases: ["--image"],
            description: "Path to the input image file")
        {
            IsRequired = true
        };
        
        var messageOption = new Option<string>(
            aliases: ["--message"],
            description: "Message to encode (required for encode mode)")
        {
            IsRequired = false
        };
        
        var outputPathOption = new Option<string>(
            aliases: ["--output"],
            description: "Path for the output image file (required for encode mode)")
        {
            IsRequired = false
        };
        
        command.AddOption(modeOption);
        command.AddOption(imagePathOption);
        command.AddOption(messageOption);
        command.AddOption(outputPathOption);
        
        command.SetHandler(async (string mode, string imagePath, string? message, string? outputPath) =>
        {
            try
            {
                var steganography = new ImageSteganography();
                
                if (mode == "encode")
                {
                    if (string.IsNullOrEmpty(message))
                    {
                        Console.WriteLine("Error: --message is required for encode mode");
                        Environment.Exit(1);
                        return;
                    }
                    
                    if (string.IsNullOrEmpty(outputPath))
                    {
                        Console.WriteLine("Error: --output is required for encode mode");
                        Environment.Exit(1);
                        return;
                    }
                    
                    await steganography.EncodeMessageAsync(imagePath, message, outputPath);
                    Console.WriteLine($"Successfully encoded message into '{outputPath}'");
                }
                else if (mode == "decode")
                {
                    var decodedMessage = await steganography.DecodeMessageAsync(imagePath);
                    Console.WriteLine($"Decoded message: {decodedMessage}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        }, modeOption, imagePathOption, messageOption, outputPathOption);
        
        return command;
    }
}