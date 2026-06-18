using System.CommandLine;
using Pixelbadger.Toolkit.Components;

namespace Pixelbadger.Toolkit.Commands;

public static class StringsCommand
{
    public static Command Create()
    {
        var command = new Command("strings", "String manipulation utilities");

        command.Add(CreateReverseCommand());
        command.Add(CreateLevenshteinDistanceCommand());
        command.Add(CreateAbjadifyCommand());
        command.Add(CreateFleschReadingEaseCommand());
        command.Add(CreateReportCommand());

        return command;
    }

    private static Command CreateReverseCommand()
    {
        var command = new Command("reverse", "Reverses the content of a file");

        var inFileOption = new Option<string>("--in-file") { Description = "Input file path", Required = true };
        var outFileOption = new Option<string>("--out-file") { Description = "Output file path", Required = true };

        command.Add(inFileOption);
        command.Add(outFileOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            try
            {
                var inFile = parseResult.GetValue(inFileOption)!;
                var outFile = parseResult.GetValue(outFileOption)!;
                var stringReverser = new StringReverser();
                await stringReverser.ReverseFileAsync(inFile, outFile);

                Console.WriteLine($"Successfully reversed content from '{inFile}' to '{outFile}'");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        });

        return command;
    }

    private static Command CreateLevenshteinDistanceCommand()
    {
        var command = new Command("levenshtein-distance", "Calculates the Levenshtein distance between two strings or files");

        var string1Option = new Option<string>("--string1") { Description = "First string or file path", Required = true };
        var string2Option = new Option<string>("--string2") { Description = "Second string or file path", Required = true };

        command.Add(string1Option);
        command.Add(string2Option);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            try
            {
                var string1 = parseResult.GetValue(string1Option)!;
                var string2 = parseResult.GetValue(string2Option)!;
                var calculator = new LevenshteinCalculator();
                var distance = await calculator.CalculateDistanceAsync(string1, string2);

                Console.WriteLine($"Levenshtein distance: {distance}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        });

        return command;
    }

    private static Command CreateAbjadifyCommand()
    {
        var command = new Command("abjadify", "Strips English vowels from text while preserving single vowel words");

        var inFileOption = new Option<string>("--in-file") { Description = "Input file path", Required = true };
        var outFileOption = new Option<string>("--out-file") { Description = "Output file path", Required = true };

        command.Add(inFileOption);
        command.Add(outFileOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            try
            {
                var inFile = parseResult.GetValue(inFileOption)!;
                var outFile = parseResult.GetValue(outFileOption)!;
                var abjadifyComponent = new AbjadifyComponent();
                await abjadifyComponent.AbjadifyFileAsync(inFile, outFile);

                Console.WriteLine($"Successfully abjadified content from '{inFile}' to '{outFile}'");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        });

        return command;
    }

    private static Command CreateFleschReadingEaseCommand()
    {
        var command = new Command("flesch-reading-ease", "Analyzes plain text readability using the Flesch Reading Ease score");

        var inFileOption = new Option<string>("--in-file") { Description = "Input plain-text file path", Required = true };

        command.Add(inFileOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            try
            {
                var inFile = parseResult.GetValue(inFileOption)!;
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
        });

        return command;
    }

    private static Command CreateReportCommand()
    {
        var command = new Command("report", "Performs a full text analysis of the input text");

        var inFileOption = new Option<string?>("--in-file") { Description = "Input file path" };
        var stringOption = new Option<string?>("--string") { Description = "Input text string" };

        command.Add(inFileOption);
        command.Add(stringOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            try
            {
                var inFile = parseResult.GetValue(inFileOption);
                var inputString = parseResult.GetValue(stringOption);

                if (inFile is null && inputString is null)
                {
                    Console.WriteLine("Error: Either '--in-file' or '--string' must be provided.");
                    Environment.Exit(1);
                    return;
                }

                if (inFile is not null && inputString is not null)
                {
                    Console.WriteLine("Error: Only one of '--in-file' or '--string' may be provided.");
                    Environment.Exit(1);
                    return;
                }

                var component = new StringReportComponent();
                StringReportResult result;

                if (inFile is not null)
                    result = await component.AnalyzeFileAsync(inFile);
                else
                    result = component.AnalyzeText(inputString!);

                Console.WriteLine($"Characters (with spaces):    {result.Characters}");
                Console.WriteLine($"Characters (without spaces): {result.CharactersNoSpaces}");
                Console.WriteLine($"Words:                       {result.Words}");
                Console.WriteLine($"Unique words:                {result.UniqueWords}");
                Console.WriteLine($"Sentences:                   {result.Sentences}");
                Console.WriteLine($"Paragraphs:                  {result.Paragraphs}");
                Console.WriteLine($"Avg words per sentence:      {result.AverageWordsPerSentence:F1}");
                Console.WriteLine($"Avg sentences per paragraph: {result.AverageSentencesPerParagraph:F1}");
                Console.WriteLine($"Estimated pages:             {result.EstimatedPages}");
                Console.WriteLine($"Est. reading time:           {FormatReadingTime(result.EstimatedReadingTimeSeconds)}");
                Console.WriteLine($"Flesch Reading Ease:         {result.FleschReadingEase:F2}");
                Console.WriteLine($"Readability:                 {result.ReadabilityBand}");
                Console.WriteLine($"Longest word:                {result.LongestWord}");
                Console.WriteLine($"Most common word:            {result.MostCommonWord}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        });

        return command;
    }

    internal static string FormatReadingTime(int totalSeconds)
    {
        if (totalSeconds < 60)
            return $"{totalSeconds}s";

        var minutes = totalSeconds / 60;
        var seconds = totalSeconds % 60;
        return seconds == 0 ? $"{minutes}m" : $"{minutes}m {seconds}s";
    }
}
