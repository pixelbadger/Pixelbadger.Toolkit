using System.CommandLine;
using Pixelbadger.Toolkit.CommandLine;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Components;

public sealed class McpServeComponent(IMcpProfileStore store, Func<RootCommand> treeFactory, IMcpServerHost serverHost)
{
    public const string DefaultHost = "127.0.0.1";
    public const int DefaultPort = 5100;

    public async Task<int> RunAsync(string? profileName, string? host, int? port, CancellationToken cancellationToken, bool allowRemote = false)
    {
        McpProfile? profile;
        try
        {
            var name = profileName ?? McpProfiles.DefaultName;
            if (profileName is not null && McpProfileValidation.ValidateName(profileName) is { } nameError)
                return Fail(nameError);
            profile = store.Get(name) ?? (name == McpProfiles.DefaultName ? McpProfiles.BuiltInDefault : null);
            if (profile is null) return Fail($"MCP profile '{name}' not found.");
        }
        catch (ConfigStoreException ex)
        {
            return Fail(ex.Message);
        }

        var effectiveHost = host ?? profile.Host ?? DefaultHost;
        var effectivePort = port ?? profile.Port ?? DefaultPort;
        if (McpProfileValidation.ValidatePort(effectivePort) is { } portError) return Fail(portError);
        if (effectiveHost.Length == 0) return Fail("Host must not be empty.");

        var root = treeFactory();
        var resolution = McpEntryResolver.ResolveAll(root, profile.Include);
        foreach (var entry in resolution.Unresolved)
            ToolkitConsole.Error.WriteLine($"Warning: skipping unresolved entry '{entry}' in MCP profile '{profile.Name}'");

        if (resolution.Tools.Count == 0) return Fail($"MCP profile '{profile.Name}' exposes no tools.");

        IReadOnlyList<McpToolDefinition> tools;
        try
        {
            tools = McpToolCatalogue.Build(root, resolution.Tools);
        }
        catch (InvalidOperationException ex)
        {
            return Fail(ex.Message);
        }

        if (!McpHostRules.IsLoopback(effectiveHost))
        {
            if (!allowRemote)
                return Fail($"Refusing to bind non-loopback address {effectiveHost}: the MCP server has no authentication and exposes file-reading and file-writing commands. Pass --allow-remote to proceed.");
            ToolkitConsole.Error.WriteLine($"Warning: binding to non-loopback address {effectiveHost}; the MCP server has no authentication.");
        }

        ToolkitConsole.InstallRouting();
        var settings = new McpServerSettings(profile.Name, effectiveHost, effectivePort, tools);
        try
        {
            await serverHost.RunAsync(settings, () =>
            {
                ToolkitConsole.Out.WriteLine($"Serving MCP profile '{profile.Name}' on http://{McpHostRules.UrlHost(effectiveHost)}:{effectivePort}/mcp");
                ToolkitConsole.Out.WriteLine($"Tools: {string.Join(", ", tools.Select(t => t.Name))}");
                ToolkitConsole.Out.Flush();
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
        return 0;
    }

    private static int Fail(string message)
    {
        ToolkitConsole.Error.WriteLine($"Error: {message}");
        return 1;
    }
}
