using OpenAI;
using OpenAI.Chat;
using System.Text.Json;

namespace Pixelbadger.Toolkit.Components;

public class LlmComponent
{
    private readonly ChatClient _chatClient;

    public LlmComponent(string model = "gpt-5-nano")
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException("OPENAI_API_KEY environment variable is not set.");
        }

        _chatClient = new OpenAIClient(apiKey)
            .GetChatClient(model);
    }

    public async Task<string> ChatAsync(string question, string? chatHistoryPath)
    {
        var messages = new List<ChatMessage>();

        // Load existing chat history if provided
        if (!string.IsNullOrEmpty(chatHistoryPath) && File.Exists(chatHistoryPath))
        {
            var historyJson = await File.ReadAllTextAsync(chatHistoryPath);
            var historyMessages = JsonSerializer.Deserialize<List<ChatHistoryMessage>>(historyJson) ?? [];

            foreach (var historyMessage in historyMessages)
            {
                messages.Add(historyMessage.Role == "user"
                    ? ChatMessage.CreateUserMessage(historyMessage.Content)
                    : ChatMessage.CreateAssistantMessage(historyMessage.Content));
            }
        }

        // Add the user's current question
        messages.Add(ChatMessage.CreateUserMessage(question));

        // Get response from OpenAI
        var response = await _chatClient.CompleteChatAsync(messages);
        var assistantMessage = response.Value.Content[0].Text;

        // Save updated conversation history
        if (!string.IsNullOrEmpty(chatHistoryPath))
        {
            // Create a simple list to save: existing messages + new user message + assistant response
            var allMessages = new List<ChatHistoryMessage>();

            // Load existing history again to preserve it
            if (File.Exists(chatHistoryPath))
            {
                var existingJson = await File.ReadAllTextAsync(chatHistoryPath);
                var existingMessages = JsonSerializer.Deserialize<List<ChatHistoryMessage>>(existingJson) ?? [];
                allMessages.AddRange(existingMessages);
            }

            // Add the new user question
            allMessages.Add(new ChatHistoryMessage { Role = "user", Content = question });

            // Add the assistant response
            allMessages.Add(new ChatHistoryMessage { Role = "assistant", Content = assistantMessage });

            await SaveChatHistoryAsync(chatHistoryPath, allMessages);
        }

        return assistantMessage;
    }

    private async Task SaveChatHistoryAsync(string chatHistoryPath, List<ChatHistoryMessage> messages)
    {
        // Ensure directory exists
        var directory = Path.GetDirectoryName(chatHistoryPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        var historyJson = JsonSerializer.Serialize(messages, options);
        await File.WriteAllTextAsync(chatHistoryPath, historyJson);
    }

    public class ChatHistoryMessage
    {
        public string Role { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }
}