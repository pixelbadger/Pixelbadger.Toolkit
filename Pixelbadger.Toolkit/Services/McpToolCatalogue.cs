using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pixelbadger.Toolkit.Services;

public enum McpOptionKind { String, Integer, Number, Boolean, Array, Enum }

/// <summary>An option as seen by an MCP client: <see cref="Key"/> is the property name, <see cref="Token"/> the CLI flag.</summary>
public sealed record McpOptionInfo(string Key, string Token, McpOptionKind Kind, bool Required);

/// <summary>A command exposed as an MCP tool.</summary>
public sealed record McpToolDefinition(
    string Name,
    string Description,
    IReadOnlyList<string> CommandPath,
    JsonElement InputSchema,
    IReadOnlyList<McpOptionInfo> Options);

public static class McpToolCatalogue
{
    /// <summary>Builds tool definitions for the given commands (which must belong to <paramref name="root"/>).</summary>
    /// <exception cref="InvalidOperationException">Two commands map to the same tool name.</exception>
    public static IReadOnlyList<McpToolDefinition> Build(RootCommand root, IEnumerable<Command> commands)
    {
        var tools = new List<McpToolDefinition>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var command in commands)
        {
            var tool = Describe(command);
            if (!names.Add(tool.Name))
                throw new InvalidOperationException($"Duplicate MCP tool name '{tool.Name}'.");
            tools.Add(tool);
        }
        return tools;
    }

    public static McpToolDefinition Describe(Command command)
    {
        var path = PathOf(command);
        var options = new List<McpOptionInfo>();
        var properties = new JsonObject();
        var required = new JsonArray();

        foreach (var option in command.Options)
        {
            if (option is System.CommandLine.Help.HelpOption or VersionOption) continue;

            var key = option.Name.TrimStart('-');
            var (kind, schema) = MapType(option.ValueType);
            if (!string.IsNullOrEmpty(option.Description)) schema["description"] = option.Description;
            properties[key] = schema;
            if (option.Required) required.Add(key);
            options.Add(new McpOptionInfo(key, option.Name, kind, option.Required));
        }

        var root = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties
        };
        if (required.Count > 0) root["required"] = required;
        root["additionalProperties"] = false;

        return new McpToolDefinition(
            string.Join('_', path),
            command.Description ?? string.Empty,
            path,
            JsonSerializer.SerializeToElement(root),
            options);
    }

    private static string[] PathOf(Command command)
    {
        var path = new List<string>();
        Symbol? current = command;
        while (current is Command c && c is not RootCommand)
        {
            path.Add(c.Name);
            current = c.Parents.FirstOrDefault();
        }
        path.Reverse();
        return path.ToArray();
    }

    private static (McpOptionKind, JsonObject) MapType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        if (type.IsEnum)
        {
            var values = new JsonArray();
            foreach (var name in Enum.GetNames(type)) values.Add(name);
            return (McpOptionKind.Enum, new JsonObject { ["type"] = "string", ["enum"] = values });
        }

        if (type == typeof(string)) return (McpOptionKind.String, new JsonObject { ["type"] = "string" });
        if (type == typeof(bool)) return (McpOptionKind.Boolean, new JsonObject { ["type"] = "boolean" });
        if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte))
            return (McpOptionKind.Integer, new JsonObject { ["type"] = "integer" });
        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
            return (McpOptionKind.Number, new JsonObject { ["type"] = "number" });
        if (typeof(System.Collections.IEnumerable).IsAssignableFrom(type))
        {
            return (McpOptionKind.Array, new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject { ["type"] = "string" }
            });
        }
        return (McpOptionKind.String, new JsonObject { ["type"] = "string" });
    }
}
