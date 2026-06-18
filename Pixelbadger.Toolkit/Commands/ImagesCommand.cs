using System.CommandLine;
using Pixelbadger.Toolkit.Components;
using Spectre.Console;

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
                        AnsiConsole.MarkupLine("[red]Error:[/] --message is required for encode mode");
                        Environment.Exit(1);
                        return;
                    }

                    if (string.IsNullOrEmpty(output))
                    {
                        AnsiConsole.MarkupLine("[red]Error:[/] --output is required for encode mode");
                        Environment.Exit(1);
                        return;
                    }

                    await steganography.EncodeMessageAsync(image, message, output);
                    AnsiConsole.MarkupLine($"[green]Message encoded successfully in '{Markup.Escape(output)}'[/]");
                }
                else if (mode.ToLower() == "decode")
                {
                    var decodedMessage = await steganography.DecodeMessageAsync(image);
                    AnsiConsole.MarkupLine($"Decoded message: {Markup.Escape(decodedMessage)}");
                }
                else
                {
                    AnsiConsole.MarkupLine("[red]Error:[/] Mode must be 'encode' or 'decode'");
                    Environment.Exit(1);
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
                Environment.Exit(1);
            }
        });

        return command;
    }
}
