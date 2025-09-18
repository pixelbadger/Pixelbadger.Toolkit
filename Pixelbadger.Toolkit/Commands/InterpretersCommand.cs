using System.CommandLine;
using Pixelbadger.Toolkit.Components;

namespace Pixelbadger.Toolkit.Commands;

public static class InterpretersCommand
{
    public static Command Create()
    {
        var command = new Command("interpreters", "Esoteric programming language interpreters");

        command.AddCommand(CreateBrainfuckCommand());
        command.AddCommand(CreateOokCommand());
        command.AddCommand(CreateBfToOokCommand());

        return command;
    }

    private static Command CreateBrainfuckCommand()
    {
        var command = new Command("brainfuck", "Executes a Brainfuck program from a file");

        var fileOption = new Option<string>(
            aliases: ["--file"],
            description: "Path to the Brainfuck program file")
        {
            IsRequired = true
        };

        command.AddOption(fileOption);

        command.SetHandler(async (string file) =>
        {
            try
            {
                var interpreter = new BrainfuckInterpreter();
                var result = await interpreter.ExecuteAsync(file);
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

    private static Command CreateOokCommand()
    {
        var command = new Command("ook", "Executes an Ook program from a file");

        var fileOption = new Option<string>(
            aliases: ["--file"],
            description: "Path to the Ook program file")
        {
            IsRequired = true
        };

        command.AddOption(fileOption);

        command.SetHandler(async (string file) =>
        {
            try
            {
                var interpreter = new OokInterpreter();
                var result = await interpreter.ExecuteAsync(file);
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

    private static Command CreateBfToOokCommand()
    {
        var command = new Command("bf-to-ook", "Converts a Brainfuck program to Ook language");

        var sourceOption = new Option<string>(
            aliases: ["--source"],
            description: "Path to the source Brainfuck program file")
        {
            IsRequired = true
        };

        var outputOption = new Option<string>(
            aliases: ["--output"],
            description: "Path to the output Ook program file")
        {
            IsRequired = true
        };

        command.AddOption(sourceOption);
        command.AddOption(outputOption);

        command.SetHandler(async (string source, string output) =>
        {
            try
            {
                var component = new BfToOokComponent();
                await component.TranslateFileAsync(source, output);
                Console.WriteLine($"Successfully converted {source} to Ook language and saved to {output}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        }, sourceOption, outputOption);

        return command;
    }

}