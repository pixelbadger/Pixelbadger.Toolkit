using Anthropic;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Pixelbadger.Toolkit.Services;

public static partial class LlmServiceCollectionExtensions
{
    public static IServiceCollection AddClaudeLlmProvider(this IServiceCollection services, string? model)
    {
        services.AddSingleton(new LlmModelOptions(model));
        services.AddSingleton(new LlmProviderCapabilities(
            LlmProviders.Claude,
            new HashSet<LlmCapability> { LlmCapability.TextChat, LlmCapability.ImageInput, LlmCapability.ToolCalling },
            ["low", "medium", "high", "xhigh", "max"]));
        services.AddSingleton<ILlmClientService, ClaudeLlmClientService>();
        services.AddSingleton<IChatClient>(sp =>
        {
            var key = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
            if (string.IsNullOrEmpty(key))
                throw new InvalidOperationException("ANTHROPIC_API_KEY environment variable is not set.");
            var modelName = sp.GetRequiredService<LlmModelOptions>().Model ?? ClaudeLlmClientService.DefaultModel;
            return new AnthropicClient { ApiKey = key }.AsIChatClient(modelName, 16000);
        });
        return services;
    }
}
