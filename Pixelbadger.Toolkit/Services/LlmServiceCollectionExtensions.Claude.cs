using Microsoft.Extensions.DependencyInjection;

namespace Pixelbadger.Toolkit.Services;

public static partial class LlmServiceCollectionExtensions
{
    public static IServiceCollection AddClaudeLlmProvider(this IServiceCollection services, string? model)
    {
        services.AddSingleton(new LlmModelOptions(model));
        services.AddSingleton(new LlmProviderCapabilities(
            LlmProviders.Claude,
            new HashSet<LlmCapability> { LlmCapability.TextChat, LlmCapability.ImageInput },
            ["low", "medium", "high", "xhigh", "max"]));
        services.AddSingleton<ILlmClientService, ClaudeLlmClientService>();
        return services;
    }
}
