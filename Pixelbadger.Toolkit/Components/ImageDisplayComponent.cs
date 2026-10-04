using Spectre.Console;
using Spectre.Console.Rendering;

namespace Pixelbadger.Toolkit.Components;

public class ImageDisplayComponent
{
    private readonly IAnsiConsole _console;

    public ImageDisplayComponent(IAnsiConsole console)
    {
        _console = console;
    }

    public void Display(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
            throw new ArgumentException("Image path must be provided", nameof(imagePath));

        if (!File.Exists(imagePath))
            throw new FileNotFoundException($"Image file not found: {imagePath}", imagePath);

        IRenderable image;
        try
        {
            image = new CanvasImage(imagePath) { MaxWidth = _console.Profile.Width };
        }
        catch (SixLabors.ImageSharp.UnknownImageFormatException)
        {
            throw new InvalidOperationException($"Unsupported image format: {imagePath}");
        }
        catch (SixLabors.ImageSharp.InvalidImageContentException)
        {
            throw new InvalidOperationException($"Invalid image content: {imagePath}");
        }

        _console.Write(image);
    }
}
