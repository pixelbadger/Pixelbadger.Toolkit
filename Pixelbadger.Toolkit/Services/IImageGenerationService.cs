namespace Pixelbadger.Toolkit.Services;

public interface IImageGenerationService
{
    Task<byte[]> GenerateImageAsync(string prompt);
}
