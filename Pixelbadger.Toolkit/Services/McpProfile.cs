using System.Text.RegularExpressions;

namespace Pixelbadger.Toolkit.Services;

/// <summary>
/// A named selection of command-tree entries exposed as MCP tools. Include entries are kept exactly as given
/// ("topic" or "topic/action[/sub]"); Host/Port null = no override.
/// </summary>
public sealed record McpProfile(string Name, IReadOnlyList<string> Include, string? Host, int? Port);

public static class McpProfiles
{
    public const string DefaultName = "default";

    private static readonly Regex NamePattern = new("^[a-z0-9-]+$", RegexOptions.Compiled);

    /// <summary>The profile used when none is named and none is saved as "default".</summary>
    public static McpProfile BuiltInDefault => new(DefaultName, ["interpreters"], null, null);

    public static bool IsValidName(string? name) =>
        name is not null && NamePattern.IsMatch(name) && !name.EndsWith('\n');
}
