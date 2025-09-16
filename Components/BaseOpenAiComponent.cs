using OpenAI;
using OpenAI.Chat;

namespace Pixelbadger.Toolkit.Components;

public abstract class BaseOpenAiComponent
{
    protected readonly ChatClient _chatClient;

    protected BaseOpenAiComponent(string model = "gpt-5-nano")
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException("OPENAI_API_KEY environment variable is not set.");
        }

        _chatClient = new OpenAIClient(apiKey)
            .GetChatClient(model);
    }

    protected static string EscapeXml(string input)
    {
        return input
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }
}