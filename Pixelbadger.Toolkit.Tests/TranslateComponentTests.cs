using FluentAssertions;
using Moq;
using OpenAI.Chat;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Tests;

public class TranslateComponentTests
{
    private readonly Mock<IOpenAiClientService> _mockOpenAiService;
    private readonly Mock<IHistoryService> _mockHistoryService;
    private readonly TranslateComponent _translateComponent;

    public TranslateComponentTests()
    {
        _mockOpenAiService = new Mock<IOpenAiClientService>();
        _mockHistoryService = new Mock<IHistoryService>();
        _mockHistoryService.Setup(x => x.CreateSessionAsync("translate")).ReturnsAsync(1L);
        _translateComponent = new TranslateComponent(_mockOpenAiService.Object, _mockHistoryService.Object);
    }

    [Fact]
    public async Task TranslateAsync_ShouldReturnTranslation_ForValidInput()
    {
        // Arrange
        var text = "Hello, how are you?";
        var targetLanguage = "Spanish";
        var expectedTranslation = "Hola, ¿cómo estás?";

        _mockOpenAiService.Setup(x => x.EscapeXml(text)).Returns(text);
        _mockOpenAiService.Setup(x => x.EscapeXml(targetLanguage)).Returns(targetLanguage);
        _mockOpenAiService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>()))
            .ReturnsAsync(new ChatResult(expectedTranslation, 15, 8));

        // Act
        var result = await _translateComponent.TranslateAsync(text, targetLanguage);

        // Assert
        result.Should().Be(expectedTranslation);
        _mockOpenAiService.Verify(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>()), Times.Once);
    }

    [Fact]
    public async Task TranslateAsync_ShouldEscapeXmlInText()
    {
        // Arrange
        var text = "Hello <script>alert('xss')</script>";
        var targetLanguage = "French";
        var expectedTranslation = "Bonjour";
        var escapedText = "Hello &lt;script&gt;alert('xss')&lt;/script&gt;";

        _mockOpenAiService.Setup(x => x.EscapeXml(text)).Returns(escapedText);
        _mockOpenAiService.Setup(x => x.EscapeXml(targetLanguage)).Returns(targetLanguage);
        _mockOpenAiService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>()))
            .ReturnsAsync(new ChatResult(expectedTranslation, 10, 5));

        // Act
        var result = await _translateComponent.TranslateAsync(text, targetLanguage);

        // Assert
        result.Should().Be(expectedTranslation);
        _mockOpenAiService.Verify(x => x.EscapeXml(text), Times.Once);
        _mockOpenAiService.Verify(x => x.EscapeXml(targetLanguage), Times.Once);
    }

    [Fact]
    public async Task TranslateAsync_ShouldEscapeXmlInTargetLanguage()
    {
        // Arrange
        var text = "Hello world";
        var targetLanguage = "French <script>";
        var expectedTranslation = "Bonjour le monde";
        var escapedLanguage = "French &lt;script&gt;";

        _mockOpenAiService.Setup(x => x.EscapeXml(text)).Returns(text);
        _mockOpenAiService.Setup(x => x.EscapeXml(targetLanguage)).Returns(escapedLanguage);
        _mockOpenAiService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>()))
            .ReturnsAsync(new ChatResult(expectedTranslation, 10, 5));

        // Act
        var result = await _translateComponent.TranslateAsync(text, targetLanguage);

        // Assert
        result.Should().Be(expectedTranslation);
        _mockOpenAiService.Verify(x => x.EscapeXml(targetLanguage), Times.Once);
    }

    [Fact]
    public async Task TranslateAsync_ShouldPassCorrectMessagesToService()
    {
        // Arrange
        var text = "Good morning";
        var targetLanguage = "German";
        var expectedTranslation = "Guten Morgen";
        List<ChatMessage>? capturedMessages = null;

        _mockOpenAiService.Setup(x => x.EscapeXml(It.IsAny<string>())).Returns<string>(s => s);
        _mockOpenAiService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>()))
            .Callback<IEnumerable<ChatMessage>>(messages => capturedMessages = messages.ToList())
            .ReturnsAsync(new ChatResult(expectedTranslation, 12, 6));

        // Act
        await _translateComponent.TranslateAsync(text, targetLanguage);

        // Assert
        capturedMessages.Should().NotBeNull();
        capturedMessages.Should().HaveCount(2);

        capturedMessages![0].ToString().Should().Contain("System");
        capturedMessages[0].Content[0].Text.Should().Contain("translation tool");
        capturedMessages[0].Content[0].Text.Should().Contain(targetLanguage);

        capturedMessages[1].ToString().Should().Contain("User");
        capturedMessages[1].Content[0].Text.Should().Contain("<userinput>");
        capturedMessages[1].Content[0].Text.Should().Contain(text);
        capturedMessages[1].Content[0].Text.Should().Contain("</userinput>");
    }

    [Fact]
    public async Task TranslateAsync_ShouldStoreHistoryAfterSuccessfulTranslation()
    {
        // Arrange
        var text = "Hello";
        var targetLanguage = "Spanish";
        _mockOpenAiService.Setup(x => x.EscapeXml(It.IsAny<string>())).Returns<string>(s => s);
        _mockOpenAiService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>()))
            .ReturnsAsync(new ChatResult("Hola", 10, 4));

        // Act
        await _translateComponent.TranslateAsync(text, targetLanguage);

        // Assert
        _mockHistoryService.Verify(x => x.CreateSessionAsync("translate"), Times.Once);
        _mockHistoryService.Verify(x => x.AddMessageAsync(1L, "system", It.IsAny<string>()), Times.Once);
        _mockHistoryService.Verify(x => x.AddMessageAsync(1L, "user", It.IsAny<string>()), Times.Once);
        _mockHistoryService.Verify(x => x.AddMessageAsync(1L, "assistant", "Hola"), Times.Once);
        _mockHistoryService.Verify(x => x.UpdateTokenUsageAsync(1L, 10, 4), Times.Once);
    }

    [Fact]
    public async Task TranslateAsync_ShouldHandleEmptyText()
    {
        // Arrange
        var text = "";
        var targetLanguage = "Italian";
        var expectedTranslation = "";

        _mockOpenAiService.Setup(x => x.EscapeXml(It.IsAny<string>())).Returns<string>(s => s);
        _mockOpenAiService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>()))
            .ReturnsAsync(new ChatResult(expectedTranslation, 8, 0));

        // Act
        var result = await _translateComponent.TranslateAsync(text, targetLanguage);

        // Assert
        result.Should().Be(expectedTranslation);
        _mockOpenAiService.Verify(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>()), Times.Once);
    }

    [Fact]
    public async Task TranslateAsync_ShouldHandleSpecialCharacters()
    {
        // Arrange
        var text = "Café & résumé with émojis 🎉";
        var targetLanguage = "Japanese";
        var expectedTranslation = "カフェとレジュメと絵文字🎉";

        _mockOpenAiService.Setup(x => x.EscapeXml(It.IsAny<string>())).Returns<string>(s => s);
        _mockOpenAiService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>()))
            .ReturnsAsync(new ChatResult(expectedTranslation, 20, 10));

        // Act
        var result = await _translateComponent.TranslateAsync(text, targetLanguage);

        // Assert
        result.Should().Be(expectedTranslation);
    }

    [Fact]
    public async Task TranslateAsync_ShouldHandleLongText()
    {
        // Arrange
        var text = string.Join(" ", Enumerable.Repeat("This is a long sentence with many words.", 50));
        var targetLanguage = "Portuguese";
        var expectedTranslation = "Esta é uma tradução longa.";

        _mockOpenAiService.Setup(x => x.EscapeXml(It.IsAny<string>())).Returns<string>(s => s);
        _mockOpenAiService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>()))
            .ReturnsAsync(new ChatResult(expectedTranslation, 200, 15));

        // Act
        var result = await _translateComponent.TranslateAsync(text, targetLanguage);

        // Assert
        result.Should().Be(expectedTranslation);
    }

    [Fact]
    public async Task TranslateAsync_ShouldVerifySystemPromptContainsSecurityMeasures()
    {
        // Arrange
        var text = "Test text";
        var targetLanguage = "Russian";
        var expectedTranslation = "Тестовый текст";
        List<ChatMessage>? capturedMessages = null;

        _mockOpenAiService.Setup(x => x.EscapeXml(It.IsAny<string>())).Returns<string>(s => s);
        _mockOpenAiService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>()))
            .Callback<IEnumerable<ChatMessage>>(messages => capturedMessages = messages.ToList())
            .ReturnsAsync(new ChatResult(expectedTranslation, 15, 7));

        // Act
        await _translateComponent.TranslateAsync(text, targetLanguage);

        // Assert
        capturedMessages.Should().NotBeNull();
        var systemMessage = capturedMessages![0].Content[0].Text;

        systemMessage.Should().Contain("prompt injection");
        systemMessage.Should().Contain("userinput");
        systemMessage.Should().Contain("ignore any instructions");
    }

    [Theory]
    [InlineData("Spanish", "Hello", "Hola")]
    [InlineData("French", "Goodbye", "Au revoir")]
    [InlineData("German", "Thank you", "Danke")]
    [InlineData("Italian", "Please", "Per favore")]
    public async Task TranslateAsync_ShouldHandleVariousLanguages(string targetLanguage, string text, string expectedTranslation)
    {
        // Arrange
        _mockOpenAiService.Setup(x => x.EscapeXml(It.IsAny<string>())).Returns<string>(s => s);
        _mockOpenAiService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>()))
            .ReturnsAsync(new ChatResult(expectedTranslation, 10, 5));

        // Act
        var result = await _translateComponent.TranslateAsync(text, targetLanguage);

        // Assert
        result.Should().Be(expectedTranslation);
    }

    [Fact]
    public async Task TranslateAsync_ShouldVerifyServiceInteraction()
    {
        // Arrange
        var text = "Hello world";
        var targetLanguage = "Chinese";
        var expectedTranslation = "你好世界";

        _mockOpenAiService.Setup(x => x.EscapeXml(It.IsAny<string>())).Returns<string>(s => s);
        _mockOpenAiService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>()))
            .ReturnsAsync(new ChatResult(expectedTranslation, 10, 5));

        // Act
        var result = await _translateComponent.TranslateAsync(text, targetLanguage);

        // Assert
        result.Should().Be(expectedTranslation);
        _mockOpenAiService.Verify(x => x.CompleteChatAsync(
            It.Is<IEnumerable<ChatMessage>>(messages =>
                messages.Count() == 2)), Times.Once);
    }
}
