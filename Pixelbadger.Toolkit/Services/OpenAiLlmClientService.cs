using OpenAI;
using OpenAI.Chat;

namespace Pixelbadger.Toolkit.Services;

public class OpenAiLlmClientService : ILlmClientService
{
    private static readonly IReadOnlyList<string> _supportedReasoningEfforts = ["low", "medium", "high"];

    private readonly string _apiKey;
    private readonly string _model;

    public OpenAiLlmClientService(string model = "gpt-5-nano")
    {
        _apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? throw new InvalidOperationException("OPENAI_API_KEY environment variable is not set.");
        _model = model;
    }

    public IReadOnlyList<string> SupportedReasoningEfforts => _supportedReasoningEfforts;

#pragma warning disable OPENAI001
    public async Task<LlmChatResult> CompleteChatAsync(IEnumerable<ChatMessage> messages, string? reasoningEffort = null)
    {
        var chatClient = new OpenAIClient(_apiKey).GetChatClient(_model);

        var response = reasoningEffort is not null
            ? await chatClient.CompleteChatAsync(messages, new ChatCompletionOptions
            {
                ReasoningEffortLevel = new ChatReasoningEffortLevel(reasoningEffort)
            })
            : await chatClient.CompleteChatAsync(messages);

        var content = response.Value.Content;
        if (content.Count == 0)
            throw new InvalidOperationException("LLM returned an empty response.");

        var usage = response.Value.Usage;
        return new LlmChatResult(
            content[0].Text,
            usage.InputTokenCount,
            usage.OutputTokenCount);
    }
#pragma warning restore OPENAI001
}
