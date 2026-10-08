using Microsoft.Extensions.AI;

namespace Pixelbadger.Toolkit.Services;

public record LlmChatResult(string Content, int PromptTokens, int CompletionTokens);

public interface ILlmClientService
{
    Task<LlmChatResult> CompleteChatAsync(IEnumerable<ChatMessage> messages, string? reasoningEffort = null);
}
