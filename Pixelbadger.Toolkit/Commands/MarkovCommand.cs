using System.CommandLine;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Services;
using Spectre.Console;

namespace Pixelbadger.Toolkit.Commands;

public static class MarkovCommand
{
    private static string DefaultModelDirectory =>
        Path.Combine(Directory.GetCurrentDirectory(), ".Markov");

    public static Command Create()
    {
        var command = new Command("markov", "Markov chain text generation utilities");

        command.Add(CreateTrainCommand());
        command.Add(CreateCompleteCommand());

        return command;
    }

    private static Command CreateTrainCommand()
    {
        var command = new Command("train", "Trains a Markov chain model from a source text file");

        var sourceOption = new Option<string>("--source") { Description = "Path to the source text file", Required = true };

        command.Add(sourceOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            try
            {
                var source = parseResult.GetValue(sourceOption)!;
                var component = new MarkovTrainComponent(new MarkovModelService());
                var uniqueWords = await component.TrainAsync(source, DefaultModelDirectory);

                AnsiConsole.MarkupLine($"[green]Model trained successfully from '{Markup.Escape(source)}' ({uniqueWords} unique transition words). Model saved to '{Markup.Escape(DefaultModelDirectory)}'.[/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
                Environment.Exit(1);
            }
        });

        return command;
    }

    private static Command CreateCompleteCommand()
    {
        var command = new Command("complete", "Generates a text completion using the trained Markov chain model");

        var textOption = new Option<string>("--text") { Description = "Input text to complete", Required = true };
        var countOption = new Option<int>("--count") { Description = "Number of words to generate (default: 50)", DefaultValueFactory = _ => 50 };

        command.Add(textOption);
        command.Add(countOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            try
            {
                var text = parseResult.GetValue(textOption)!;
                var count = parseResult.GetValue(countOption);
                var component = new MarkovCompleteComponent(new MarkovModelService());
                var result = await component.CompleteAsync(text, DefaultModelDirectory, count);

                AnsiConsole.WriteLine(result);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
                Environment.Exit(1);
            }
        });

        return command;
    }
}
