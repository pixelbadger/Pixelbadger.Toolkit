using System.CommandLine;
using Pixelbadger.Toolkit.Components;

namespace Pixelbadger.Toolkit.Commands;

public static class OokCommand
{
    public static Command Create()
    {
        var command = new Command("ook", "Executes an Ook program from a file");

        var fileOption = new Option<string>(
            aliases: ["--file", "-f"],
            description: "Path to the Ook program file")
        {
            IsRequired = true
        };

        command.AddOption(fileOption);

        command.SetHandler(async (string filePath) =>
        {
            try
            {
                var interpreter = new OokInterpreter();
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