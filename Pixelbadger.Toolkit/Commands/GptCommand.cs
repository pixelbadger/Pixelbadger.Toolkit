using System.CommandLine;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Services;
using Spectre.Console;

namespace Pixelbadger.Toolkit.Commands;

public static class GptCommand
{
    private const string DefaultModelDir = "./.gpt";

    public static Command Create()
    {
        var command = new Command("gpt", "Train and sample from a tiny from-scratch char-level GPT");

        command.Add(CreateTrainCommand());
        command.Add(CreateCompleteCommand());

        return command;
    }

    /// <summary>Returns the file contents when <paramref name="input"/> is an existing path, otherwise the literal text.</summary>
    internal static async Task<string> ResolveTextOrFilePath(string input)
    {
        var resolvedPath = Path.GetFullPath(input);
        if (File.Exists(resolvedPath))
            return await File.ReadAllTextAsync(resolvedPath);
        return input;
    }

    private static Command CreateTrainCommand()
    {
        var command = new Command("train", "Train a char-level GPT on a text corpus and save a checkpoint");

        var sourceOption = new Option<string>("--source") { Description = "Path to a training corpus file (or literal text)", Required = true };
        var outOption = new Option<string>("--out") { Description = "Checkpoint output directory", DefaultValueFactory = _ => DefaultModelDir };
        var stepsOption = new Option<int>("--steps") { Description = "Number of training steps", DefaultValueFactory = _ => 2000 };
        var batchSizeOption = new Option<int>("--batch-size") { Description = "Sequences per training step", DefaultValueFactory = _ => 16 };
        var blockSizeOption = new Option<int>("--block-size") { Description = "Context length in characters", DefaultValueFactory = _ => 64 };
        var nEmbdOption = new Option<int>("--n-embd") { Description = "Embedding dimension", DefaultValueFactory = _ => 128 };
        var nHeadOption = new Option<int>("--n-head") { Description = "Number of attention heads", DefaultValueFactory = _ => 4 };
        var nLayerOption = new Option<int>("--n-layer") { Description = "Number of transformer blocks", DefaultValueFactory = _ => 3 };
        var lrOption = new Option<float>("--lr") { Description = "Learning rate", DefaultValueFactory = _ => 3e-4f };
        var seedOption = new Option<int>("--seed") { Description = "Random seed for reproducible runs", DefaultValueFactory = _ => 1337 };

        command.Add(sourceOption);
        command.Add(outOption);
        command.Add(stepsOption);
        command.Add(batchSizeOption);
        command.Add(blockSizeOption);
        command.Add(nEmbdOption);
        command.Add(nHeadOption);
        command.Add(nLayerOption);
        command.Add(lrOption);
        command.Add(seedOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            try
            {
                var source = parseResult.GetValue(sourceOption)!;
                var outDir = parseResult.GetValue(outOption)!;
                var options = new GptTrainOptions(
                    Steps: parseResult.GetValue(stepsOption),
                    BatchSize: parseResult.GetValue(batchSizeOption),
                    BlockSize: parseResult.GetValue(blockSizeOption),
                    NEmbd: parseResult.GetValue(nEmbdOption),
                    NHead: parseResult.GetValue(nHeadOption),
                    NLayer: parseResult.GetValue(nLayerOption),
                    LearningRate: parseResult.GetValue(lrOption),
                    Seed: parseResult.GetValue(seedOption));

                var corpus = await ResolveTextOrFilePath(source);

                var component = new GptTrainComponent(new CheckpointService());
                var result = await component.TrainAsync(corpus, outDir, options, (step, loss) =>
                {
                    if (step == 1 || step % 100 == 0 || step == options.Steps)
                        AnsiConsole.MarkupLine($"[grey]step[/] {step}/{options.Steps}  [yellow]loss[/] {loss:F4}");
                });

                AnsiConsole.MarkupLine(
                    $"[green]Trained[/] {result.ParameterCount:N0} params (vocab {result.VocabSize}) — final loss {result.FinalLoss:F4}. Checkpoint: {Markup.Escape(result.CheckpointPath)}");
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
        var command = new Command("complete", "Generate text from a trained GPT checkpoint");

        var modelOption = new Option<string>("--model") { Description = "Checkpoint directory", DefaultValueFactory = _ => DefaultModelDir };
        var promptOption = new Option<string>("--prompt") { Description = "Prompt to continue", Required = true };
        var maxTokensOption = new Option<int>("--max-tokens") { Description = "Number of characters to generate", DefaultValueFactory = _ => 200 };
        var temperatureOption = new Option<float>("--temperature") { Description = "Sampling temperature (0 = greedy/deterministic)", DefaultValueFactory = _ => 0.8f };
        var seedOption = new Option<int>("--seed") { Description = "Random seed for sampling", DefaultValueFactory = _ => 1337 };

        command.Add(modelOption);
        command.Add(promptOption);
        command.Add(maxTokensOption);
        command.Add(temperatureOption);
        command.Add(seedOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            try
            {
                var modelDir = parseResult.GetValue(modelOption)!;
                var prompt = parseResult.GetValue(promptOption)!;
                var maxTokens = parseResult.GetValue(maxTokensOption);
                var temperature = parseResult.GetValue(temperatureOption);
                var seed = parseResult.GetValue(seedOption);

                var component = new GptCompleteComponent(new CheckpointService());
                var result = await component.CompleteAsync(modelDir, prompt, maxTokens, temperature, seed);

                AnsiConsole.WriteLine(result.Text);
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
