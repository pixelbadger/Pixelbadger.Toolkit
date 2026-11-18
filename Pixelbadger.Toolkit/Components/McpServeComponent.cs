using System.CommandLine;
using Pixelbadger.Toolkit.CommandLine;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Components;

public sealed class McpServeComponent(IMcpProfileStore store, Func<RootCommand> treeFactory, IMcpServerHost serverHost)
{
    public const string DefaultHost = "127.0.0.1";
    public const int DefaultPort = 5100;

    public async Task<int> RunAsync(string? profileName, string? host, int? port, CancellationToken cancellationToken, bool allowRemote = false, bool stdio = false)
    {
        if (stdio && (host is not null || port is not null || allowRemote))
            return Fail("--stdio cannot be combined with --host, --port or --allow-remote.");

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
        if (!stdio)
        {
            if (McpProfileValidation.ValidatePort(effectivePort) is { } portError) return Fail(portError);
            if (effectiveHost.Length == 0) return Fail("Host must not be empty.");
        }

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

        if (!stdio && !McpHostRules.IsLoopback(effectiveHost))
        {
            if (!allowRemote)
                return Fail($"Refusing to bind non-loopback address {effectiveHost}: the MCP server has no authentication and exposes file-reading and file-writing commands. Pass --allow-remote to proceed.");
            ToolkitConsole.Error.WriteLine($"Warning: binding to non-loopback address {effectiveHost}; the MCP server has no authentication.");
        }

        // Over stdio the protocol owns the real stdout: stray process-level writes go to stderr for the serve's lifetime.
        var originalOut = Console.Out;
        if (stdio) Console.SetOut(Console.Error);
        ToolkitConsole.InstallRouting();
        var settings = new McpServerSettings(profile.Name, effectiveHost, effectivePort, tools, stdio);
        try
        {
            await serverHost.RunAsync(settings, () =>
            {
                var banner = stdio ? ToolkitConsole.Error : ToolkitConsole.Out;
                banner.WriteLine(stdio
                    ? $"Serving MCP profile '{profile.Name}' over stdio"
                    : $"Serving MCP profile '{profile.Name}' on http://{McpHostRules.UrlHost(effectiveHost)}:{effectivePort}/mcp");
                banner.WriteLine($"Tools: {string.Join(", ", tools.Select(t => t.Name))}");
                banner.Flush();
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
        finally
        {
            if (stdio) Console.SetOut(originalOut);
        }
        return 0;
    }

    private static int Fail(string message)
    {
        ToolkitConsole.Error.WriteLine($"Error: {message}");
        return 1;
    }
}
