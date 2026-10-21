using BenchmarkDotNet.Attributes;
using Pixelbadger.Toolkit.Components;
using Spectre.Console;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Pixelbadger.Toolkit.Benchmarks;

[MemoryDiagnoser]
public class ImageDisplayBenchmarks
{
    private string _tempDir = null!;
    private string _smallPath = null!;
    private string _largePath = null!;
    private ImageDisplayComponent _small = null!;
    private ImageDisplayComponent _large = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "pbtk-benchmarks-image-display");
        Directory.CreateDirectory(_tempDir);

        _smallPath = Path.Combine(_tempDir, "gradient-64.png");
        _largePath = Path.Combine(_tempDir, "gradient-512.png");

        await WriteGradientAsync(64, _smallPath);
        await WriteGradientAsync(512, _largePath);

        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(TextWriter.Null),
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.TrueColor,
            Interactive = InteractionSupport.No,
        });
        console.Profile.Width = 80;

        _small = new ImageDisplayComponent(console);
        _large = new ImageDisplayComponent(console);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Benchmark]
    public void Display_64x64() =>
        _small.Display(_smallPath);

    [Benchmark]
    public void Display_512x512() =>
        _large.Display(_largePath);

    // Deterministic RGB gradient: red by x, green by y, blue constant.
    private static async Task WriteGradientAsync(int size, string path)
    {
        using var image = new Image<Rgba32>(size, size);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                byte r = (byte)(x * 255 / (size - 1));
                byte g = (byte)(y * 255 / (size - 1));
                image[x, y] = new Rgba32(r, g, 128, 255);
            }
        }
        await image.SaveAsPngAsync(path);
    }
}
