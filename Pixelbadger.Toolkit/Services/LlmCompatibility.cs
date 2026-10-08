namespace Pixelbadger.Toolkit.Services;

public enum LlmCapability { TextChat, ImageInput, ImageGeneration }

/// <summary>Registered as a singleton by each provider module. Plain data; no API key access.</summary>
public sealed record LlmProviderCapabilities(
    string ProviderName,
    IReadOnlySet<LlmCapability> Capabilities,
    IReadOnlyList<string> SupportedReasoningEfforts);

/// <summary>Registered as a singleton by the provider module; null Model means the service uses its own default.</summary>
public sealed record LlmModelOptions(string? Model);

public sealed record LlmActionRequirements(
    string ActionName,
    IReadOnlySet<LlmCapability> RequiredCapabilities,
    string? ReasoningEffort = null);

public sealed record LlmCompatibilityResult(bool IsCompatible, string? Error, string? NormalisedReasoningEffort);

public interface ILlmCompatibilityValidator
{
    LlmCompatibilityResult Validate(LlmActionRequirements requirements);
}

public class LlmCompatibilityValidator : ILlmCompatibilityValidator
{
    private readonly LlmProviderCapabilities _provider;

    public LlmCompatibilityValidator(LlmProviderCapabilities provider)
    {
        _provider = provider;
    }

    public LlmCompatibilityResult Validate(LlmActionRequirements requirements)
    {
        var missing = requirements.RequiredCapabilities
            .Where(c => !_provider.Capabilities.Contains(c))
            .OrderBy(c => c)
            .Select(Describe)
            .ToList();

        if (missing.Count > 0)
        {
            return new LlmCompatibilityResult(
                false,
                $"The '{requirements.ActionName}' action requires {string.Join(" and ", missing)} which is not supported by the '{_provider.ProviderName}' provider.",
                null);
        }

        if (requirements.ReasoningEffort is null)
            return new LlmCompatibilityResult(true, null, null);

        var match = _provider.SupportedReasoningEfforts
            .FirstOrDefault(s => s.Equals(requirements.ReasoningEffort, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            return new LlmCompatibilityResult(
                false,
                $"Invalid reasoning effort '{requirements.ReasoningEffort}' for provider '{_provider.ProviderName}'. Supported values: {string.Join(", ", _provider.SupportedReasoningEfforts)}",
                null);
        }

        return new LlmCompatibilityResult(true, null, match);
    }

    private static string Describe(LlmCapability capability) => capability switch
    {
        LlmCapability.TextChat => "text chat",
        LlmCapability.ImageInput => "image input",
        LlmCapability.ImageGeneration => "image generation",
        _ => capability.ToString()
    };
}
