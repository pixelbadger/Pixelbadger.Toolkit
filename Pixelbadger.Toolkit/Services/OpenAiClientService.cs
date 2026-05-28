using OpenAI;
using OpenAI.Chat;

namespace Pixelbadger.Toolkit.Services;

public record ChatResult(string Content, int PromptTokens, int CompletionTokens);

public interface IOpenAiClientService
{
    Task<ChatResult> CompleteChatAsync(IEnumerable<ChatMessage> messages);
    string EscapeXml(string input);
}

public class OpenAiClientService : IOpenAiClientService
{
    private readonly string _apiKey;
    private readonly string _model;

    public OpenAiClientService(string model = "gpt-4o-mini")
    {
        _apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? throw new InvalidOperationException("OPENAI_API_KEY environment variable is not set.");
        _model = model;
    }

    public async Task<ChatResult> CompleteChatAsync(IEnumerable<ChatMessage> messages)
    {
        var chatClient = new OpenAIClient(_apiKey).GetChatClient(_model);
        var response = await chatClient.CompleteChatAsync(messages);
        var usage = response.Value.Usage;
        return new ChatResult(
            response.Value.Content[0].Text,
            usage.InputTokenCount,
            usage.OutputTokenCount);
    }

    public string EscapeXml(string input)
    {
        return input
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }
}