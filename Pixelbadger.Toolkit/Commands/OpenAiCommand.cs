using System.CommandLine;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Commands;

public static class OpenAiCommand
{
    public static Command Create()
    {
        var command = new Command("openai", "OpenAI utilities");

        command.Add(CreateChatCommand());
        command.Add(CreateTranslateCommand());
        command.Add(CreateOcaaarCommand());
        command.Add(CreateCorpospeakCommand());
        command.Add(CreateHistoryCommand());

        return command;
    }

    private static Command CreateChatCommand()
    {
        var command = new Command("chat", "Chat with OpenAI maintaining conversation history");

        var messageOption = new Option<string>("--message") { Description = "The message to send to the LLM", Required = true };
        var sessionIdOption = new Option<long?>("--session-id") { Description = "Session ID to continue a previous conversation (omit to start a new session)" };
        var modelOption = new Option<string>("--model") { Description = "The OpenAI model to use", DefaultValueFactory = _ => "gpt-5-nano" };

        command.Add(messageOption);
        command.Add(sessionIdOption);
        command.Add(modelOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            try
            {
                var message = parseResult.GetValue(messageOption)!;
                var sessionId = parseResult.GetValue(sessionIdOption);
                var model = parseResult.GetValue(modelOption)!;

                var openAiClientService = new OpenAiClientService(model);
                using var historyService = new HistoryService();
                var chatComponent = new ChatComponent(openAiClientService, historyService);
                var result = await chatComponent.ChatAsync(message, sessionId);

                Console.WriteLine(result.Response);
                Console.Error.WriteLine($"Session: {result.SessionId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        });

        return command;
    }

    private static Command CreateTranslateCommand()
    {
        var command = new Command("translate", "Translate text to a target language using OpenAI");

        var textOption = new Option<string>("--text") { Description = "The text to translate", Required = true };
        var targetLanguageOption = new Option<string>("--target-language") { Description = "The target language to translate to", Required = true };
        var modelOption = new Option<string>("--model") { Description = "The OpenAI model to use", DefaultValueFactory = _ => "gpt-5-nano" };

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

                var openAiClientService = new OpenAiClientService(model);
                using var historyService = new HistoryService();
                var translateComponent = new TranslateComponent(openAiClientService, historyService);
                var translation = await translateComponent.TranslateAsync(text, targetLanguage);

                Console.WriteLine(translation);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        });

        return command;
    }

    private static Command CreateOcaaarCommand()
    {
        var command = new Command("ocaaar", "Extract text from an image and translate it to pirate speak");

        var imagePathOption = new Option<string>("--image-path") { Description = "Path to the image file to process", Required = true };
        var modelOption = new Option<string>("--model") { Description = "The OpenAI model to use", DefaultValueFactory = _ => "gpt-5-nano" };

        command.Add(imagePathOption);
        command.Add(modelOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            try
            {
                var imagePath = parseResult.GetValue(imagePathOption)!;
                var model = parseResult.GetValue(modelOption)!;

                var openAiClientService = new OpenAiClientService(model);
                using var historyService = new HistoryService();
                var ocaaarComponent = new OcaaarComponent(openAiClientService, historyService);
                var response = await ocaaarComponent.OcaaarAsync(imagePath);

                Console.WriteLine(response);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
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
        var modelOption = new Option<string>("--model") { Description = "The OpenAI model to use", DefaultValueFactory = _ => "gpt-5-nano" };

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

                var openAiClientService = new OpenAiClientService(model);
                using var historyService = new HistoryService();
                var corpospeakComponent = new CorpospeakComponent(openAiClientService, historyService);
                var result = await corpospeakComponent.CorpospeakAsync(source, audience, userMessages);

                Console.WriteLine(result);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        });

        return command;
    }

    private static Command CreateHistoryCommand()
    {
        var command = new Command("history", "Manage OpenAI command history");

        command.Add(CreateHistoryListCommand());
        command.Add(CreateHistoryDeleteCommand());

        return command;
    }

    private static Command CreateHistoryListCommand()
    {
        var command = new Command("list", "List all OpenAI command sessions");

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            try
            {
                using var historyService = new HistoryService();
                var historyComponent = new OpenAiHistoryComponent(historyService);
                var output = await historyComponent.ListAsync();
                Console.WriteLine(output);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
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
                var historyComponent = new OpenAiHistoryComponent(historyService);
                await historyComponent.DeleteAsync(sessionId);

                Console.WriteLine($"Session {sessionId} deleted.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        });

        return command;
    }
}
