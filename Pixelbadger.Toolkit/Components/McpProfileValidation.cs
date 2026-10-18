using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Components;

internal static class McpProfileValidation
{
    public static string? ValidateName(string name) =>
        McpProfiles.IsValidName(name)
            ? null
            : $"Invalid profile name '{name}'. Profile names must match ^[a-z0-9-]+$.";

    public static string? ValidatePort(int? port) =>
        port is { } p && (p < 1 || p > 65535) ? "Port must be between 1 and 65535." : null;
}
