using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Components;

public class GenerateImageComponent
{
    private readonly IImageGenerationService _imageGenerationService;

    public GenerateImageComponent(IImageGenerationService imageGenerationService)
    {
        _imageGenerationService = imageGenerationService;
    }

    public async Task<string> GenerateImageAsync(string prompt, string outFile)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            throw new ArgumentException("Prompt must not be empty.", nameof(prompt));
        if (string.IsNullOrWhiteSpace(outFile))
            throw new ArgumentException("Output file path must not be empty.", nameof(outFile));

        var resolvedPath = Path.GetFullPath(outFile);
        var imageBytes = await _imageGenerationService.GenerateImageAsync(prompt);

        var directory = Path.GetDirectoryName(resolvedPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllBytesAsync(resolvedPath, imageBytes);
        return resolvedPath;
    }
}
