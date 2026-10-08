using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Services;
using Spectre.Console;

namespace Pixelbadger.Toolkit.Commands;

public static class LlmCommand
{
    public static Command Create()
    {
        var command = new Command("llm", "LLM utilities");

        command.Add(CreateChatCommand());
        command.Add(CreateTranslateCommand());
        command.Add(CreateOcaaarCommand());
        command.Add(CreateCorpospeakCommand());
        command.Add(CreateGenerateImageCommand());
        command.Add(CreateHistoryCommand());

        return command;
    }

    /// <summary>Provider names accepted by --provider. Add new providers here and in <see cref="BuildLlmServices"/>.</summary>
    internal static readonly string[] SupportedProviders = [LlmProviders.OpenAi, LlmProviders.Claude];

    private static Option<string> CreateProviderOption()
    {
        var option = new Option<string>("--provider")
        {
            Description = $"The LLM provider to use (supported: {string.Join(", ", SupportedProviders)})",
            DefaultValueFactory = _ => LlmProviders.OpenAi
        };
        option.AcceptOnlyFromAmong(SupportedProviders);
        return option;
    }

    private static Option<string?> CreateModelOption(string description = "The model to use (defaults to the provider's default model)")
        => new("--model") { Description = description };

    /// <summary>
    /// Composition root: registers exactly one provider module plus the provider-neutral services.
    /// Nothing here reads an API key; keys are only read when a provider service is resolved.
    /// </summary>
    internal static ServiceProvider BuildLlmServices(string provider, string? model)
    {
        var services = new ServiceCollection();

        switch (provider)
        {
            case LlmProviders.OpenAi:
                services.AddOpenAiLlmProvider(model);
                break;
            case LlmProviders.Claude:
                services.AddClaudeLlmProvider(model);
                break;
            default:
                throw new ArgumentException($"Unknown provider '{provider}'. Supported providers: {string.Join(", ", SupportedProviders)}", nameof(provider));
        }

        services.AddSingleton<ILlmCompatibilityValidator, LlmCompatibilityValidator>();
        services.AddSingleton<IHistoryService, HistoryService>();
        services.AddTransient<ChatComponent>();
        services.AddTransient<TranslateComponent>();
        services.AddTransient<OcaaarComponent>();
        services.AddTransient<CorpospeakComponent>();
        services.AddTransient<GenerateImageComponent>();

        return services.BuildServiceProvider();
    }

    private static readonly IReadOnlySet<LlmCapability> TextChatOnly = new HashSet<LlmCapability> { LlmCapability.TextChat };

    /// <summary>
    /// Builds the provider, validates compatibility BEFORE resolving any provider service (and so before any
    /// API-key lookup, file read, history DB access or network call), then runs the action. Returns the exit code.
    /// </summary>
    internal static async Task<int> RunLlmActionAsync(
        string provider,
        string? model,
        LlmActionRequirements requirements,
        Func<IServiceProvider, LlmCompatibilityResult, Task> action)
    {
        try
        {
            using var services = BuildLlmServices(provider, model);

            var compatibility = services.GetRequiredService<ILlmCompatibilityValidator>().Validate(requirements);
            if (!compatibility.IsCompatible)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(compatibility.Error!)}");
                return 1;
            }

            await action(services, compatibility);
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
    }

    private static Command CreateChatCommand()
    {
        var command = new Command("chat", "Chat with an LLM maintaining conversation history");

        var messageOption = new Option<string>("--message") { Description = "The message to send to the LLM", Required = true };
        var sessionIdOption = new Option<long?>("--session-id") { Description = "Session ID to continue a previous conversation (omit to start a new session)" };
        var modelOption = CreateModelOption();
        var providerOption = CreateProviderOption();
        var reasoningEffortOption = new Option<string?>("--reasoning-effort") { Description = "Reasoning effort level (supported values depend on --provider)" };

        command.Add(messageOption);
        command.Add(sessionIdOption);
        command.Add(modelOption);
        command.Add(providerOption);
        command.Add(reasoningEffortOption);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var message = parseResult.GetValue(messageOption)!;
            var sessionId = parseResult.GetValue(sessionIdOption);
            var reasoningEffort = parseResult.GetValue(reasoningEffortOption);

            return RunLlmActionAsync(
                parseResult.GetValue(providerOption)!,
                parseResult.GetValue(modelOption),
                new LlmActionRequirements("chat", TextChatOnly, reasoningEffort),
                async (services, compatibility) =>
                {
                    var result = await services.GetRequiredService<ChatComponent>()
                        .ChatAsync(message, sessionId, compatibility.NormalisedReasoningEffort);

                    AnsiConsole.WriteLine(result.Response);
                    Console.Error.WriteLine($"Session: {result.SessionId}");
                });
        });

        return command;
    }

    private static Command CreateTranslateCommand()
    {
        var command = new Command("translate", "Translate text to a target language using an LLM");

        var textOption = new Option<string>("--text") { Description = "The text to translate", Required = true };
        var targetLanguageOption = new Option<string>("--target-language") { Description = "The target language to translate to", Required = true };
        var modelOption = CreateModelOption();
        var providerOption = CreateProviderOption();

        command.Add(textOption);
        command.Add(targetLanguageOption);
        command.Add(modelOption);
        command.Add(providerOption);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var text = parseResult.GetValue(textOption)!;
            var targetLanguage = parseResult.GetValue(targetLanguageOption)!;

            return RunLlmActionAsync(
                parseResult.GetValue(providerOption)!,
                parseResult.GetValue(modelOption),
                new LlmActionRequirements("translate", TextChatOnly),
                async (services, _) =>
                {
                    var translation = await services.GetRequiredService<TranslateComponent>()
                        .TranslateAsync(text, targetLanguage);

                    AnsiConsole.WriteLine(translation);
                });
        });

        return command;
    }

    private static Command CreateOcaaarCommand()
    {
        var command = new Command("ocaaar", "Extract text from an image and translate it to pirate speak");

        var imagePathOption = new Option<string>("--image-path") { Description = "Path to the image file to process", Required = true };
        var modelOption = CreateModelOption();
        var providerOption = CreateProviderOption();

        command.Add(imagePathOption);
        command.Add(modelOption);
        command.Add(providerOption);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var imagePath = parseResult.GetValue(imagePathOption)!;

            return RunLlmActionAsync(
                parseResult.GetValue(providerOption)!,
                parseResult.GetValue(modelOption),
                new LlmActionRequirements("ocaaar", new HashSet<LlmCapability> { LlmCapability.TextChat, LlmCapability.ImageInput }),
                async (services, _) =>
                {
                    var response = await services.GetRequiredService<OcaaarComponent>().OcaaarAsync(imagePath);

                    AnsiConsole.WriteLine(response);
                });
        });

        return command;
    }

    private static Command CreateCorpospeakCommand()
    {
        var command = new Command("corpospeak", "Rewrite text for enterprise audiences with optional idiolect adaptation");

        var sourceOption = new Option<string>("--source") { Description = "The source text to rewrite (or path to file containing the text)", Required = true };
        var audienceOption = new Option<string>("--audience") { Description = "Target audience (csuite, engineering, product, sales, marketing, operations, finance, legal, hr, customer-success)", Required = true };
        var userMessagesOption = new Option<string[]>("--user-messages") { Description = "Optional user messages to learn idiolect from (text or file paths, multiple values allowed)", AllowMultipleArgumentsPerToken = true };
        var modelOption = CreateModelOption();
        var providerOption = CreateProviderOption();

        command.Add(sourceOption);
        command.Add(audienceOption);
        command.Add(userMessagesOption);
        command.Add(modelOption);
        command.Add(providerOption);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var source = parseResult.GetValue(sourceOption)!;
            var audience = parseResult.GetValue(audienceOption)!;
            var userMessages = parseResult.GetValue(userMessagesOption) ?? [];

            return RunLlmActionAsync(
                parseResult.GetValue(providerOption)!,
                parseResult.GetValue(modelOption),
                new LlmActionRequirements("corpospeak", TextChatOnly),
                async (services, _) =>
                {
                    var result = await services.GetRequiredService<CorpospeakComponent>()
                        .CorpospeakAsync(source, audience, userMessages);

                    AnsiConsole.WriteLine(result);
                });
        });

        return command;
    }

    internal static Command CreateGenerateImageCommand()
    {
        var command = new Command("generate-image", "Generate an image from a text prompt using an LLM provider");

        var promptOption = new Option<string>("--prompt") { Description = "The prompt describing the image to generate", Required = true };
        var outFileOption = new Option<string>("--out-file") { Description = "Path to write the generated image to", Required = true };
        var modelOption = CreateModelOption();
        var providerOption = CreateProviderOption();

        var overwriteOption = new Option<bool>("--overwrite") { Description = "Replace --out-file if it already exists" };

        command.Add(promptOption);
        command.Add(outFileOption);
        command.Add(modelOption);
        command.Add(providerOption);
        command.Add(overwriteOption);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var prompt = parseResult.GetValue(promptOption)!;
            var outFile = parseResult.GetValue(outFileOption)!;
            var overwrite = parseResult.GetValue(overwriteOption);

            return RunLlmActionAsync(
                parseResult.GetValue(providerOption)!,
                parseResult.GetValue(modelOption),
                new LlmActionRequirements("generate-image", new HashSet<LlmCapability> { LlmCapability.ImageGeneration }),
                async (services, _) =>
                {
                    var path = await services.GetRequiredService<GenerateImageComponent>()
                        .GenerateImageAsync(prompt, outFile, overwrite);

                    AnsiConsole.MarkupLine($"[green]Image written to {Markup.Escape(path)}[/]");
                });
        });

        return command;
    }

    private static Command CreateHistoryCommand()
    {
        var command = new Command("history", "Manage LLM command history");

        command.Add(CreateHistoryListCommand());
        command.Add(CreateHistoryDeleteCommand());

        return command;
    }

    private static Command CreateHistoryListCommand()
    {
        var command = new Command("list", "List all LLM command sessions");

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            try
            {
                using var historyService = new HistoryService();
                var historyComponent = new LlmHistoryComponent(historyService);
                var output = await historyComponent.ListAsync();
                AnsiConsole.WriteLine(output);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
                Environment.Exit(1);
            }
        });

        return command;
    }

    private static Command CreateHistoryDeleteCommand()
    {
        var command = new Command("delete", "Delete a session and all its messages");

        var sessionIdOption = new Option<long>("--session-id") { Description = "ID of the session to delete", Required = true };
        command.Add(sessionIdOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            try
            {
                var sessionId = parseResult.GetValue(sessionIdOption);

                using var historyService = new HistoryService();
                var historyComponent = new LlmHistoryComponent(historyService);
                await historyComponent.DeleteAsync(sessionId);

                AnsiConsole.MarkupLine($"[green]Session {Markup.Escape(sessionId.ToString())} deleted.[/]");
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
