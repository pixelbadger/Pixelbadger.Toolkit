using OpenAI;
using OpenAI.Images;

namespace Pixelbadger.Toolkit.Services;

public class OpenAiImageGenerationService : IImageGenerationService
{
    public const string DefaultModel = "gpt-image-1";

    private readonly ImageClient _imageClient;

    public OpenAiImageGenerationService(LlmModelOptions options)
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? throw new InvalidOperationException("OPENAI_API_KEY environment variable is not set.");
        _imageClient = new OpenAIClient(apiKey).GetImageClient(options.Model ?? DefaultModel);
    }

    public async Task<byte[]> GenerateImageAsync(string prompt)
    {
        var response = await _imageClient.GenerateImageAsync(prompt);

        var bytes = response.Value.ImageBytes;
        if (bytes is null)
            throw new InvalidOperationException("Image generation returned no image data.");

        return bytes.ToArray();
    }
}
