using System.Text;
using Microsoft.Extensions.AI;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Components;

/// <summary>Builds the stateless per-step prompt for the interactive-fiction agent from persisted state.</summary>
public static class IfPromptBuilder
{
    public const int EventWindow = 40;

    private const string BaseSystemPrompt =
        "You are playing a parser interactive-fiction game through tools. Each send_command plays exactly one game turn and returns the game's response. " +
        "The 'thought' you give with each command is your in-character inner monologue (1-3 sentences, first person); it is shown to the human watching. " +
        "Never type save, restore, quit or restart as commands: use save_slot before risky actions and restore_slot after death or a dead end. " +
        "Keep a map, clues, puzzles and goals with update_notes; notes persist across sessions and are shown to you every turn. " +
        "A human may halt you, play turns and leave notes; treat human notes as instructions to follow. " +
        "Make 1-5 tool calls per response. Call finish(summary) only when the game is won or over, or you are truly stuck.";

    public static string SystemPrompt(string? instructions)
    {
        if (string.IsNullOrWhiteSpace(instructions)) return BaseSystemPrompt;
        return BaseSystemPrompt + "\n\nAdditional instructions from the user:\n" + instructions;
    }

    public static List<ChatMessage> Build(IReadOnlyList<IfTranscriptEvent> events, string notes, string? instructions, int? humanSinceIndex = null)
    {
        ArgumentNullException.ThrowIfNull(events);
        var sb = new StringBuilder();
        sb.Append("## Your notes\n");
        sb.Append(string.IsNullOrWhiteSpace(notes) ? "(none yet)" : notes.TrimEnd()).Append("\n\n");

        sb.Append("## Recent transcript (oldest first)\n");
        if (events.Count == 0)
        {
            sb.Append("(no events yet)\n");
        }
        else
        {
            var start = Math.Max(0, events.Count - EventWindow);
            if (start > 0) sb.Append($"({start} earlier events omitted)\n");
            for (var i = start; i < events.Count; i++)
                sb.Append(Format(events[i])).Append('\n');
        }
        sb.Append('\n');

        var firstHuman = humanSinceIndex ?? 0;
        if (humanSinceIndex is null)
        {
            var lastModel = -1;
            for (var i = events.Count - 1; i >= 0; i--)
                if (events[i].Actor == IfActors.Model) { lastModel = i; break; }
            firstHuman = lastModel + 1;
        }
        var human = new List<IfTranscriptEvent>();
        for (var i = Math.Max(0, firstHuman); i < events.Count; i++)
            if (events[i].Actor == IfActors.Human) human.Add(events[i]);
        if (human.Count > 0)
        {
            sb.Append("## While you were halted, the human:\n");
            foreach (var e in human) sb.Append(Format(e)).Append('\n');
            sb.Append('\n');
        }

        sb.Append("Continue playing.");
        return
        [
            new ChatMessage(ChatRole.System, SystemPrompt(instructions)),
            new ChatMessage(ChatRole.User, sb.ToString()),
        ];
    }

    private static string Format(IfTranscriptEvent e)
    {
        var withOutput = (string head) => string.IsNullOrEmpty(e.Output) ? head : head + "\n" + e.Output;
        return e.Kind switch
        {
            IfEventKinds.Thought => $"[{e.Actor} thought] {e.Text}",
            IfEventKinds.Command => withOutput($"[{e.Actor}] > {e.Text}"),
            IfEventKinds.Note => $"[{e.Actor} note] {e.Text}",
            IfEventKinds.Save => $"[{e.Actor} save] {e.Text}",
            IfEventKinds.Restore => withOutput($"[{e.Actor} restore] {e.Text}"),
            IfEventKinds.Error => $"[error] {e.Text}",
            IfEventKinds.Finish => $"[finish] {e.Text}",
            _ => withOutput($"[{e.Actor} {e.Kind}] {e.Text}"),
        };
    }
}
