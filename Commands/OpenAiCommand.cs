using System.CommandLine;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Commands;

public static class OpenAiCommand
{
    public static Command Create()
    {
        var command = new Command("openai", "OpenAI utilities");

        command.AddCommand(CreateChatCommand());
        command.AddCommand(CreateTranslateCommand());
        command.AddCommand(CreateOcaaarCommand());
        command.AddCommand(CreateCorpospeakCommand());

        return command;
    }

    private static Command CreateChatCommand()
    {
        var command = new Command("chat", "Chat with OpenAI maintaining conversation history");

        var messageOption = new Option<string>(
            aliases: ["--message"],
            description: "The message to send to the LLM")
        {
            IsRequired = true
        };

        var chatHistoryOption = new Option<string?>(
            aliases: ["--chat-history"],
            description: "Path to JSON file containing chat history (will be created if it doesn't exist)")
        {
            IsRequired = false
        };

        var modelOption = new Option<string>(
            aliases: ["--model"],
            description: "The OpenAI model to use",
            getDefaultValue: () => "gpt-5-nano")
        {
            IsRequired = false
        };

        command.AddOption(messageOption);
        command.AddOption(chatHistoryOption);
        command.AddOption(modelOption);

        command.SetHandler(async (string message, string? chatHistory, string model) =>
        {
            try
            {
                var openAiClientService = new OpenAiClientService();
                var chatComponent = new ChatComponent(openAiClientService);
                var response = await chatComponent.ChatAsync(message, chatHistory, model);

                Console.WriteLine(response);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        }, messageOption, chatHistoryOption, modelOption);

        return command;
    }

    private static Command CreateTranslateCommand()
    {
        var command = new Command("translate", "Translate text to a target language using OpenAI");

        var textOption = new Option<string>(
            aliases: ["--text"],
            description: "The text to translate")
        {
            IsRequired = true
        };

        var targetLanguageOption = new Option<string>(
            aliases: ["--target-language"],
            description: "The target language to translate to")
        {
            IsRequired = true
        };

        var modelOption = new Option<string>(
            aliases: ["--model"],
            description: "The OpenAI model to use",
            getDefaultValue: () => "gpt-5-nano")
        {
            IsRequired = false
        };

        command.AddOption(textOption);
        command.AddOption(targetLanguageOption);
        command.AddOption(modelOption);

        command.SetHandler(async (string text, string targetLanguage, string model) =>
        {
            try
            {
                var openAiClientService = new OpenAiClientService();
                var translateComponent = new TranslateComponent(openAiClientService);
                var translation = await translateComponent.TranslateAsync(text, targetLanguage, model);

                Console.WriteLine(translation);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        }, textOption, targetLanguageOption, modelOption);

        return command;
    }

    private static Command CreateOcaaarCommand()
    {
        var command = new Command("ocaaar", "Extract text from an image and translate it to pirate speak");

        var imagePathOption = new Option<string>(
            aliases: ["--image-path"],
            description: "Path to the image file to process")
        {
            IsRequired = true
        };

        var modelOption = new Option<string>(
            aliases: ["--model"],
            description: "The OpenAI model to use",
            getDefaultValue: () => "gpt-5-nano")
        {
            IsRequired = false
        };

        command.AddOption(imagePathOption);
        command.AddOption(modelOption);

        command.SetHandler(async (string imagePath, string model) =>
        {
            try
            {
                var openAiClientService = new OpenAiClientService();
                var ocaaarComponent = new OcaaarComponent(openAiClientService);
                var response = await ocaaarComponent.OcaaarAsync(imagePath, model);

                Console.WriteLine(response);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        }, imagePathOption, modelOption);

        return command;
    }

    private static Command CreateCorpospeakCommand()
    {
        var command = new Command("corpospeak", "Rewrite text for enterprise audiences with optional idiolect adaptation");

        var sourceOption = new Option<string>(
            aliases: ["--source"],
            description: "The source text to rewrite")
        {
            IsRequired = true
        };

        var audienceOption = new Option<string>(
            aliases: ["--audience"],
            description: "Target audience (csuite, engineering, product, sales, marketing, operations, finance, legal, hr, customer-success)")
        {
            IsRequired = true
        };

        var userMessagesOption = new Option<string[]>(
            aliases: ["--user-messages"],
            description: "Optional user messages to learn idiolect from (multiple values allowed)")
        {
            IsRequired = false,
            AllowMultipleArgumentsPerToken = true
        };

        var modelOption = new Option<string>(
            aliases: ["--model"],
            description: "The OpenAI model to use",
            getDefaultValue: () => "gpt-5-nano")
        {
            IsRequired = false
        };

        command.AddOption(sourceOption);
        command.AddOption(audienceOption);
        command.AddOption(userMessagesOption);
        command.AddOption(modelOption);

        command.SetHandler(async (string source, string audience, string[] userMessages, string model) =>
        {
            try
            {
                var openAiClientService = new OpenAiClientService();
                var corpospeakComponent = new CorpospeakComponent(openAiClientService);
                var result = await corpospeakComponent.CorpospeakAsync(source, audience, userMessages ?? [], model);

                Console.WriteLine(result);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        }, sourceOption, audienceOption, userMessagesOption, modelOption);

        return command;
    }
}