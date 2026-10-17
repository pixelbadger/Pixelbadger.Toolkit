using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;

namespace Pixelbadger.Toolkit.Services;

public static partial class LlmServiceCollectionExtensions
{
    public static IServiceCollection AddOpenAiLlmProvider(this IServiceCollection services, string? model)
    {
        services.AddSingleton(new LlmModelOptions(model));
        services.AddSingleton(new LlmProviderCapabilities(
            LlmProviders.OpenAi,
            new HashSet<LlmCapability> { LlmCapability.TextChat, LlmCapability.ImageInput, LlmCapability.ImageGeneration, LlmCapability.ToolCalling },
            ["low", "medium", "high"]));
        services.AddSingleton<ILlmClientService, OpenAiLlmClientService>();
        services.AddSingleton<IImageGenerationService, OpenAiImageGenerationService>();
        services.AddSingleton<IChatReasoningConfigurator, OpenAiChatReasoningConfigurator>();
        services.AddSingleton<IChatClient>(sp =>
        {
            var key = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                ?? throw new InvalidOperationException("OPENAI_API_KEY environment variable is not set.");
            var modelName = sp.GetRequiredService<LlmModelOptions>().Model ?? OpenAiLlmClientService.DefaultModel;
#pragma warning disable OPENAI001
            return new OpenAIClient(key).GetResponsesClient().AsIChatClient(modelName);
#pragma warning restore OPENAI001
        });
        return services;
    }
}
