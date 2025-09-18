using FluentAssertions;
using Moq;
using OpenAI.Chat;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Services;
using System.Text.Json;

namespace Pixelbadger.Toolkit.Tests;

public class ChatComponentTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly Mock<IOpenAiClientService> _mockOpenAiService;
    private readonly ChatComponent _chatComponent;

    public ChatComponentTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDirectory);

        _mockOpenAiService = new Mock<IOpenAiClientService>();
        _chatComponent = new ChatComponent(_mockOpenAiService.Object);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    [Fact]
    public async Task ChatAsync_ShouldReturnResponse_ForSimpleQuestion()
    {
        // Arrange
        var question = "What is the weather today?";
        var expectedResponse = "It's sunny and warm today.";

        _mockOpenAiService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _chatComponent.ChatAsync(question, null);

        // Assert
        result.Should().Be(expectedResponse);
        _mockOpenAiService.Verify(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>()), Times.Once);
    }

    [Fact]
    public async Task ChatAsync_ShouldSaveChatHistory_WhenHistoryPathProvided()
    {
        // Arrange
        var question = "Test question";
        var expectedResponse = "Test response";
        var historyPath = Path.Combine(_testDirectory, "chat_history.json");

        _mockOpenAiService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _chatComponent.ChatAsync(question, historyPath);

        // Assert
        result.Should().Be(expectedResponse);
        File.Exists(historyPath).Should().BeTrue();

        var historyJson = await File.ReadAllTextAsync(historyPath);
        var history = JsonSerializer.Deserialize<List<ChatComponent.ChatHistoryMessage>>(historyJson);

        history.Should().HaveCount(2);
        history![0].Role.Should().Be("user");
        history[0].Content.Should().Be(question);
        history[1].Role.Should().Be("assistant");
        history[1].Content.Should().Be(expectedResponse);
    }

    [Fact]
    public async Task ChatAsync_ShouldLoadExistingChatHistory()
    {
        // Arrange
        var question = "Follow up question";
        var expectedResponse = "Follow up response";
        var historyPath = Path.Combine(_testDirectory, "existing_history.json");

        // Create existing history
        var existingHistory = new List<ChatComponent.ChatHistoryMessage>
        {
            new() { Role = "user", Content = "Previous question" },
            new() { Role = "assistant", Content = "Previous response" }
        };

        var historyJson = JsonSerializer.Serialize(existingHistory, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(historyPath, historyJson);

        _mockOpenAiService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _chatComponent.ChatAsync(question, historyPath);

        // Assert
        result.Should().Be(expectedResponse);

        // Verify the service was called with all messages (existing + new question)
        _mockOpenAiService.Verify(x => x.CompleteChatAsync(
            It.Is<IEnumerable<ChatMessage>>(messages => messages.Count() == 3)), Times.Once);

        // Verify updated history was saved
        var updatedHistoryJson = await File.ReadAllTextAsync(historyPath);
        var updatedHistory = JsonSerializer.Deserialize<List<ChatComponent.ChatHistoryMessage>>(updatedHistoryJson);

        updatedHistory.Should().HaveCount(4); // 2 existing + 2 new (question + response)
        updatedHistory![2].Content.Should().Be(question);
        updatedHistory[3].Content.Should().Be(expectedResponse);
    }

    [Fact]
    public async Task ChatAsync_ShouldCreateDirectoryForHistoryPath()
    {
        // Arrange
        var question = "Test question";
        var expectedResponse = "Test response";
        var nestedPath = Path.Combine(_testDirectory, "nested", "deep", "chat_history.json");

        _mockOpenAiService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>()))
            .ReturnsAsync(expectedResponse);

        // Act
        await _chatComponent.ChatAsync(question, nestedPath);

        // Assert
        File.Exists(nestedPath).Should().BeTrue();
        Directory.Exists(Path.GetDirectoryName(nestedPath)).Should().BeTrue();
    }

    [Fact]
    public async Task ChatAsync_ShouldHandleEmptyQuestion()
    {
        // Arrange
        var question = "";
        var expectedResponse = "I need more information to help you.";

        _mockOpenAiService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _chatComponent.ChatAsync(question, null);

        // Assert
        result.Should().Be(expectedResponse);
    }

    [Fact]
    public async Task ChatAsync_ShouldHandleNonExistentHistoryPath()
    {
        // Arrange
        var question = "Test question";
        var expectedResponse = "Test response";
        var nonExistentPath = Path.Combine(_testDirectory, "nonexistent.json");

        _mockOpenAiService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _chatComponent.ChatAsync(question, nonExistentPath);

        // Assert
        result.Should().Be(expectedResponse);

        // Should create new history file
        File.Exists(nonExistentPath).Should().BeTrue();
    }

    [Fact]
    public async Task ChatAsync_ShouldPassCorrectMessagesToService()
    {
        // Arrange
        var question = "What is AI?";
        var expectedResponse = "AI is artificial intelligence.";
        List<ChatMessage>? capturedMessages = null;

        _mockOpenAiService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>()))
            .Callback<IEnumerable<ChatMessage>>(messages => capturedMessages = messages.ToList())
            .ReturnsAsync(expectedResponse);

        // Act
        await _chatComponent.ChatAsync(question, null);

        // Assert
        capturedMessages.Should().NotBeNull();
        capturedMessages.Should().HaveCount(1);
        capturedMessages![0].Content[0].Text.Should().Be(question);
    }

    [Fact]
    public async Task ChatAsync_ShouldHandleChatHistoryWithInvalidJson()
    {
        // Arrange
        var question = "Test question";
        var expectedResponse = "Test response";
        var historyPath = Path.Combine(_testDirectory, "invalid_history.json");

        // Create invalid JSON file
        await File.WriteAllTextAsync(historyPath, "{ invalid json }");

        _mockOpenAiService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>()))
            .ReturnsAsync(expectedResponse);

        // Act & Assert - Should throw JsonException for invalid JSON
        var act = async () => await _chatComponent.ChatAsync(question, historyPath);
        await act.Should().ThrowAsync<JsonException>();
    }

    [Fact]
    public async Task ChatAsync_ShouldVerifyServiceInteraction()
    {
        // Arrange
        var question = "Hello";
        var expectedResponse = "Hi there!";

        _mockOpenAiService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _chatComponent.ChatAsync(question, null);

        // Assert
        result.Should().Be(expectedResponse);
        _mockOpenAiService.Verify(x => x.CompleteChatAsync(
            It.Is<IEnumerable<ChatMessage>>(messages =>
                messages.Count() == 1 &&
                messages.First().Content[0].Text == question)), Times.Once);
    }
}