using FluentAssertions;
using Pixelbadger.Toolkit.Components;

namespace Pixelbadger.Toolkit.Tests;

public class ImageSteganographyTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly string _testImagePath;

    public ImageSteganographyTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDirectory);

        // Copy test image to temp directory
        var sourceImagePath = Path.Combine("test-assets", "test-image.jpg");
        _testImagePath = Path.Combine(_testDirectory, "test-image.jpg");

        if (!File.Exists(sourceImagePath))
        {
            throw new FileNotFoundException(
                $"Required test asset not found at: {sourceImagePath}. " +
                "Please ensure test-assets/test-image.jpg exists before running tests.");
        }

        File.Copy(sourceImagePath, _testImagePath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    [Fact]
    public async Task EncodeMessageAsync_ShouldThrowFileNotFoundException_WhenImageDoesNotExist()
    {
        // Arrange
        var steganography = new ImageSteganography();
        var nonExistentImage = Path.Combine(_testDirectory, "nonexistent.jpg");
        var outputPath = Path.Combine(_testDirectory, "output.png");

        // Act & Assert
        var act = async () => await steganography.EncodeMessageAsync(nonExistentImage, "test message", outputPath);
        await act.Should().ThrowAsync<FileNotFoundException>()
            .WithMessage($"Image file not found: {nonExistentImage}");
    }

    [Fact]
    public async Task DecodeMessageAsync_ShouldThrowFileNotFoundException_WhenImageDoesNotExist()
    {
        // Arrange
        var steganography = new ImageSteganography();
        var nonExistentImage = Path.Combine(_testDirectory, "nonexistent.png");

        // Act & Assert
        var act = async () => await steganography.DecodeMessageAsync(nonExistentImage);
        await act.Should().ThrowAsync<FileNotFoundException>()
            .WithMessage($"Image file not found: {nonExistentImage}");
    }

    [Fact]
    public async Task EncodeAndDecodeMessage_ShouldPreserveMessage()
    {
        // Arrange
        var steganography = new ImageSteganography();
        var originalMessage = "This is a secret message!";
        var encodedImagePath = Path.Combine(_testDirectory, "encoded.png");

        // Act
        await steganography.EncodeMessageAsync(_testImagePath, originalMessage, encodedImagePath);
        var decodedMessage = await steganography.DecodeMessageAsync(encodedImagePath);

        // Assert
        decodedMessage.Should().Be(originalMessage);
        File.Exists(encodedImagePath).Should().BeTrue();
    }

    [Fact]
    public async Task EncodeMessage_ShouldCreateOutputFile()
    {
        // Arrange
        var steganography = new ImageSteganography();
        var message = "Test message";
        var outputPath = Path.Combine(_testDirectory, "output.png");

        // Act
        await steganography.EncodeMessageAsync(_testImagePath, message, outputPath);

        // Assert
        File.Exists(outputPath).Should().BeTrue();
        var fileInfo = new FileInfo(outputPath);
        fileInfo.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task EncodeMessage_ShouldHandleEmptyMessage()
    {
        // Arrange
        var steganography = new ImageSteganography();
        var emptyMessage = "";
        var outputPath = Path.Combine(_testDirectory, "empty-message.png");

        // Act
        await steganography.EncodeMessageAsync(_testImagePath, emptyMessage, outputPath);
        var decodedMessage = await steganography.DecodeMessageAsync(outputPath);

        // Assert
        decodedMessage.Should().Be(emptyMessage);
    }

    [Fact]
    public async Task EncodeMessage_ShouldHandleSpecialCharacters()
    {
        // Arrange
        var steganography = new ImageSteganography();
        var messageWithSpecialChars = "Hello! @#$%^&*()_+{}|:<>?[]\\;'\",./ 你好世界";
        var outputPath = Path.Combine(_testDirectory, "special-chars.png");

        // Act
        await steganography.EncodeMessageAsync(_testImagePath, messageWithSpecialChars, outputPath);
        var decodedMessage = await steganography.DecodeMessageAsync(outputPath);

        // Assert
        decodedMessage.Should().Be(messageWithSpecialChars);
    }

    [Fact]
    public async Task EncodeMessage_ShouldHandleMultilineMessage()
    {
        // Arrange
        var steganography = new ImageSteganography();
        var multilineMessage = "Line 1\nLine 2\r\nLine 3\nFinal line";
        var outputPath = Path.Combine(_testDirectory, "multiline.png");

        // Act
        await steganography.EncodeMessageAsync(_testImagePath, multilineMessage, outputPath);
        var decodedMessage = await steganography.DecodeMessageAsync(outputPath);

        // Assert
        decodedMessage.Should().Be(multilineMessage);
    }

    [Fact]
    public async Task DecodeMessage_ShouldThrowForImageWithoutMessage()
    {
        // Arrange
        var steganography = new ImageSteganography();

        // Act & Assert - Try to decode from original image without any encoded message
        var act = async () => await steganography.DecodeMessageAsync(_testImagePath);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("No valid steganographic message found in the image");
    }

    [Fact]
    public async Task EncodeMessage_ShouldThrowForMessageTooLong()
    {
        // Arrange
        var steganography = new ImageSteganography();
        // Create a very long message that would exceed image capacity
        var longMessage = new string('X', 1_000_000); // 1MB of X's
        var outputPath = Path.Combine(_testDirectory, "too-long.png");

        // Act & Assert
        var act = async () => await steganography.EncodeMessageAsync(_testImagePath, longMessage, outputPath);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Message too long for the image capacity");
    }

    [Fact]
    public async Task EncodeAndDecodeMessage_ShouldHandleLongerMessage()
    {
        // Arrange
        var steganography = new ImageSteganography();
        var longerMessage = "Lorem ipsum dolor sit amet, consectetur adipiscing elit. " +
                           "Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. " +
                           "Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris " +
                           "nisi ut aliquip ex ea commodo consequat. Duis aute irure dolor in " +
                           "reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur.";
        var outputPath = Path.Combine(_testDirectory, "longer-message.png");

        // Act
        await steganography.EncodeMessageAsync(_testImagePath, longerMessage, outputPath);
        var decodedMessage = await steganography.DecodeMessageAsync(outputPath);

        // Assert
        decodedMessage.Should().Be(longerMessage);
    }
}