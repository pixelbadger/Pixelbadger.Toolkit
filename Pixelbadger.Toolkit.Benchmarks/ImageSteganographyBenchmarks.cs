using BenchmarkDotNet.Attributes;
using Pixelbadger.Toolkit.Components;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Pixelbadger.Toolkit.Benchmarks;

[MemoryDiagnoser]
public class ImageSteganographyBenchmarks
{
    private ImageSteganography _steganography = null!;
    private string _smallImagePath = null!;
    private string _largeImagePath = null!;
    private string _encodedSmallPath = null!;
    private string _encodedLargePath = null!;
    private string _outputPath = null!;

    private const string ShortMessage = "Hello, World!";
    private const string LongMessage =
        "The quick brown fox jumps over the lazy dog. " +
        "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor " +
        "incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud " +
        "exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat. Duis aute irure " +
        "dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur.";

    [GlobalSetup]
    public async Task Setup()
    {
        _steganography = new ImageSteganography();

        var tempDir = Path.Combine(Path.GetTempPath(), "pbtk-benchmarks-steg");
        Directory.CreateDirectory(tempDir);

        _smallImagePath = Path.Combine(tempDir, "small.png");
        _largeImagePath = Path.Combine(tempDir, "large.png");
        _encodedSmallPath = Path.Combine(tempDir, "encoded-small.png");
        _encodedLargePath = Path.Combine(tempDir, "encoded-large.png");
        _outputPath = Path.Combine(tempDir, "output.png");

        using (var small = new Image<Rgba32>(200, 200))
            await small.SaveAsPngAsync(_smallImagePath);

        using (var large = new Image<Rgba32>(800, 800))
            await large.SaveAsPngAsync(_largeImagePath);

        await _steganography.EncodeMessageAsync(_smallImagePath, ShortMessage, _encodedSmallPath);
        await _steganography.EncodeMessageAsync(_largeImagePath, LongMessage, _encodedLargePath);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "pbtk-benchmarks-steg");
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, true);
    }

    [Benchmark]
    public async Task Encode_ShortMessage_SmallImage() =>
        await _steganography.EncodeMessageAsync(_smallImagePath, ShortMessage, _outputPath);

    [Benchmark]
    public async Task Encode_LongMessage_LargeImage() =>
        await _steganography.EncodeMessageAsync(_largeImagePath, LongMessage, _outputPath);

    // Decode scans the entire image regardless of message length — benchmark both sizes
    [Benchmark]
    public async Task<string> Decode_SmallImage() =>
        await _steganography.DecodeMessageAsync(_encodedSmallPath);

    [Benchmark]
    public async Task<string> Decode_LargeImage() =>
        await _steganography.DecodeMessageAsync(_encodedLargePath);
}
