using FluentAssertions;
using Moq;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Models;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Tests;

public class OpenAiHistoryComponentTests
{
    private readonly Mock<IHistoryService> _mockHistoryService;
    private readonly OpenAiHistoryComponent _historyComponent;

    public OpenAiHistoryComponentTests()
    {
        _mockHistoryService = new Mock<IHistoryService>();
        _historyComponent = new OpenAiHistoryComponent(_mockHistoryService.Object);
    }

    [Fact]
    public async Task ListAsync_ShouldReturnNoSessionsMessage_WhenNoSessionsExist()
    {
        // Arrange
        _mockHistoryService.Setup(x => x.ListSessionsAsync())
            .ReturnsAsync([]);

        // Act
        var result = await _historyComponent.ListAsync();

        // Assert
        result.Should().Be("No sessions found.");
    }

    [Fact]
    public async Task ListAsync_ShouldReturnFormattedTable_WhenSessionsExist()
    {
        // Arrange
        _mockHistoryService.Setup(x => x.ListSessionsAsync())
            .ReturnsAsync([
                new Session { Id = 1, Command = "chat", PromptTokens = 100, CompletionTokens = 50, CreatedAt = "2026-05-28T10:00:00.0000000Z", UpdatedAt = "2026-05-28T10:01:00.0000000Z" },
                new Session { Id = 2, Command = "translate", PromptTokens = 80, CompletionTokens = 30, CreatedAt = "2026-05-28T11:00:00.0000000Z", UpdatedAt = "2026-05-28T11:00:30.0000000Z" }
            ]);

        // Act
        var result = await _historyComponent.ListAsync();

        // Assert
        result.Should().Contain("chat");
        result.Should().Contain("translate");
        result.Should().Contain("100/50");
        result.Should().Contain("80/30");
        result.Should().Contain("1");
        result.Should().Contain("2");
    }

    [Fact]
    public async Task ListAsync_ShouldIncludeHeaderRow()
    {
        // Arrange
        _mockHistoryService.Setup(x => x.ListSessionsAsync())
            .ReturnsAsync([
                new Session { Id = 1, Command = "chat", PromptTokens = 10, CompletionTokens = 5, CreatedAt = "2026-05-28T10:00:00.0000000Z", UpdatedAt = "2026-05-28T10:00:00.0000000Z" }
            ]);

        // Act
        var result = await _historyComponent.ListAsync();

        // Assert
        result.Should().Contain("ID");
        result.Should().Contain("Command");
        result.Should().Contain("Created");
        result.Should().Contain("Tokens");
    }

    [Fact]
    public async Task ListAsync_ShouldFormatCreatedAtWithoutSubseconds()
    {
        // Arrange
        _mockHistoryService.Setup(x => x.ListSessionsAsync())
            .ReturnsAsync([
                new Session { Id = 1, Command = "chat", PromptTokens = 0, CompletionTokens = 0, CreatedAt = "2026-05-28T14:30:00.1234567Z", UpdatedAt = "2026-05-28T14:30:00.1234567Z" }
            ]);

        // Act
        var result = await _historyComponent.ListAsync();

        // Assert
        result.Should().Contain("2026-05-28 14:30:00");
        result.Should().NotContain("1234567");
    }

    [Fact]
    public async Task DeleteAsync_ShouldCallDeleteOnHistoryService_WhenSessionExists()
    {
        // Arrange
        var sessionId = 5L;
        _mockHistoryService.Setup(x => x.SessionExistsAsync(sessionId)).ReturnsAsync(true);

        // Act
        await _historyComponent.DeleteAsync(sessionId);

        // Assert
        _mockHistoryService.Verify(x => x.DeleteSessionAsync(sessionId), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_ShouldThrowInvalidOperationException_WhenSessionNotFound()
    {
        // Arrange
        var sessionId = 999L;
        _mockHistoryService.Setup(x => x.SessionExistsAsync(sessionId)).ReturnsAsync(false);

        // Act & Assert
        var act = async () => await _historyComponent.DeleteAsync(sessionId);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"Session {sessionId} not found.");
    }

    [Fact]
    public async Task DeleteAsync_ShouldNotCallDelete_WhenSessionNotFound()
    {
        // Arrange
        var sessionId = 999L;
        _mockHistoryService.Setup(x => x.SessionExistsAsync(sessionId)).ReturnsAsync(false);

        // Act
        try { await _historyComponent.DeleteAsync(sessionId); } catch { }

        // Assert
        _mockHistoryService.Verify(x => x.DeleteSessionAsync(It.IsAny<long>()), Times.Never);
    }

    [Fact]
    public async Task ListAsync_ShouldShowAllSessions_WithCorrectTokenCounts()
    {
        // Arrange
        _mockHistoryService.Setup(x => x.ListSessionsAsync())
            .ReturnsAsync([
                new Session { Id = 3, Command = "ocaaar", PromptTokens = 250, CompletionTokens = 75, CreatedAt = "2026-05-28T09:00:00.0000000Z", UpdatedAt = "2026-05-28T09:00:00.0000000Z" }
            ]);

        // Act
        var result = await _historyComponent.ListAsync();

        // Assert
        result.Should().Contain("ocaaar");
        result.Should().Contain("250/75");
    }
}
