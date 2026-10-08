using Microsoft.Extensions.DependencyInjection;

namespace Pixelbadger.Toolkit.Services;

public static partial class LlmServiceCollectionExtensions
{
    public static IServiceCollection AddOpenAiLlmProvider(this IServiceCollection services, string? model)
    {
        services.AddSingleton(new LlmModelOptions(model));
        services.AddSingleton(new LlmProviderCapabilities(
            LlmProviders.OpenAi,
            new HashSet<LlmCapability> { LlmCapability.TextChat, LlmCapability.ImageInput, LlmCapability.ImageGeneration },
            ["low", "medium", "high"]));
        services.AddSingleton<ILlmClientService, OpenAiLlmClientService>();
        services.AddSingleton<IImageGenerationService, OpenAiImageGenerationService>();
        return services;
    }
}
