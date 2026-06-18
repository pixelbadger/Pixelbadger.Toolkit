using System.CommandLine;
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
        command.Add(CreateHistoryCommand());

        return command;
    }

    internal static bool TryValidateReasoningEffort(
        string? effort,
        IReadOnlyList<string> supported,
        out string? normalised)
    {
        if (effort is null)
        {
            normalised = null;
            return true;
        }

        var match = supported.FirstOrDefault(s => s.Equals(effort, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            normalised = null;
            return false;
        }

        normalised = match;
        return true;
    }

    private static Command CreateChatCommand()
    {
        var command = new Command("chat", "Chat with an LLM maintaining conversation history");

        var messageOption = new Option<string>("--message") { Description = "The message to send to the LLM", Required = true };
        var sessionIdOption = new Option<long?>("--session-id") { Description = "Session ID to continue a previous conversation (omit to start a new session)" };
        var modelOption = new Option<string>("--model") { Description = "The model to use", DefaultValueFactory = _ => "gpt-5-nano" };
        var reasoningEffortOption = new Option<string?>("--reasoning-effort") { Description = "Reasoning effort level (e.g. low, medium, high for OpenAI o-series models)" };

        command.Add(messageOption);
        command.Add(sessionIdOption);
        command.Add(modelOption);
        command.Add(reasoningEffortOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            try
            {
                var message = parseResult.GetValue(messageOption)!;
                var sessionId = parseResult.GetValue(sessionIdOption);
                var model = parseResult.GetValue(modelOption)!;
                var reasoningEffort = parseResult.GetValue(reasoningEffortOption);

                var llmClientService = new OpenAiLlmClientService(model);

                if (!TryValidateReasoningEffort(reasoningEffort, llmClientService.SupportedReasoningEfforts, out var normalisedEffort))
                {
                    Console.WriteLine($"Error: Invalid reasoning effort '{reasoningEffort}'. Supported values: {string.Join(", ", llmClientService.SupportedReasoningEfforts)}");
                    Environment.Exit(1);
                    return;
                }

                using var historyService = new HistoryService();
                var chatComponent = new ChatComponent(llmClientService, historyService);
                var result = await chatComponent.ChatAsync(message, sessionId, normalisedEffort);

                AnsiConsole.WriteLine(result.Response);
                Console.Error.WriteLine($"Session: {result.SessionId}");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
                Environment.Exit(1);
            }
        });

        return command;
    }

    private static Command CreateTranslateCommand()
    {
        var command = new Command("translate", "Translate text to a target language using an LLM");

        var textOption = new Option<string>("--text") { Description = "The text to translate", Required = true };
        var targetLanguageOption = new Option<string>("--target-language") { Description = "The target language to translate to", Required = true };
        var modelOption = new Option<string>("--model") { Description = "The model to use", DefaultValueFactory = _ => "gpt-5-nano" };

        command.Add(textOption);
        command.Add(targetLanguageOption);
        command.Add(modelOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            try
            {
                var text = parseResult.GetValue(textOption)!;
                var targetLanguage = parseResult.GetValue(targetLanguageOption)!;
                var model = parseResult.GetValue(modelOption)!;

                var llmClientService = new OpenAiLlmClientService(model);
                using var historyService = new HistoryService();
                var translateComponent = new TranslateComponent(llmClientService, historyService);
                var translation = await translateComponent.TranslateAsync(text, targetLanguage);

                AnsiConsole.WriteLine(translation);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
                Environment.Exit(1);
            }
        });

        return command;
    }

    private static Command CreateOcaaarCommand()
    {
        var command = new Command("ocaaar", "Extract text from an image and translate it to pirate speak");

        var imagePathOption = new Option<string>("--image-path") { Description = "Path to the image file to process", Required = true };
        var modelOption = new Option<string>("--model") { Description = "The model to use", DefaultValueFactory = _ => "gpt-5-nano" };

        command.Add(imagePathOption);
        command.Add(modelOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            try
            {
                var imagePath = parseResult.GetValue(imagePathOption)!;
                var model = parseResult.GetValue(modelOption)!;

                var llmClientService = new OpenAiLlmClientService(model);
                using var historyService = new HistoryService();
                var ocaaarComponent = new OcaaarComponent(llmClientService, historyService);
                var response = await ocaaarComponent.OcaaarAsync(imagePath);

                AnsiConsole.WriteLine(response);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
                Environment.Exit(1);
            }
        });

        return command;
    }

    private static Command CreateCorpospeakCommand()
    {
        var command = new Command("corpospeak", "Rewrite text for enterprise audiences with optional idiolect adaptation");

        var sourceOption = new Option<string>("--source") { Description = "The source text to rewrite (or path to file containing the text)", Required = true };
        var audienceOption = new Option<string>("--audience") { Description = "Target audience (csuite, engineering, product, sales, marketing, operations, finance, legal, hr, customer-success)", Required = true };
        var userMessagesOption = new Option<string[]>("--user-messages") { Description = "Optional user messages to learn idiolect from (text or file paths, multiple values allowed)", AllowMultipleArgumentsPerToken = true };
        var modelOption = new Option<string>("--model") { Description = "The model to use", DefaultValueFactory = _ => "gpt-5-nano" };

        command.Add(sourceOption);
        command.Add(audienceOption);
        command.Add(userMessagesOption);
        command.Add(modelOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            try
            {
                var source = parseResult.GetValue(sourceOption)!;
                var audience = parseResult.GetValue(audienceOption)!;
                var userMessages = parseResult.GetValue(userMessagesOption) ?? [];
                var model = parseResult.GetValue(modelOption)!;

                var llmClientService = new OpenAiLlmClientService(model);
                using var historyService = new HistoryService();
                var corpospeakComponent = new CorpospeakComponent(llmClientService, historyService);
                var result = await corpospeakComponent.CorpospeakAsync(source, audience, userMessages);

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

                AnsiConsole.MarkupLine($"[green]Session {sessionId} deleted.[/]");
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
