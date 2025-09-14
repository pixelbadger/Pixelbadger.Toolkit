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
        command.AddCommand(CreatePietCommand());

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

    private static Command CreatePietCommand()
    {
        var command = new Command("piet", "Executes a Piet program from an image file");

        var fileOption = new Option<string>(
            aliases: ["--file"],
            description: "Path to the Piet program image file (PNG, JPG, etc.)")
        {
            IsRequired = true
        };

        var codelSizeOption = new Option<int>(
            aliases: ["--codel-size"],
            description: "Size of each codel in pixels (default: 1)")
        {
            IsRequired = false
        };
        codelSizeOption.SetDefaultValue(1);

        var debugOption = new Option<bool>(
            aliases: ["--debug"],
            description: "Enable debug output to trace program execution")
        {
            IsRequired = false
        };
        debugOption.SetDefaultValue(false);

        command.AddOption(fileOption);
        command.AddOption(codelSizeOption);
        command.AddOption(debugOption);

        command.SetHandler(async (string file, int codelSize, bool debug) =>
        {
            try
            {
                var interpreter = new PietInterpreter(file, codelSize, debug);
                await interpreter.ExecuteAsync();
                Console.WriteLine(); // Add newline after execution
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        }, fileOption, codelSizeOption, debugOption);

        return command;
    }
}