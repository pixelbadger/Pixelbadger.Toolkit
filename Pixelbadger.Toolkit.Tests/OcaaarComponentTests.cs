using FluentAssertions;
using Moq;
using OpenAI.Chat;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Tests;

public class OcaaarComponentTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly Mock<IOpenAiClientService> _mockOpenAiService;
    private readonly OcaaarComponent _ocaaarComponent;

    public OcaaarComponentTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDirectory);

        _mockOpenAiService = new Mock<IOpenAiClientService>();
        _ocaaarComponent = new OcaaarComponent(_mockOpenAiService.Object);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    [Fact]
    public async Task OcaaarAsync_ShouldReturnPirateText_ForValidImage()
    {
        // Arrange
        var imagePath = Path.Combine(_testDirectory, "test.jpg");
        var imageBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }; // JPEG header
        await File.WriteAllBytesAsync(imagePath, imageBytes);

        var expectedPirateText = "Ahoy matey! This here be some fine text, ye scurvy dog!";

        _mockOpenAiService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>()))
            .ReturnsAsync(expectedPirateText);

        // Act
        var result = await _ocaaarComponent.OcaaarAsync(imagePath);

        // Assert
        result.Should().Be(expectedPirateText);
        _mockOpenAiService.Verify(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>()), Times.Once);
    }

    [Fact]
    public async Task OcaaarAsync_ShouldThrowFileNotFoundException_WhenImageDoesNotExist()
    {
        // Arrange
        var nonExistentPath = Path.Combine(_testDirectory, "nonexistent.jpg");

        // Act & Assert
        var act = async () => await _ocaaarComponent.OcaaarAsync(nonExistentPath);
        await act.Should().ThrowAsync<FileNotFoundException>()
            .WithMessage($"Image file not found: {nonExistentPath}");
    }

    [Fact]
    public async Task OcaaarAsync_ShouldPassCorrectMessagesToService()
    {
        // Arrange
        var imagePath = Path.Combine(_testDirectory, "test.png");
        var imageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG header
        await File.WriteAllBytesAsync(imagePath, imageBytes);

        var expectedPirateText = "Arrr, this be some treasure map text!";
        List<ChatMessage>? capturedMessages = null;

        _mockOpenAiService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>()))
            .Callback<IEnumerable<ChatMessage>>(messages => capturedMessages = messages.ToList())
            .ReturnsAsync(expectedPirateText);

        // Act
        await _ocaaarComponent.OcaaarAsync(imagePath);

        // Assert
        capturedMessages.Should().NotBeNull();
        capturedMessages.Should().HaveCount(2);

        // Verify system message
        capturedMessages![0].ToString().Should().Contain("System");
        capturedMessages[0].Content[0].Text.Should().Contain("pirate");
        capturedMessages[0].Content[0].Text.Should().Contain("extract the text");
        capturedMessages[0].Content[0].Text.Should().Contain("bucaneering dialect");

        // Verify user message contains image
        capturedMessages[1].ToString().Should().Contain("User");
        capturedMessages[1].Content.Should().HaveCount(1);
        capturedMessages[1].Content[0].Kind.Should().Be(ChatMessageContentPartKind.Image);
    }

    [Theory]
    [InlineData(".jpg")]
    [InlineData(".jpeg")]
    [InlineData(".png")]
    [InlineData(".gif")]
    [InlineData(".webp")]
    [InlineData(".bmp")] // fallback case
    public async Task OcaaarAsync_ShouldProcessDifferentImageFormats(string extension)
    {
        // Arrange
        var imagePath = Path.Combine(_testDirectory, $"test{extension}");
        var imageBytes = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        await File.WriteAllBytesAsync(imagePath, imageBytes);

        var expectedPirateText = "Yarrr, me hearty!";
        List<ChatMessage>? capturedMessages = null;

        _mockOpenAiService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>()))
            .Callback<IEnumerable<ChatMessage>>(messages => capturedMessages = messages.ToList())
            .ReturnsAsync(expectedPirateText);

        // Act
        await _ocaaarComponent.OcaaarAsync(imagePath);

        // Assert
        capturedMessages.Should().NotBeNull();
        var imageMessage = capturedMessages![1];
        var imagePart = imageMessage.Content[0];

        // Verify the image was processed by checking it's an image part
        imagePart.Kind.Should().Be(ChatMessageContentPartKind.Image);
    }

    [Fact]
    public async Task OcaaarAsync_ShouldHandleLargeImages()
    {
        // Arrange
        var imagePath = Path.Combine(_testDirectory, "large.jpg");
        var largeImageBytes = new byte[1024 * 1024]; // 1MB
        // Fill with some pattern
        for (int i = 0; i < largeImageBytes.Length; i++)
        {
            largeImageBytes[i] = (byte)(i % 256);
        }
        await File.WriteAllBytesAsync(imagePath, largeImageBytes);

        var expectedPirateText = "Shiver me timbers, that be a mighty large image!";

        _mockOpenAiService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>()))
            .ReturnsAsync(expectedPirateText);

        // Act
        var result = await _ocaaarComponent.OcaaarAsync(imagePath);

        // Assert
        result.Should().Be(expectedPirateText);
    }

    [Fact]
    public async Task OcaaarAsync_ShouldHandleEmptyImage()
    {
        // Arrange
        var imagePath = Path.Combine(_testDirectory, "empty.jpg");
        await File.WriteAllBytesAsync(imagePath, Array.Empty<byte>());

        var expectedPirateText = "Arrr, this image be as empty as Davy Jones' locker!";

        _mockOpenAiService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>()))
            .ReturnsAsync(expectedPirateText);

        // Act
        var result = await _ocaaarComponent.OcaaarAsync(imagePath);

        // Assert
        result.Should().Be(expectedPirateText);
    }

    [Fact]
    public async Task OcaaarAsync_ShouldVerifySystemPromptContainsPirateInstructions()
    {
        // Arrange
        var imagePath = Path.Combine(_testDirectory, "test.jpg");
        var imageBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
        await File.WriteAllBytesAsync(imagePath, imageBytes);

        var expectedPirateText = "Yo ho ho and a bottle of rum!";
        List<ChatMessage>? capturedMessages = null;

        _mockOpenAiService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>()))
            .Callback<IEnumerable<ChatMessage>>(messages => capturedMessages = messages.ToList())
            .ReturnsAsync(expectedPirateText);

        // Act
        await _ocaaarComponent.OcaaarAsync(imagePath);

        // Assert
        capturedMessages.Should().NotBeNull();
        var systemMessage = capturedMessages![0].Content[0].Text;

        // Verify pirate-specific instructions
        systemMessage.Should().Contain("pirate");
        systemMessage.Should().Contain("extract the text");
        systemMessage.Should().Contain("translate");
        systemMessage.Should().Contain("bucaneering dialect");
        systemMessage.Should().Contain("ONLY the pirate-translated text");
        systemMessage.Should().Contain("no explanations");
        systemMessage.Should().Contain("Aaargh");
    }

    [Fact]
    public async Task OcaaarAsync_ShouldHandleImagePathWithSpaces()
    {
        // Arrange
        var imagePath = Path.Combine(_testDirectory, "test image with spaces.jpg");
        var imageBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
        await File.WriteAllBytesAsync(imagePath, imageBytes);

        var expectedPirateText = "Ahoy there, ye landlubber!";

        _mockOpenAiService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>()))
            .ReturnsAsync(expectedPirateText);

        // Act
        var result = await _ocaaarComponent.OcaaarAsync(imagePath);

        // Assert
        result.Should().Be(expectedPirateText);
    }

    [Fact]
    public async Task OcaaarAsync_ShouldHandleSpecialCharactersInImagePath()
    {
        // Arrange
        var imagePath = Path.Combine(_testDirectory, "tëst-ïmage_123.png");
        var imageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        await File.WriteAllBytesAsync(imagePath, imageBytes);

        var expectedPirateText = "Batten down the hatches, matey!";

        _mockOpenAiService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>()))
            .ReturnsAsync(expectedPirateText);

        // Act
        var result = await _ocaaarComponent.OcaaarAsync(imagePath);

        // Assert
        result.Should().Be(expectedPirateText);
    }

    [Fact]
    public async Task OcaaarAsync_ShouldVerifyServiceInteraction()
    {
        // Arrange
        var imagePath = Path.Combine(_testDirectory, "interaction.gif");
        var imageBytes = new byte[] { 0x47, 0x49, 0x46, 0x38 }; // GIF header
        await File.WriteAllBytesAsync(imagePath, imageBytes);

        var expectedPirateText = "Splice the mainbrace!";

        _mockOpenAiService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>()))
            .ReturnsAsync(expectedPirateText);

        // Act
        var result = await _ocaaarComponent.OcaaarAsync(imagePath);

        // Assert
        result.Should().Be(expectedPirateText);
        _mockOpenAiService.Verify(x => x.CompleteChatAsync(
            It.Is<IEnumerable<ChatMessage>>(messages =>
                messages.Count() == 2 &&
                messages.Last().Content[0].Kind == ChatMessageContentPartKind.Image)), Times.Once);
    }
}