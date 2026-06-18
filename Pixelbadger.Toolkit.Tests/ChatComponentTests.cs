using FluentAssertions;
using Moq;
using OpenAI.Chat;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Models;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Tests;

public class ChatComponentTests
{
    private readonly Mock<ILlmClientService> _mockLlmService;
    private readonly Mock<IHistoryService> _mockHistoryService;
    private readonly ChatComponent _chatComponent;

    public ChatComponentTests()
    {
        _mockLlmService = new Mock<ILlmClientService>();
        _mockHistoryService = new Mock<IHistoryService>();
        _chatComponent = new ChatComponent(_mockLlmService.Object, _mockHistoryService.Object);
    }

    [Fact]
    public async Task ChatAsync_ShouldReturnResponse_ForSimpleQuestion()
    {
        // Arrange
        var question = "What is the weather today?";
        var expectedResponse = "It's sunny and warm today.";

        _mockLlmService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<string?>()))
            .ReturnsAsync(new LlmChatResult(expectedResponse, 10, 5));
        _mockHistoryService.Setup(x => x.CreateSessionAsync("chat")).ReturnsAsync(1L);

        // Act
        var result = await _chatComponent.ChatAsync(question, null);

        // Assert
        result.Response.Should().Be(expectedResponse);
        _mockLlmService.Verify(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task ChatAsync_ShouldReturnNewSessionId_WhenNoSessionProvided()
    {
        // Arrange
        var question = "Hello";
        _mockLlmService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<string?>()))
            .ReturnsAsync(new LlmChatResult("Hi there!", 5, 3));
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
        _mockLlmService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<string?>()))
            .ReturnsAsync(new LlmChatResult("Follow up answer", 20, 10));

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
        _mockLlmService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<string?>()))
            .Callback<IEnumerable<ChatMessage>, string?>((msgs, _) => capturedMessages = msgs.ToList())
            .ReturnsAsync(new LlmChatResult("Follow up response", 30, 15));

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
        _mockLlmService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<string?>()))
            .ReturnsAsync(new LlmChatResult(response, 10, 5));
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
        _mockLlmService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<string?>()))
            .ReturnsAsync(new LlmChatResult("Response", 25, 12));
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

        _mockLlmService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<string?>()))
            .Callback<IEnumerable<ChatMessage>, string?>((msgs, _) => capturedMessages = msgs.ToList())
            .ReturnsAsync(new LlmChatResult("AI is artificial intelligence.", 10, 5));
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
        _mockLlmService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<string?>()))
            .Callback<IEnumerable<ChatMessage>, string?>((msgs, _) => capturedMessages = msgs.ToList())
            .ReturnsAsync(new LlmChatResult("Sure!", 20, 8));

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
        _mockLlmService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<string?>()))
            .ReturnsAsync(new LlmChatResult("I need more information.", 2, 8));
        _mockHistoryService.Setup(x => x.CreateSessionAsync("chat")).ReturnsAsync(1L);

        // Act
        var result = await _chatComponent.ChatAsync(question, null);

        // Assert
        result.Response.Should().Be("I need more information.");
    }

    [Fact]
    public async Task ChatAsync_ShouldPassReasoningEffortToService_WhenProvided()
    {
        // Arrange
        var question = "Solve this problem";
        string? capturedEffort = null;

        _mockLlmService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<string?>()))
            .Callback<IEnumerable<ChatMessage>, string?>((_, effort) => capturedEffort = effort)
            .ReturnsAsync(new LlmChatResult("Solution", 20, 10));
        _mockHistoryService.Setup(x => x.CreateSessionAsync("chat")).ReturnsAsync(1L);

        // Act
        await _chatComponent.ChatAsync(question, null, "high");

        // Assert
        capturedEffort.Should().Be("high");
    }

    [Fact]
    public async Task ChatAsync_ShouldPassNullReasoningEffort_WhenNotProvided()
    {
        // Arrange
        var question = "Hello";
        string? capturedEffort = "sentinel";

        _mockLlmService.Setup(x => x.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<string?>()))
            .Callback<IEnumerable<ChatMessage>, string?>((_, effort) => capturedEffort = effort)
            .ReturnsAsync(new LlmChatResult("Hi", 5, 3));
        _mockHistoryService.Setup(x => x.CreateSessionAsync("chat")).ReturnsAsync(1L);

        // Act
        await _chatComponent.ChatAsync(question, null);

        // Assert
        capturedEffort.Should().BeNull();
    }
}
