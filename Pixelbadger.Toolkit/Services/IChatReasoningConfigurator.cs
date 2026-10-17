using Anthropic.Models.Messages;
using Microsoft.Extensions.AI;

namespace Pixelbadger.Toolkit.Services;

/// <summary>Applies a provider-specific reasoning effort to the ChatOptions of an IChatClient request.</summary>
public interface IChatReasoningConfigurator
{
    void Apply(ChatOptions options, string reasoningEffort);
}

/// <summary>OpenAI Responses API: maps low/medium/high to ChatOptions.Reasoning.Effort.</summary>
public sealed class OpenAiChatReasoningConfigurator : IChatReasoningConfigurator
{
    public void Apply(ChatOptions options, string reasoningEffort)
    {
        var effort = reasoningEffort.ToLowerInvariant() switch
        {
            "low" => ReasoningEffort.Low,
            "medium" => ReasoningEffort.Medium,
            "high" => ReasoningEffort.High,
            _ => throw new ArgumentException($"Unsupported reasoning effort '{reasoningEffort}' for the OpenAI provider.", nameof(reasoningEffort))
        };
        options.Reasoning = new ReasoningOptions { Effort = effort };
    }
}

/// <summary>
/// Claude: sends the native effort in OutputConfig.Effort via RawRepresentationFactory, because the adapter's
/// ChatOptions.Reasoning mapping shifts the scale. The adapter does not fill Model/MaxTokens into a factory-provided
/// request, so they are supplied here.
/// </summary>
public sealed class ClaudeChatReasoningConfigurator : IChatReasoningConfigurator
{
    public const int DefaultMaxTokens = 16000;

    private readonly string _model;
    private readonly int _maxTokens;

    public ClaudeChatReasoningConfigurator(string? model = null, int maxTokens = DefaultMaxTokens)
    {
        _model = model ?? ClaudeLlmClientService.DefaultModel;
        _maxTokens = maxTokens;
    }

    public void Apply(ChatOptions options, string reasoningEffort)
    {
        var effort = ClaudeLlmClientService.MapEffort(reasoningEffort)!.Value;
        var model = _model;
        var maxTokens = _maxTokens;
        options.RawRepresentationFactory = _ => new MessageCreateParams
        {
            Model = model,
            MaxTokens = maxTokens,
            Messages = [],
            OutputConfig = new OutputConfig { Effort = effort }
        };
    }
}
