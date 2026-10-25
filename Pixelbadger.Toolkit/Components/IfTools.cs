using System.Text.Json;
using Microsoft.Extensions.AI;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Components;

public sealed record IfToolOutcome(FunctionResultContent Result, bool Finished, string? Summary, string Description);

/// <summary>The interactive-fiction tools exposed to the model, plus validated invocation against an <see cref="IIfSession"/>.</summary>
public class IfTools
{
    private const string StrSchema = """{"type":"string"}""";
    private readonly IIfSession _session;

    public IReadOnlyList<AITool> Tools { get; }

    public IfTools(IIfSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        Tools =
        [
            Declare("send_command", "Play exactly one game turn by typing one command (a single line, at most 200 characters) and return the game's response. 'thought' is your in-character inner monologue (1-3 sentences, first person), shown to the human watching.",
                Schema([("command", StrSchema), ("thought", StrSchema)], ["command", "thought"])),
            Declare("save_slot", "Save the current position to a named slot (letters, digits, '_' and '-', 1-32 characters). Overwrites a slot of the same name.",
                Schema([("name", StrSchema)], ["name"])),
            Declare("restore_slot", "Restore a named slot and return the game's description of the restored location.",
                Schema([("name", StrSchema)], ["name"])),
            Declare("list_slots", "List the names of the saved slots.", Schema([], [])),
            Declare("update_notes", "Replace your persistent notes (map, clues, puzzles, goals; at most 8000 characters). The whole notes text is replaced, so include everything worth keeping.",
                Schema([("notes", StrSchema)], ["notes"])),
            Declare("finish", "Stop playing, with a short summary. Only call when the game is won or over, or you are truly stuck.",
                Schema([("summary", StrSchema)], ["summary"])),
        ];
    }

    private static JsonElement Schema((string Name, string Json)[] props, string[] required)
    {
        var p = string.Join(",", props.Select(x => $"\"{x.Name}\":{x.Json}"));
        var r = string.Join(",", required.Select(x => $"\"{x}\""));
        using var doc = JsonDocument.Parse($"{{\"type\":\"object\",\"properties\":{{{p}}},\"required\":[{r}]}}");
        return doc.RootElement.Clone();
    }

    private static AITool Declare(string name, string description, JsonElement schema) =>
        AIFunctionFactory.CreateDeclaration(name, description, schema);

    public async Task<IfToolOutcome> InvokeAsync(FunctionCallContent call, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(call);
        cancellationToken.ThrowIfCancellationRequested();
        var args = call.Arguments ?? new Dictionary<string, object?>();
        try
        {
            switch (call.Name)
            {
                case "send_command":
                {
                    var command = Str(args, "command");
                    var thought = Str(args, "thought");
                    var r = await _session.PlayAsync(IfActors.Model, command, thought, cancellationToken);
                    return Outcome(call, r, $"send_command: {command}");
                }
                case "save_slot":
                {
                    var name = Str(args, "name");
                    var r = await _session.SaveSlotAsync(IfActors.Model, name, cancellationToken);
                    return Outcome(call, r, $"save_slot: {name}");
                }
                case "restore_slot":
                {
                    var name = Str(args, "name");
                    var r = await _session.RestoreSlotAsync(IfActors.Model, name, cancellationToken);
                    return Outcome(call, r, $"restore_slot: {name}");
                }
                case "list_slots":
                {
                    var slots = _session.ListSlots();
                    var text = slots.Count == 0 ? "No slots saved." : "Slots: " + string.Join(", ", slots);
                    return new(new FunctionResultContent(call.CallId, text), false, null, "list_slots");
                }
                case "update_notes":
                {
                    var notes = Str(args, "notes");
                    var r = _session.UpdateNotes(notes);
                    return Outcome(call, r, "update_notes");
                }
                case "finish":
                {
                    var summary = Str(args, "summary");
                    _session.RecordFinish(summary);
                    return new(new FunctionResultContent(call.CallId, "Finished."), true, summary, $"finish: {summary}");
                }
                default:
                    throw new ArgumentException($"Unknown tool '{call.Name}'.");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var result = new FunctionResultContent(call.CallId, $"Error: {ex.Message}") { Exception = ex };
            return new(result, false, null, $"{call.Name} → error: {ex.Message}");
        }
    }

    private static IfToolOutcome Outcome(FunctionCallContent call, IfActionResult r, string description)
    {
        var text = r.Success ? r.Text : "Error: " + r.Text;
        return new(new FunctionResultContent(call.CallId, text), false, null, description);
    }

    private static string Str(IDictionary<string, object?> args, string name)
    {
        if (!args.TryGetValue(name, out var v) || v is null
            || v is JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined })
            throw new ArgumentException($"Missing argument '{name}'.");
        return v switch
        {
            JsonElement { ValueKind: JsonValueKind.String } je => je.GetString()!,
            string s => s,
            _ => throw new ArgumentException($"Argument '{name}' must be a string."),
        };
    }
}
