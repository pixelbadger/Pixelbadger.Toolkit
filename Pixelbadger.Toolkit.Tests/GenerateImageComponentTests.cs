using FluentAssertions;
using Moq;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Tests;

public class GenerateImageComponentTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly Mock<IImageGenerationService> _mockService = new();
    private readonly GenerateImageComponent _component;

    public GenerateImageComponentTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDirectory);
        _component = new GenerateImageComponent(_mockService.Object);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
            Directory.Delete(_testDirectory, true);
    }

    [Fact]
    public async Task GenerateImageAsync_ShouldWriteImageBytes_WhenValidInputProvided()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        _mockService.Setup(x => x.GenerateImageAsync("a cat")).ReturnsAsync(bytes);
        var outFile = Path.Combine(_testDirectory, "cat.png");

        var result = await _component.GenerateImageAsync("a cat", outFile);

        result.Should().Be(Path.GetFullPath(outFile));
        (await File.ReadAllBytesAsync(outFile)).Should().Equal(bytes);
        _mockService.Verify(x => x.GenerateImageAsync("a cat"), Times.Once);
    }

    [Fact]
    public async Task GenerateImageAsync_ShouldCreateDirectory_WhenOutputDirectoryMissing()
    {
        _mockService.Setup(x => x.GenerateImageAsync(It.IsAny<string>())).ReturnsAsync(new byte[] { 9 });
        var outFile = Path.Combine(_testDirectory, "nested", "dir", "img.png");

        await _component.GenerateImageAsync("p", outFile);

        File.Exists(outFile).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GenerateImageAsync_ShouldThrow_WhenPromptEmpty(string prompt)
    {
        var act = () => _component.GenerateImageAsync(prompt, Path.Combine(_testDirectory, "x.png"));

        await act.Should().ThrowAsync<ArgumentException>();
        _mockService.Verify(x => x.GenerateImageAsync(It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GenerateImageAsync_ShouldThrow_WhenOutFileEmpty(string outFile)
    {
        var act = () => _component.GenerateImageAsync("p", outFile);

        await act.Should().ThrowAsync<ArgumentException>();
        _mockService.Verify(x => x.GenerateImageAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task GenerateImageAsync_ShouldNotWriteFile_WhenServiceFails()
    {
        _mockService.Setup(x => x.GenerateImageAsync(It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        var outFile = Path.Combine(_testDirectory, "fail.png");

        var act = () => _component.GenerateImageAsync("p", outFile);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
        File.Exists(outFile).Should().BeFalse();
    }
}
