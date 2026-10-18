using System.CommandLine;
using Pixelbadger.Toolkit.CommandLine;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Commands;

public static class McpCommand
{
    public static Command Create(IConfigStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        var profiles = new SqliteMcpProfileStore(store.DatabasePath);
        Func<RootCommand> tree = () => ToolkitCommandTree.Create(store);

        var command = new Command("mcp", "Serve toolkit commands as MCP tools and manage MCP profiles").ExcludeFromMcp();
        command.Add(CreateServe(store, profiles, tree));
        command.Add(CreateSave(profiles, tree));
        command.Add(CreateDelete(profiles));
        command.Add(CreateList(profiles, tree));
        return command;
    }

    private static Command CreateServe(IConfigStore store, IMcpProfileStore profiles, Func<RootCommand> tree)
    {
        var command = new Command("serve", "Serves a profile of toolkit commands as MCP tools over stateless HTTP");
        var profileOption = new Option<string?>("--profile") { Description = "Profile to serve (default: the 'default' profile)" };
        var hostOption = new Option<string?>("--host") { Description = "Host to bind (overrides the profile; default 127.0.0.1)" }.WithImmutableDefault();
        var portOption = new Option<int?>("--port") { Description = "Port to bind (overrides the profile; default 5100)" }.WithImmutableDefault();
        var allowRemoteOption = new Option<bool>("--allow-remote") { Description = "Allow binding a non-loopback host (the server has no authentication)" }.WithImmutableDefault();
        command.Add(profileOption);
        command.Add(hostOption);
        command.Add(portOption);
        command.Add(allowRemoteOption);

        command.SetAction((parseResult, ct) =>
        {
            var component = new McpServeComponent(profiles, tree, new McpServerHost(new McpToolExecutor(store)));
            return component.RunAsync(parseResult.GetValue(profileOption), parseResult.GetValue(hostOption), parseResult.GetValue(portOption), ct, parseResult.GetValue(allowRemoteOption));
        });
        return command;
    }

    private static Command CreateSave(IMcpProfileStore profiles, Func<RootCommand> tree)
    {
        var command = new Command("save", "Saves (or overwrites) a named MCP profile");
        var nameOption = new Option<string>("--name") { Description = "Profile name (lowercase letters, digits and hyphens)", Required = true }.WithImmutableDefault();
        var includeOption = new Option<string[]>("--include")
        {
            Description = "Entries to expose: 'topic' or 'topic/action[/sub]' (repeatable)",
            Required = true,
            AllowMultipleArgumentsPerToken = true
        }.WithImmutableDefault();
        var hostOption = new Option<string?>("--host") { Description = "Host to bind when serving this profile" }.WithImmutableDefault();
        var portOption = new Option<int?>("--port") { Description = "Port to bind when serving this profile" }.WithImmutableDefault();
        command.Add(nameOption);
        command.Add(includeOption);
        command.Add(hostOption);
        command.Add(portOption);

        command.SetAction(parseResult =>
        {
            var component = new McpSaveComponent(profiles, tree);
            return component.Run(parseResult.GetValue(nameOption)!, parseResult.GetValue(includeOption)!,
                parseResult.GetValue(hostOption), parseResult.GetValue(portOption));
        });
        return command;
    }

    private static Command CreateDelete(IMcpProfileStore profiles)
    {
        var command = new Command("delete", "Deletes a saved MCP profile");
        var nameOption = new Option<string>("--name") { Description = "Profile name", Required = true }.WithImmutableDefault();
        command.Add(nameOption);

        command.SetAction(parseResult => new McpDeleteComponent(profiles).Run(parseResult.GetValue(nameOption)!));
        return command;
    }

    private static Command CreateList(IMcpProfileStore profiles, Func<RootCommand> tree)
    {
        var command = new Command("list", "Lists the built-in default and saved MCP profiles");
        command.SetAction(_ => new McpListComponent(profiles, tree).Run());
        return command;
    }
}
