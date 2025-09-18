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

}