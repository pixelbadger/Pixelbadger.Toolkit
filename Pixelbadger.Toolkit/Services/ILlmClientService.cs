using OpenAI.Chat;

namespace Pixelbadger.Toolkit.Services;

public record LlmChatResult(string Content, int PromptTokens, int CompletionTokens);

// NOTE: CompleteChatAsync currently accepts OpenAI.Chat.ChatMessage because all
// components build OpenAI SDK message objects. When a second provider is added,
// introduce a provider-neutral message wrapper and update all components at that time.
public interface ILlmClientService
{
    IReadOnlyList<string> SupportedReasoningEfforts { get; }
    Task<LlmChatResult> CompleteChatAsync(IEnumerable<ChatMessage> messages, string? reasoningEffort = null);
}
