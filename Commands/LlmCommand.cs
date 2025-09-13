using System.CommandLine;
using Pixelbadger.Toolkit.Components;

namespace Pixelbadger.Toolkit.Commands;

public static class LlmCommand
{
    public static Command Create()
    {
        var command = new Command("llm", "Large Language Model utilities");

        command.AddCommand(CreateOpenAiCommand());

        return command;
    }

    private static Command CreateOpenAiCommand()
    {
        var command = new Command("openai", "Chat with OpenAI maintaining conversation history");

        var questionOption = new Option<string>(
            aliases: ["--question"],
            description: "The question to ask the LLM")
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

        command.AddOption(questionOption);
        command.AddOption(chatHistoryOption);
        command.AddOption(modelOption);

        command.SetHandler(async (string question, string? chatHistory, string model) =>
        {
            try
            {
                var llmComponent = new LlmComponent(model);
                var response = await llmComponent.ChatAsync(question, chatHistory);

                Console.WriteLine(response);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        }, questionOption, chatHistoryOption, modelOption);

        return command;
    }
}