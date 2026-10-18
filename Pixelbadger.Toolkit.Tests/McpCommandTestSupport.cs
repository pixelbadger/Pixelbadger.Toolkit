using Pixelbadger.Toolkit.CommandLine;
using Pixelbadger.Toolkit.Commands;
using Pixelbadger.Toolkit.Services;
using Spectre.Console;

namespace Pixelbadger.Toolkit.Tests;

/// <summary>Shared invocation helpers for the MCP command test classes.</summary>
internal static class McpCommandTestSupport
{
    public static async Task<(int Code, string Out, string Err)> RunAsync(IConfigStore store, string[] args)
    {
        var root = ToolkitCommandTree.Create(store);
        var output = new StringWriter();
        var error = new StringWriter();
        ToolkitConsole.InstallRouting();
        using (ToolkitConsole.Capture(ToolkitConsole.CreateCaptureConsole(output), output, error))
        {
            var code = await ConfigBootstrap.InvokeAsync(root, args, store);
            return (code, output.ToString(), error.ToString());
        }
    }

    public static string[] Lines(string text) => text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
}
