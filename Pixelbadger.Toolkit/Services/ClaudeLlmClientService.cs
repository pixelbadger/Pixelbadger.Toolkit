using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.AI;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Pixelbadger.Toolkit.Services;

public class ClaudeLlmClientService : ILlmClientService
{
    public const string DefaultModel = "claude-sonnet-5-5";
    private const int MaxOutputTokens = 16000;

    private readonly string _apiKey;
    private readonly string _model;

    public ClaudeLlmClientService(LlmModelOptions options)
    {
        var key = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (string.IsNullOrEmpty(key))
            throw new InvalidOperationException("ANTHROPIC_API_KEY environment variable is not set.");
        _apiKey = key;
        _model = options.Model ?? DefaultModel;
    }

    public async Task<LlmChatResult> CompleteChatAsync(IEnumerable<AiChatMessage> messages, string? reasoningEffort = null)
    {
        var client = new AnthropicClient { ApiKey = _apiKey };
        var request = BuildRequest(_model, messages, reasoningEffort);
        var response = await client.Messages.Create(request);
        return ExtractResult(response);
    }

    internal static MessageCreateParams BuildRequest(string model, IEnumerable<AiChatMessage> messages, string? reasoningEffort)
    {
        var (system, mapped) = MapMessages(messages);
        var request = new MessageCreateParams
        {
            Model = model,
            MaxTokens = MaxOutputTokens,
            Messages = mapped
        };
        if (system is not null)
            request = request with { System = system };
        if (MapEffort(reasoningEffort) is { } effort)
            request = request with { OutputConfig = new OutputConfig { Effort = effort } };
        return request;
    }

    internal static Effort? MapEffort(string? reasoningEffort) => reasoningEffort?.ToLowerInvariant() switch
    {
        null => null,
        "low" => Effort.Low,
        "medium" => Effort.Medium,
        "high" => Effort.High,
        "xhigh" => Effort.Xhigh,
        "max" => Effort.Max,
        _ => throw new ArgumentException($"Unsupported reasoning effort '{reasoningEffort}' for the Claude provider.", nameof(reasoningEffort))
    };

    internal static LlmChatResult ExtractResult(Message response)
    {
        if (response.StopReason == "refusal")
        {
            string? category = response.StopDetails?.Category?.Raw();
            throw new InvalidOperationException(string.IsNullOrEmpty(category)
                ? "Claude declined the request."
                : $"Claude declined the request (category: {category}).");
        }

        var text = string.Concat(response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));
        if (text.Length == 0)
            throw new InvalidOperationException("LLM returned an empty response.");

        return new LlmChatResult(
            text,
            checked((int)response.Usage.InputTokens),
            checked((int)response.Usage.OutputTokens));
    }

    internal static (string? System, List<MessageParam> Messages) MapMessages(IEnumerable<AiChatMessage> messages)
    {
        var systemParts = new List<string>();
        var result = new List<MessageParam>();
        foreach (var message in messages)
        {
            if (message.Role == ChatRole.System)
            {
                foreach (var content in message.Contents)
                {
                    if (content is not TextContent text)
                        throw new NotSupportedException($"System message content of type '{content.GetType().Name}' is not supported by the Claude provider; only text is supported.");
                }
                systemParts.Add(message.Text);
            }
            else if (message.Role == ChatRole.User || message.Role == ChatRole.Assistant)
            {
                result.Add(new MessageParam
                {
                    Role = message.Role == ChatRole.User ? Role.User : Role.Assistant,
                    Content = MapBlocks(message)
                });
            }
            else
            {
                throw new NotSupportedException($"Chat role '{message.Role}' is not supported by the Claude provider.");
            }
        }
        return (systemParts.Count > 0 ? string.Join("\n\n", systemParts) : null, result);
    }

    private static List<ContentBlockParam> MapBlocks(AiChatMessage message)
    {
        var blocks = new List<ContentBlockParam>();
        foreach (var content in message.Contents)
        {
            switch (content)
            {
                case TextContent text:
                    blocks.Add(new TextBlockParam { Text = text.Text });
                    break;
                case DataContent data:
                    var mediaType = data.MediaType?.ToLowerInvariant();
                    var apiMediaType = mediaType switch
                    {
                        "image/jpeg" => (MediaType?)MediaType.ImageJpeg,
                        "image/png" => MediaType.ImagePng,
                        "image/gif" => MediaType.ImageGif,
                        "image/webp" => MediaType.ImageWebP,
                        _ => null
                    };
                    if (apiMediaType is null)
                        throw new NotSupportedException($"Data content with media type '{data.MediaType}' is not supported by the Claude provider; supported types: image/jpeg, image/png, image/gif, image/webp.");
                    blocks.Add(new ImageBlockParam
                    {
                        Source = new Base64ImageSource { Data = Convert.ToBase64String(data.Data.Span), MediaType = apiMediaType.Value }
                    });
                    break;
                default:
                    throw new NotSupportedException($"Message content of type '{content.GetType().Name}' is not supported by the Claude provider.");
            }
        }
        return blocks;
    }
}
