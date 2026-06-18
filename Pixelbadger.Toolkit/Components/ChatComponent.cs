using OpenAI.Chat;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Components;

public record ChatSessionResult(string Response, long SessionId);

public class ChatComponent
{
    private readonly ILlmClientService _llmClientService;
    private readonly IHistoryService _historyService;

    public ChatComponent(ILlmClientService llmClientService, IHistoryService historyService)
    {
        _llmClientService = llmClientService;
        _historyService = historyService;
    }

    public async Task<ChatSessionResult> ChatAsync(string question, long? sessionId, string? reasoningEffort = null)
    {
        var messages = new List<ChatMessage>();

        if (sessionId.HasValue)
        {
            var existingMessages = await _historyService.GetSessionMessagesAsync(sessionId.Value);
            foreach (var msg in existingMessages)
            {
                messages.Add(msg.Role switch
                {
                    "system" => ChatMessage.CreateSystemMessage(msg.Content),
                    "assistant" => ChatMessage.CreateAssistantMessage(msg.Content),
                    _ => ChatMessage.CreateUserMessage(msg.Content)
                });
            }
        }

        messages.Add(ChatMessage.CreateUserMessage(question));

        var result = await _llmClientService.CompleteChatAsync(messages, reasoningEffort);

        var activeSessionId = sessionId ?? await _historyService.CreateSessionAsync("chat");
        await _historyService.AddMessageAsync(activeSessionId, "user", question);
        await _historyService.AddMessageAsync(activeSessionId, "assistant", result.Content);
        await _historyService.UpdateTokenUsageAsync(activeSessionId, result.PromptTokens, result.CompletionTokens);

        return new ChatSessionResult(result.Content, activeSessionId);
    }
}
