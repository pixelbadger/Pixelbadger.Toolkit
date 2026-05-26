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

        return command;
    }

    private static Command CreateChatCommand()
    {
        var command = new Command("chat", "Chat with OpenAI maintaining conversation history");

        var messageOption = new Option<string>("--message") { Description = "The message to send to the LLM", Required = true };
        var chatHistoryOption = new Option<string?>("--chat-history") { Description = "Path to JSON file containing chat history (will be created if it doesn't exist)" };
        var modelOption = new Option<string>("--model") { Description = "The OpenAI model to use", DefaultValueFactory = _ => "gpt-5-nano" };

        command.Add(messageOption);
        command.Add(chatHistoryOption);
        command.Add(modelOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            try
            {
                var message = parseResult.GetValue(messageOption)!;
                var chatHistory = parseResult.GetValue(chatHistoryOption);
                var model = parseResult.GetValue(modelOption)!;

                var openAiClientService = new OpenAiClientService(model);
                var chatComponent = new ChatComponent(openAiClientService);
                var response = await chatComponent.ChatAsync(message, chatHistory);

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
                var translateComponent = new TranslateComponent(openAiClientService);
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
                var ocaaarComponent = new OcaaarComponent(openAiClientService);
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
                var corpospeakComponent = new CorpospeakComponent(openAiClientService);
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
}
