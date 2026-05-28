using Pixelbadger.Toolkit.Models;

namespace Pixelbadger.Toolkit.Services;

public interface IHistoryService
{
    Task<long> CreateSessionAsync(string command);
    Task AddMessageAsync(long sessionId, string role, string content);
    Task UpdateTokenUsageAsync(long sessionId, int additionalPromptTokens, int additionalCompletionTokens);
    Task<IEnumerable<Session>> ListSessionsAsync();
    Task DeleteSessionAsync(long sessionId);
    Task<IEnumerable<Message>> GetSessionMessagesAsync(long sessionId);
    Task<bool> SessionExistsAsync(long sessionId);
}
