using System.CommandLine;
using Pixelbadger.Toolkit.Components;

namespace Pixelbadger.Toolkit.Commands;

public static class BrainfuckCommand
{
    public static Command Create()
    {
        var command = new Command("brainfuck", "Executes a Brainfuck program from a file");

        var fileOption = new Option<string>(
            aliases: ["--file", "-f"],
            description: "Path to the Brainfuck program file")
        {
            IsRequired = true
        };

        command.AddOption(fileOption);

        command.SetHandler(async (string filePath) =>
        {
            try
            {
                var interpreter = new BrainfuckInterpreter();
                var result = await interpreter.ExecuteAsync(filePath);
                
                Console.Write(result);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        }, fileOption);

        return command;
    }
}