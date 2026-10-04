using FluentAssertions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Pixelbadger.Toolkit.Tests;

public class ImagesDisplayCommandIntegrationTests : IDisposable
{
    private readonly string _testDirectory;

    public ImagesDisplayCommandIntegrationTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    [Fact]
    public async Task DotnetRun_ShouldReturnSuccess_WhenFileOptionPointsToValidImage()
    {
        var path = Path.Combine(_testDirectory, "image.png");
        using (var image = new Image<Rgba32>(4, 4, new Rgba32(255, 0, 0, 255)))
        {
            await image.SaveAsPngAsync(path);
        }

        var (exitCode, _, standardError) = await ToolkitProcess.RunAsync(null, "images", "display", "--file", path);

        exitCode.Should().Be(0, standardError);
    }

    [Fact]
    public async Task DotnetRun_ShouldReturnFailure_WhenFileOptionIsMissing()
    {
        var (exitCode, standardOutput, standardError) = await ToolkitProcess.RunAsync(null, "images", "display");

        exitCode.Should().NotBe(0);
        (standardOutput + standardError).Should().Contain("Option '--file' is required");
    }

    [Fact]
    public async Task DotnetRun_ShouldReturnFailure_WhenFileDoesNotExist()
    {
        var path = Path.Combine(_testDirectory, "missing.png");

        var (exitCode, standardOutput, standardError) = await ToolkitProcess.RunAsync(null, "images", "display", "--file", path);

        exitCode.Should().NotBe(0);
        (standardOutput + standardError).Should().Contain("Error:");
    }
}
