using System.CommandLine;
using Pixelbadger.Toolkit.Components;

namespace Pixelbadger.Toolkit.Commands;

public static class LevenshteinDistanceCommand
{
    public static Command Create()
    {
        var command = new Command("levenshtein-distance", "Calculates the Levenshtein distance between two strings or files");

        var string1Option = new Option<string>(
            aliases: ["--string1", "-s1"],
            description: "First string or path to text file")
        {
            IsRequired = true
        };

        var string2Option = new Option<string>(
            aliases: ["--string2", "-s2"],
            description: "Second string or path to text file")
        {
            IsRequired = true
        };

        command.AddOption(string1Option);
        command.AddOption(string2Option);

        command.SetHandler(async (string str1, string str2) =>
        {
            try
            {
                var calculator = new LevenshteinCalculator();
                var distance = await calculator.CalculateDistanceAsync(str1, str2);
                
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