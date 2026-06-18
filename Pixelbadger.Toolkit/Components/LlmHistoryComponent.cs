using System.Text;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Components;

public class LlmHistoryComponent
{
    private readonly IHistoryService _historyService;

    public LlmHistoryComponent(IHistoryService historyService)
    {
        _historyService = historyService;
    }

    public async Task<string> ListAsync()
    {
        var sessions = (await _historyService.ListSessionsAsync()).ToList();

        if (sessions.Count == 0)
            return "No sessions found.";

        var sb = new StringBuilder();
        sb.AppendLine($"{"ID",-6} {"Command",-15} {"Created (UTC)",-25} {"Tokens (in/out)",-18}");
        sb.AppendLine(new string('-', 68));
        foreach (var session in sessions)
        {
            var created = session.CreatedAt.Length > 19 ? session.CreatedAt[..19].Replace("T", " ") : session.CreatedAt;
            sb.AppendLine($"{session.Id,-6} {session.Command,-15} {created,-25} {session.PromptTokens}/{session.CompletionTokens}");
        }
        return sb.ToString().TrimEnd();
    }

    public async Task DeleteAsync(long sessionId)
    {
        if (!await _historyService.SessionExistsAsync(sessionId))
            throw new InvalidOperationException($"Session {sessionId} not found.");

        await _historyService.DeleteSessionAsync(sessionId);
    }
}
