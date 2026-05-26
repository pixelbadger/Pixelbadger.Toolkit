using System.CommandLine;
using Pixelbadger.Toolkit.Components;

namespace Pixelbadger.Toolkit.Commands;

public static class InterpretersCommand
{
    public static Command Create()
    {
        var command = new Command("interpreters", "Esoteric programming language interpreters");

        command.Add(CreateBrainfuckCommand());
        command.Add(CreateOokCommand());
        command.Add(CreateBfToOokCommand());

        return command;
    }

    private static Command CreateBrainfuckCommand()
    {
        var command = new Command("brainfuck", "Executes a Brainfuck program from a file");

        var fileOption = new Option<string>("--file") { Description = "Path to the Brainfuck program file", Required = true };

        command.Add(fileOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            try
            {
                var file = parseResult.GetValue(fileOption)!;
                var interpreter = new BrainfuckInterpreter();
                var result = await interpreter.ExecuteAsync(file);
                Console.Write(result);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        });

        return command;
    }

    private static Command CreateOokCommand()
    {
        var command = new Command("ook", "Executes an Ook program from a file");

        var fileOption = new Option<string>("--file") { Description = "Path to the Ook program file", Required = true };

        command.Add(fileOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            try
            {
                var file = parseResult.GetValue(fileOption)!;
                var interpreter = new OokInterpreter();
                var result = await interpreter.ExecuteAsync(file);
                Console.Write(result);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        });

        return command;
    }

    private static Command CreateBfToOokCommand()
    {
        var command = new Command("bf-to-ook", "Converts a Brainfuck program to Ook language");

        var sourceOption = new Option<string>("--source") { Description = "Path to the source Brainfuck program file", Required = true };
        var outputOption = new Option<string>("--output") { Description = "Path to the output Ook program file", Required = true };

        command.Add(sourceOption);
        command.Add(outputOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            try
            {
                var source = parseResult.GetValue(sourceOption)!;
                var output = parseResult.GetValue(outputOption)!;
                var component = new BfToOokComponent();
                await component.TranslateFileAsync(source, output);
                Console.WriteLine($"Successfully converted {source} to Ook language and saved to {output}");
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
