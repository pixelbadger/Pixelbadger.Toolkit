using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;
using OpenAiChatMessage = OpenAI.Chat.ChatMessage;

namespace Pixelbadger.Toolkit.Services;

public class OpenAiLlmClientService : ILlmClientService
{
    public const string DefaultModel = "gpt-5-nano";

    private readonly string _apiKey;
    private readonly string _model;

    public OpenAiLlmClientService(LlmModelOptions options)
    {
        _apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? throw new InvalidOperationException("OPENAI_API_KEY environment variable is not set.");
        _model = options.Model ?? DefaultModel;
    }

#pragma warning disable OPENAI001
    public async Task<LlmChatResult> CompleteChatAsync(IEnumerable<AiChatMessage> messages, string? reasoningEffort = null)
    {
        var chatClient = new OpenAIClient(_apiKey).GetChatClient(_model);
        var openAiMessages = MapMessages(messages);

        var response = reasoningEffort is not null
            ? await chatClient.CompleteChatAsync(openAiMessages, new ChatCompletionOptions
            {
                ReasoningEffortLevel = new ChatReasoningEffortLevel(reasoningEffort)
            })
            : await chatClient.CompleteChatAsync(openAiMessages);

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

    internal static List<OpenAiChatMessage> MapMessages(IEnumerable<AiChatMessage> messages)
    {
        var result = new List<OpenAiChatMessage>();
        foreach (var message in messages)
        {
            var textOnly = message.Contents.Count > 0 && message.Contents.All(c => c is TextContent);
            if (message.Role == ChatRole.System)
            {
                result.Add(textOnly
                    ? OpenAiChatMessage.CreateSystemMessage(message.Text)
                    : OpenAiChatMessage.CreateSystemMessage(MapParts(message)));
            }
            else if (message.Role == ChatRole.Assistant)
            {
                result.Add(textOnly
                    ? OpenAiChatMessage.CreateAssistantMessage(message.Text)
                    : OpenAiChatMessage.CreateAssistantMessage(MapParts(message)));
            }
            else if (message.Role == ChatRole.User)
            {
                result.Add(textOnly
                    ? OpenAiChatMessage.CreateUserMessage(message.Text)
                    : OpenAiChatMessage.CreateUserMessage(MapParts(message)));
            }
            else
            {
                throw new NotSupportedException($"Chat role '{message.Role}' is not supported by the OpenAI provider.");
            }
        }
        return result;
    }

    private static List<ChatMessageContentPart> MapParts(AiChatMessage message)
    {
        var parts = new List<ChatMessageContentPart>();
        foreach (var content in message.Contents)
        {
            switch (content)
            {
                case TextContent text:
                    parts.Add(ChatMessageContentPart.CreateTextPart(text.Text));
                    break;
                case DataContent data when data.MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase):
                    parts.Add(ChatMessageContentPart.CreateImagePart(BinaryData.FromBytes(data.Data), data.MediaType));
                    break;
                case DataContent data:
                    throw new NotSupportedException($"Data content with media type '{data.MediaType}' is not supported by the OpenAI provider; only image/* is supported.");
                default:
                    throw new NotSupportedException($"Message content of type '{content.GetType().Name}' is not supported by the OpenAI provider.");
            }
        }
        return parts;
    }
}
