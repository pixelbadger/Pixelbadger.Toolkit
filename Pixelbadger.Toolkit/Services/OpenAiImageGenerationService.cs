using OpenAI;

namespace Pixelbadger.Toolkit.Services;

public class OpenAiImageGenerationService : IImageGenerationService
{
    public const string DefaultModel = "gpt-image-2.5";

    private readonly string _apiKey;
    private readonly string _model;

    public OpenAiImageGenerationService(string model = DefaultModel)
    {
        _apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? throw new InvalidOperationException("OPENAI_API_KEY environment variable is not set.");
        _model = model;
    }

    public async Task<byte[]> GenerateImageAsync(string prompt)
    {
        var imageClient = new OpenAIClient(_apiKey).GetImageClient(_model);
        var response = await imageClient.GenerateImageAsync(prompt);

        var bytes = response.Value.ImageBytes;
        if (bytes is null)
            throw new InvalidOperationException("Image generation returned no image data.");

        return bytes.ToArray();
    }
}
