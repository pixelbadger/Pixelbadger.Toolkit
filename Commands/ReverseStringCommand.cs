using System.CommandLine;
using Pixelbadger.Toolkit.Components;

namespace Pixelbadger.Toolkit.Commands;

public static class ReverseStringCommand
{
    public static Command Create()
    {
        var command = new Command("reverse-string", "Reverses the content of a file");

        var inFileOption = new Option<string>(
            aliases: ["--in-file"],
            description: "Input file path")
        {
            IsRequired = true
        };

        var outFileOption = new Option<string>(
            aliases: ["--out-file"],
            description: "Output file path")
        {
            IsRequired = true
        };

        command.AddOption(inFileOption);
        command.AddOption(outFileOption);

        command.SetHandler(async (string inFile, string outFile) =>
        {
            try
            {
                var stringReverser = new StringReverser();
                await stringReverser.ReverseFileAsync(inFile, outFile);
                
                Console.WriteLine($"Successfully reversed content from '{inFile}' to '{outFile}'");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        }, inFileOption, outFileOption);

        return command;
    }
}