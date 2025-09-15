using System.CommandLine;
using Pixelbadger.Toolkit.Components;

namespace Pixelbadger.Toolkit.Commands;

public static class LlmCommand
{
    public static Command Create()
    {
        var command = new Command("llm", "Large Language Model utilities");

        command.AddCommand(CreateOpenAiCommand());
        command.AddCommand(CreateTranslateCommand());
        command.AddCommand(CreateOcaaarCommand());

        return command;
    }

    private static Command CreateOpenAiCommand()
    {
        var command = new Command("openai", "Chat with OpenAI maintaining conversation history");

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
                var llmComponent = new LlmComponent(model);
                var response = await llmComponent.ChatAsync(message, chatHistory);

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
                var llmComponent = new LlmComponent(model);
                var translation = await llmComponent.TranslateAsync(text, targetLanguage);

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
                var llmComponent = new LlmComponent(model);
                var response = await llmComponent.OcaaarAsync(imagePath);

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
}