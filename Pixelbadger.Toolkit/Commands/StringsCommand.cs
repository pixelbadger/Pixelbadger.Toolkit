using System.CommandLine;
using Pixelbadger.Toolkit.Components;

namespace Pixelbadger.Toolkit.Commands;

public static class StringsCommand
{
    public static Command Create()
    {
        var command = new Command("strings", "String manipulation utilities");

        command.AddCommand(CreateReverseCommand());
        command.AddCommand(CreateLevenshteinDistanceCommand());
        command.AddCommand(CreateAbjadifyCommand());
        command.AddCommand(CreateFleschReadingEaseCommand());

        return command;
    }

    private static Command CreateReverseCommand()
    {
        var command = new Command("reverse", "Reverses the content of a file");

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

    private static Command CreateAbjadifyCommand()
    {
        var command = new Command("abjadify", "Strips English vowels from text while preserving single vowel words");

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
                var abjadifyComponent = new AbjadifyComponent();
                await abjadifyComponent.AbjadifyFileAsync(inFile, outFile);

                Console.WriteLine($"Successfully abjadified content from '{inFile}' to '{outFile}'");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        }, inFileOption, outFileOption);

        return command;
    }

    private static Command CreateFleschReadingEaseCommand()
    {
        var command = new Command("flesch-reading-ease", "Analyzes plain text readability using the Flesch Reading Ease score");

        var inFileOption = new Option<string>(
            aliases: ["--in-file"],
            description: "Input plain-text file path")
        {
            IsRequired = true
        };

        command.AddOption(inFileOption);

        command.SetHandler(async (string inFile) =>
        {
            try
            {
                var component = new FleschReadingEaseComponent();
                var result = await component.AnalyzeFileAsync(inFile);

                Console.WriteLine($"Flesch Reading Ease: {result.Score:F2}");
                Console.WriteLine($"Readability: {result.ReadabilityBand}");
                Console.WriteLine($"Sentences: {result.Sentences}");
                Console.WriteLine($"Words: {result.Words}");
                Console.WriteLine($"Syllables: {result.Syllables}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        }, inFileOption);

        return command;
    }
}
