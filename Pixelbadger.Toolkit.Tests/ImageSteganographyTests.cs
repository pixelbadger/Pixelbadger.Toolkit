using FluentAssertions;
using Pixelbadger.Toolkit.Components;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Pixelbadger.Toolkit.Tests;

public class ImageSteganographyTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly string _testImagePath;

    public ImageSteganographyTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDirectory);

        // Generate a test image programmatically (200x200 gradient)
        // This eliminates the need for external test assets
        _testImagePath = Path.Combine(_testDirectory, "test-image.jpg");
        CreateTestImage(_testImagePath);
    }

    private static void CreateTestImage(string path)
    {
        // Create a 200x200 test image with a gradient pattern
        // Large enough to store test messages (200*200*3 = 120,000 bits = 15,000 bytes)
        using var image = new Image<Rgba32>(200, 200);

        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                byte r = (byte)((x * 255) / image.Width);
                byte g = (byte)((y * 255) / image.Height);
                byte b = (byte)(((x + y) * 128) / (image.Width + image.Height));
                image[x, y] = new Rgba32(r, g, b, 255);
            }
        }

        image.SaveAsJpeg(path);
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
        // PNG output should be reasonable size - minimum 1KB, maximum 100KB for test image\n        fileInfo.Length.Should().BeInRange(1024, 102400);
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