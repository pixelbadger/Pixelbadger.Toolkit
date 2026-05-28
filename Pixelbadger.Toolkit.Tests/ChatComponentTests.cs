using FluentAssertions;
using Moq;
using OpenAI.Chat;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Models;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Tests;

public class ChatComponentTests
{
    private readonly Mock<IOpenAiClientService> _mockOpenAiService;
    private readonly Mock<IHistoryService> _mockHistoryService;
    private readonly ChatComponent _chatComponent;

    public ChatComponentTests()
    {
        _mockOpenAiService = new Mock<IOpenAiClientService>();
        _mockHistoryService = new Mock<IHistoryService>();
        _chatComponent = new ChatComponent(_mockOpenAiService.Object, _mockHistoryService.Object);
    }

    [Fact]
    public async Task ChatAsync_ShouldReturnResponse_ForSimpleQuestion()
    {
        // Arrange
        var question = "What is the weather today?";
        var expectedResponse = "It's sunny and warm today.";

        _mockOpenAiService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>()))
            .ReturnsAsync(new ChatResult(expectedResponse, 10, 5));
        _mockHistoryService.Setup(x => x.CreateSessionAsync("chat")).ReturnsAsync(1L);

        // Act
        var result = await _chatComponent.ChatAsync(question, null);

        // Assert
        result.Response.Should().Be(expectedResponse);
        _mockOpenAiService.Verify(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>()), Times.Once);
    }

    [Fact]
    public async Task ChatAsync_ShouldReturnNewSessionId_WhenNoSessionProvided()
    {
        // Arrange
        var question = "Hello";
        _mockOpenAiService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>()))
            .ReturnsAsync(new ChatResult("Hi there!", 5, 3));
        _mockHistoryService.Setup(x => x.CreateSessionAsync("chat")).ReturnsAsync(42L);

        // Act
        var result = await _chatComponent.ChatAsync(question, null);

        // Assert
        result.SessionId.Should().Be(42L);
        _mockHistoryService.Verify(x => x.CreateSessionAsync("chat"), Times.Once);
    }

    [Fact]
    public async Task ChatAsync_ShouldReuseExistingSessionId_WhenSessionProvided()
    {
        // Arrange
        var question = "Follow up";
        var existingSessionId = 7L;
        _mockHistoryService.Setup(x => x.GetSessionMessagesAsync(existingSessionId))
            .ReturnsAsync([
                new Message { Role = "user", Content = "First question" },
                new Message { Role = "assistant", Content = "First answer" }
            ]);
        _mockOpenAiService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>()))
            .ReturnsAsync(new ChatResult("Follow up answer", 20, 10));

        // Act
        var result = await _chatComponent.ChatAsync(question, existingSessionId);

        // Assert
        result.SessionId.Should().Be(existingSessionId);
        _mockHistoryService.Verify(x => x.CreateSessionAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ChatAsync_ShouldLoadExistingMessagesFromSession_WhenSessionIdProvided()
    {
        // Arrange
        var question = "Follow up question";
        var existingSessionId = 3L;
        List<ChatMessage>? capturedMessages = null;

        _mockHistoryService.Setup(x => x.GetSessionMessagesAsync(existingSessionId))
            .ReturnsAsync([
                new Message { Role = "user", Content = "Previous question" },
                new Message { Role = "assistant", Content = "Previous response" }
            ]);
        _mockOpenAiService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>()))
            .Callback<IEnumerable<ChatMessage>>(msgs => capturedMessages = msgs.ToList())
            .ReturnsAsync(new ChatResult("Follow up response", 30, 15));

        // Act
        await _chatComponent.ChatAsync(question, existingSessionId);

        // Assert
        capturedMessages.Should().HaveCount(3); // 2 existing + 1 new
        capturedMessages![2].Content[0].Text.Should().Be(question);
    }

    [Fact]
    public async Task ChatAsync_ShouldStoreUserAndAssistantMessages_InHistory()
    {
        // Arrange
        var question = "Test question";
        var response = "Test response";
        _mockOpenAiService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>()))
            .ReturnsAsync(new ChatResult(response, 10, 5));
        _mockHistoryService.Setup(x => x.CreateSessionAsync("chat")).ReturnsAsync(1L);

        // Act
        await _chatComponent.ChatAsync(question, null);

        // Assert
        _mockHistoryService.Verify(x => x.AddMessageAsync(1L, "user", question), Times.Once);
        _mockHistoryService.Verify(x => x.AddMessageAsync(1L, "assistant", response), Times.Once);
    }

    [Fact]
    public async Task ChatAsync_ShouldUpdateTokenUsage_AfterSuccessfulCall()
    {
        // Arrange
        var question = "Test question";
        _mockOpenAiService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>()))
            .ReturnsAsync(new ChatResult("Response", 25, 12));
        _mockHistoryService.Setup(x => x.CreateSessionAsync("chat")).ReturnsAsync(5L);

        // Act
        await _chatComponent.ChatAsync(question, null);

        // Assert
        _mockHistoryService.Verify(x => x.UpdateTokenUsageAsync(5L, 25, 12), Times.Once);
    }

    [Fact]
    public async Task ChatAsync_ShouldSendOnlyNewMessageToApi_WhenNoSessionProvided()
    {
        // Arrange
        var question = "What is AI?";
        List<ChatMessage>? capturedMessages = null;

        _mockOpenAiService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>()))
            .Callback<IEnumerable<ChatMessage>>(msgs => capturedMessages = msgs.ToList())
            .ReturnsAsync(new ChatResult("AI is artificial intelligence.", 10, 5));
        _mockHistoryService.Setup(x => x.CreateSessionAsync("chat")).ReturnsAsync(1L);

        // Act
        await _chatComponent.ChatAsync(question, null);

        // Assert
        capturedMessages.Should().HaveCount(1);
        capturedMessages![0].Content[0].Text.Should().Be(question);
    }

    [Fact]
    public async Task ChatAsync_ShouldHandleSystemMessagesInExistingSession()
    {
        // Arrange
        var question = "Follow up";
        var existingSessionId = 2L;
        List<ChatMessage>? capturedMessages = null;

        _mockHistoryService.Setup(x => x.GetSessionMessagesAsync(existingSessionId))
            .ReturnsAsync([
                new Message { Role = "system", Content = "You are helpful." },
                new Message { Role = "user", Content = "Hi" },
                new Message { Role = "assistant", Content = "Hello!" }
            ]);
        _mockOpenAiService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>()))
            .Callback<IEnumerable<ChatMessage>>(msgs => capturedMessages = msgs.ToList())
            .ReturnsAsync(new ChatResult("Sure!", 20, 8));

        // Act
        await _chatComponent.ChatAsync(question, existingSessionId);

        // Assert
        capturedMessages.Should().HaveCount(4);
        capturedMessages![0].ToString().Should().Contain("System");
    }

    [Fact]
    public async Task ChatAsync_ShouldHandleEmptyQuestion()
    {
        // Arrange
        var question = "";
        _mockOpenAiService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>()))
            .ReturnsAsync(new ChatResult("I need more information.", 2, 8));
        _mockHistoryService.Setup(x => x.CreateSessionAsync("chat")).ReturnsAsync(1L);

        // Act
        var result = await _chatComponent.ChatAsync(question, null);

        // Assert
        result.Response.Should().Be("I need more information.");
    }
}
