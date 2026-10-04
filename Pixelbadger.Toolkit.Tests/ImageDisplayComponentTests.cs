using FluentAssertions;
using Pixelbadger.Toolkit.Components;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Spectre.Console;

namespace Pixelbadger.Toolkit.Tests;

public class ImageDisplayComponentTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly StringWriter _output = new();
    private readonly IAnsiConsole _console;
    private readonly ImageDisplayComponent _component;

    public ImageDisplayComponentTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDirectory);
        _console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.TrueColor,
            Out = new AnsiConsoleOutput(_output)
        });
        _component = new ImageDisplayComponent(_console);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
            Directory.Delete(_testDirectory, true);
    }

    [Fact]
    public void Display_ShouldWriteOutput_WhenImageIsValid()
    {
        var path = Path.Combine(_testDirectory, "a.png");
        using (var image = new Image<Rgba32>(8, 8, new Rgba32(255, 0, 0, 255)))
            image.SaveAsPng(path);

        _component.Display(path);

        _output.ToString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Display_ShouldThrowFileNotFound_WhenFileMissing()
    {
        var act = () => _component.Display(Path.Combine(_testDirectory, "missing.png"));

        act.Should().Throw<FileNotFoundException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Display_ShouldThrowArgumentException_WhenPathEmpty(string path)
    {
        var act = () => _component.Display(path);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Display_ShouldThrowInvalidOperation_WhenFileIsNotAnImage()
    {
        var path = Path.Combine(_testDirectory, "bad.png");
        File.WriteAllText(path, "not an image");

        var act = () => _component.Display(path);

        act.Should().Throw<InvalidOperationException>();
    }
}
