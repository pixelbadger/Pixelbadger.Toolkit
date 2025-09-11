using System.CommandLine;
using Pixelbadger.Toolkit.Components;

namespace Pixelbadger.Toolkit.Commands;

public static class AlgorithmsCommand
{
    public static Command Create()
    {
        var command = new Command("algorithms", "Algorithm implementations and utilities");

        command.AddCommand(CreateLevenshteinDistanceCommand());

        return command;
    }

    private static Command CreateLevenshteinDistanceCommand()
    {
        var command = new Command("levenshtein-distance", "Calculates the Levenshtein distance between two strings or files");

        var string1Option = new Option<string>(
            aliases: ["--string1"],
            description: "First string or file path")
        {
            IsRequired = true
        };

        var string2Option = new Option<string>(
            aliases: ["--string2"],
            description: "Second string or file path")
        {
            IsRequired = true
        };

        command.AddOption(string1Option);
        command.AddOption(string2Option);

        command.SetHandler(async (string string1, string string2) =>
        {
            try
            {
                var calculator = new LevenshteinCalculator();
                var distance = await calculator.CalculateDistanceAsync(string1, string2);
                
                Console.WriteLine($"Levenshtein distance: {distance}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        }, string1Option, string2Option);

        return command;
    }
}