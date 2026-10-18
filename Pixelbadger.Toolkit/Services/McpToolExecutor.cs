using System.CommandLine;
using System.Text.Json;
using Pixelbadger.Toolkit.CommandLine;
using Pixelbadger.Toolkit.Commands;

namespace Pixelbadger.Toolkit.Services;

/// <summary>Outcome of one tool call. <see cref="ExitCode"/> is 1 for timeouts and argument errors.</summary>
public sealed record McpToolResult(string Text, bool IsError, int ExitCode);

public interface IMcpToolExecutor
{
    Task<McpToolResult> ExecuteAsync(McpToolDefinition tool, IReadOnlyDictionary<string, JsonElement>? arguments,
        CancellationToken cancellationToken = default);
}

/// <summary>Runs tools in-process against a fresh command tree per call, capturing stdout and stderr per async flow.</summary>
public sealed class McpToolExecutor : IMcpToolExecutor
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(300);

    private readonly IConfigStore _store;
    private readonly TimeSpan _timeout;
    private readonly Func<RootCommand> _treeFactory;

    public McpToolExecutor(IConfigStore store, TimeSpan? timeout = null, Func<RootCommand>? treeFactory = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        _timeout = timeout ?? DefaultTimeout;
        _treeFactory = treeFactory ?? (() => ToolkitCommandTree.Create(store));
    }

    public async Task<McpToolResult> ExecuteAsync(McpToolDefinition tool, IReadOnlyDictionary<string, JsonElement>? arguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tool);

        var mapping = McpArgumentMapper.Map(tool, arguments);
        if (mapping.Error is not null) return new McpToolResult(mapping.Error, true, 1);
        var tokens = mapping.Tokens!.ToArray();

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = linked.Token;

        var run = Task.Run(() => RunAsync(tokens, token), CancellationToken.None);
        var delay = Task.Delay(_timeout, cancellationToken);

        var finished = await Task.WhenAny(run, delay).ConfigureAwait(false);
        if (finished == run) return await run.ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        linked.Cancel();
        var seconds = _timeout.TotalSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        return new McpToolResult($"Tool {tool.Name} timed out after {seconds} s.", true, 1);
    }

    private async Task<McpToolResult> RunAsync(string[] tokens, CancellationToken cancellationToken)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var console = ToolkitConsole.CreateCaptureConsole(stdout);
        int exitCode;
        using (ToolkitConsole.Capture(console, stdout, stderr))
        {
            try
            {
                var root = _treeFactory();
                exitCode = await ConfigBootstrap.InvokeAsync(root, tokens, _store,
                    new InvocationConfiguration { Output = stdout, Error = stderr }, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                stderr.WriteLine(ex.Message);
                exitCode = 1;
            }
        }
        return Format(stdout.ToString(), stderr.ToString(), exitCode);
    }

    internal static McpToolResult Format(string stdout, string stderr, int exitCode)
    {
        var text = new System.Text.StringBuilder(stdout);
        if (stderr.Length > 0)
        {
            if (text.Length > 0 && text[^1] != '\n') text.Append('\n');
            text.Append("stderr:\n").Append(stderr);
        }
        if (exitCode != 0)
        {
            if (text.Length > 0 && text[^1] != '\n') text.Append('\n');
            text.Append("exit code: ").Append(exitCode);
        }
        var result = text.Length == 0 ? "(no output)" : text.ToString();
        return new McpToolResult(result, exitCode != 0, exitCode);
    }
}
