using System.Text.Json;

namespace Pixelbadger.Toolkit.Services;

/// <summary>Maps MCP tool arguments to CLI tokens. Exactly one of <see cref="Tokens"/> / <see cref="Error"/> is set.</summary>
public sealed record McpArgumentMapping(IReadOnlyList<string>? Tokens, string? Error);

public static class McpArgumentMapper
{
    public static McpArgumentMapping Map(McpToolDefinition tool, IReadOnlyDictionary<string, JsonElement>? arguments)
    {
        ArgumentNullException.ThrowIfNull(tool);
        arguments ??= new Dictionary<string, JsonElement>();

        foreach (var key in arguments.Keys)
            if (!tool.Options.Any(o => o.Key == key))
                return Fail($"Unknown argument '{key}' for tool {tool.Name}.");

        var tokens = new List<string>(tool.CommandPath);
        foreach (var option in tool.Options)
        {
            if (!arguments.TryGetValue(option.Key, out var value)) continue;
            switch (value.ValueKind)
            {
                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                    break;
                case JsonValueKind.Array:
                    foreach (var element in value.EnumerateArray())
                    {
                        if (!TryScalar(element, out var text)) return Unsupported(option.Key, tool);
                        tokens.Add(option.Token);
                        tokens.Add(text);
                    }
                    break;
                default:
                    if (!TryScalar(value, out var scalar)) return Unsupported(option.Key, tool);
                    tokens.Add(option.Token);
                    tokens.Add(scalar);
                    break;
            }
        }
        return new McpArgumentMapping(tokens, null);
    }

    private static bool TryScalar(JsonElement element, out string text)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String: text = element.GetString() ?? string.Empty; return true;
            case JsonValueKind.Number: text = element.GetRawText(); return true;
            case JsonValueKind.True: text = "true"; return true;
            case JsonValueKind.False: text = "false"; return true;
            default: text = string.Empty; return false;
        }
    }

    private static McpArgumentMapping Fail(string error) => new(null, error);

    private static McpArgumentMapping Unsupported(string key, McpToolDefinition tool) =>
        Fail($"Argument '{key}' for tool {tool.Name} has an unsupported type.");
}
