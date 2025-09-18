using OpenAI;
using OpenAI.Chat;

namespace Pixelbadger.Toolkit.Services;

public interface IOpenAiClientService
{
    ChatClient GetChatClient(string model);
    string EscapeXml(string input);
}

public class OpenAiClientService : IOpenAiClientService
{
    private readonly string _apiKey;

    public OpenAiClientService()
    {
        _apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? throw new InvalidOperationException("OPENAI_API_KEY environment variable is not set.");
    }

    public ChatClient GetChatClient(string model)
    {
        return new OpenAIClient(_apiKey).GetChatClient(model);
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