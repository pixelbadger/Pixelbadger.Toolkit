using FluentAssertions;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Tests;

public class HistoryServiceTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly HistoryService _historyService;

    public HistoryServiceTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDirectory);
        _historyService = new HistoryService(Path.Combine(_testDirectory, "test.db"));
    }

    public void Dispose()
    {
        _historyService.Dispose();
        if (Directory.Exists(_testDirectory))
            Directory.Delete(_testDirectory, true);
    }

    [Fact]
    public async Task CreateSessionAsync_ShouldReturnPositiveSessionId()
    {
        // Act
        var sessionId = await _historyService.CreateSessionAsync("chat");

        // Assert
        sessionId.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task CreateSessionAsync_ShouldIncrementSessionIds()
    {
        // Act
        var id1 = await _historyService.CreateSessionAsync("chat");
        var id2 = await _historyService.CreateSessionAsync("translate");

        // Assert
        id2.Should().BeGreaterThan(id1);
    }

    [Fact]
    public async Task SessionExistsAsync_ShouldReturnTrue_ForExistingSession()
    {
        // Arrange
        var sessionId = await _historyService.CreateSessionAsync("chat");

        // Act
        var exists = await _historyService.SessionExistsAsync(sessionId);

        // Assert
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task SessionExistsAsync_ShouldReturnFalse_ForNonExistentSession()
    {
        // Act
        var exists = await _historyService.SessionExistsAsync(9999L);

        // Assert
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task AddMessageAsync_ShouldStoreMessage_InCorrectSession()
    {
        // Arrange
        var sessionId = await _historyService.CreateSessionAsync("chat");

        // Act
        await _historyService.AddMessageAsync(sessionId, "user", "Hello");
        await _historyService.AddMessageAsync(sessionId, "assistant", "Hi there");

        // Assert
        var messages = (await _historyService.GetSessionMessagesAsync(sessionId)).ToList();
        messages.Should().HaveCount(2);
        messages[0].Role.Should().Be("user");
        messages[0].Content.Should().Be("Hello");
        messages[1].Role.Should().Be("assistant");
        messages[1].Content.Should().Be("Hi there");
    }

    [Fact]
    public async Task AddMessageAsync_ShouldPreserveMessageOrder()
    {
        // Arrange
        var sessionId = await _historyService.CreateSessionAsync("chat");
        var contents = new[] { "first", "second", "third" };

        // Act
        foreach (var content in contents)
            await _historyService.AddMessageAsync(sessionId, "user", content);

        // Assert
        var messages = (await _historyService.GetSessionMessagesAsync(sessionId)).ToList();
        for (int i = 0; i < contents.Length; i++)
            messages[i].Content.Should().Be(contents[i]);
    }

    [Fact]
    public async Task GetSessionMessagesAsync_ShouldReturnOnlyMessagesForSession()
    {
        // Arrange
        var session1 = await _historyService.CreateSessionAsync("chat");
        var session2 = await _historyService.CreateSessionAsync("translate");
        await _historyService.AddMessageAsync(session1, "user", "chat message");
        await _historyService.AddMessageAsync(session2, "user", "translate message");

        // Act
        var session1Messages = (await _historyService.GetSessionMessagesAsync(session1)).ToList();
        var session2Messages = (await _historyService.GetSessionMessagesAsync(session2)).ToList();

        // Assert
        session1Messages.Should().HaveCount(1);
        session1Messages[0].Content.Should().Be("chat message");
        session2Messages.Should().HaveCount(1);
        session2Messages[0].Content.Should().Be("translate message");
    }

    [Fact]
    public async Task GetSessionMessagesAsync_ShouldReturnEmpty_ForSessionWithNoMessages()
    {
        // Arrange
        var sessionId = await _historyService.CreateSessionAsync("chat");

        // Act
        var messages = await _historyService.GetSessionMessagesAsync(sessionId);

        // Assert
        messages.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateTokenUsageAsync_ShouldAccumulateTokens_AcrossMultipleCalls()
    {
        // Arrange
        var sessionId = await _historyService.CreateSessionAsync("chat");

        // Act
        await _historyService.UpdateTokenUsageAsync(sessionId, 10, 5);
        await _historyService.UpdateTokenUsageAsync(sessionId, 20, 8);

        // Assert
        var sessions = (await _historyService.ListSessionsAsync()).ToList();
        var session = sessions.First(s => s.Id == sessionId);
        session.PromptTokens.Should().Be(30);
        session.CompletionTokens.Should().Be(13);
    }

    [Fact]
    public async Task ListSessionsAsync_ShouldReturnAllSessions_InDescendingOrder()
    {
        // Arrange
        await _historyService.CreateSessionAsync("chat");
        await _historyService.CreateSessionAsync("translate");
        await _historyService.CreateSessionAsync("ocaaar");

        // Act
        var sessions = (await _historyService.ListSessionsAsync()).ToList();

        // Assert
        sessions.Should().HaveCount(3);
        sessions.Select(s => s.Id).Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task ListSessionsAsync_ShouldReturnCorrectCommandNames()
    {
        // Arrange
        await _historyService.CreateSessionAsync("chat");
        await _historyService.CreateSessionAsync("translate");

        // Act
        var sessions = (await _historyService.ListSessionsAsync()).ToList();

        // Assert
        sessions.Should().Contain(s => s.Command == "chat");
        sessions.Should().Contain(s => s.Command == "translate");
    }

    [Fact]
    public async Task ListSessionsAsync_ShouldReturnEmpty_WhenNoSessionsExist()
    {
        // Act
        var sessions = await _historyService.ListSessionsAsync();

        // Assert
        sessions.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteSessionAsync_ShouldRemoveSession_FromList()
    {
        // Arrange
        var sessionId = await _historyService.CreateSessionAsync("chat");

        // Act
        await _historyService.DeleteSessionAsync(sessionId);

        // Assert
        var exists = await _historyService.SessionExistsAsync(sessionId);
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteSessionAsync_ShouldCascadeDelete_SessionMessages()
    {
        // Arrange
        var sessionId = await _historyService.CreateSessionAsync("chat");
        await _historyService.AddMessageAsync(sessionId, "user", "Hello");
        await _historyService.AddMessageAsync(sessionId, "assistant", "Hi");

        // Act
        await _historyService.DeleteSessionAsync(sessionId);

        // Assert
        var messages = await _historyService.GetSessionMessagesAsync(sessionId);
        messages.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteSessionAsync_ShouldOnlyDeleteTargetSession()
    {
        // Arrange
        var session1 = await _historyService.CreateSessionAsync("chat");
        var session2 = await _historyService.CreateSessionAsync("translate");

        // Act
        await _historyService.DeleteSessionAsync(session1);

        // Assert
        var session2Exists = await _historyService.SessionExistsAsync(session2);
        session2Exists.Should().BeTrue();
    }

    [Fact]
    public async Task CreatedAt_ShouldBePopulated_WhenSessionCreated()
    {
        // Act
        var sessionId = await _historyService.CreateSessionAsync("chat");

        // Assert
        var sessions = (await _historyService.ListSessionsAsync()).ToList();
        var session = sessions.First(s => s.Id == sessionId);
        session.CreatedAt.Should().NotBeNullOrEmpty();
        session.UpdatedAt.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Messages_ShouldHaveSessionIdSet_ToParentSession()
    {
        // Arrange
        var sessionId = await _historyService.CreateSessionAsync("chat");

        // Act
        await _historyService.AddMessageAsync(sessionId, "user", "Test");

        // Assert
        var messages = (await _historyService.GetSessionMessagesAsync(sessionId)).ToList();
        messages[0].SessionId.Should().Be(sessionId);
    }
}
