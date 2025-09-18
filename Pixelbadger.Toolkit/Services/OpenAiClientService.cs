using OpenAI;
using OpenAI.Chat;

namespace Pixelbadger.Toolkit.Services;

public interface IOpenAiClientService
{
    Task<string> CompleteChatAsync(IEnumerable<ChatMessage> messages);
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

    public async Task<string> CompleteChatAsync(IEnumerable<ChatMessage> messages)
    {
        var chatClient = new OpenAIClient(_apiKey).GetChatClient(_model);
        var response = await chatClient.CompleteChatAsync(messages);
        return response.Value.Content[0].Text;
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