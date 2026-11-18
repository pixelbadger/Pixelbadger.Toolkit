using System.Diagnostics;
using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace Pixelbadger.Toolkit.Services;

public sealed record McpServerSettings(string ProfileName, string Host, int Port, IReadOnlyList<McpToolDefinition> Tools, bool Stdio = false);

public interface IMcpServerHost
{
    /// <summary>Starts the server, invokes <paramref name="onStarted"/> once it is listening, then runs until cancelled.</summary>
    Task RunAsync(McpServerSettings settings, Action onStarted, CancellationToken cancellationToken);
}

public static class McpHostRules
{
    private static readonly string[] LoopbackNames = ["localhost", "127.0.0.1", "[::1]", "::1"];

    /// <summary>True for "localhost" or an IP literal (optionally bracketed) that is a loopback address.</summary>
    public static bool IsLoopback(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)) return true;
        var trimmed = host.Trim('[', ']');
        return IPAddress.TryParse(trimmed, out var ip) && IPAddress.IsLoopback(ip);
    }

    /// <summary>True if a request Host header's hostname is one of the accepted loopback names.</summary>
    public static bool IsLoopbackHostName(string? hostName) =>
        hostName is not null && LoopbackNames.Contains(hostName, StringComparer.OrdinalIgnoreCase);

    public static string UrlHost(string host) =>
        host.Contains(':') && !host.StartsWith('[') ? $"[{host}]" : host;
}

/// <summary>Stateless HTTP MCP server exposing a fixed tool catalogue and delegating calls to <see cref="IMcpToolExecutor"/>.</summary>
public sealed class McpServerHost(IMcpToolExecutor executor, TextWriter? callLog = null) : IMcpServerHost
{
    private readonly TextWriter _callLog = callLog ?? Console.Error;
    private int _inFlight;

    public async Task RunAsync(McpServerSettings settings, Action onStarted, CancellationToken cancellationToken)
    {
        if (settings.Stdio)
        {
            await RunStdioAsync(settings, onStarted, cancellationToken).ConfigureAwait(false);
            return;
        }

        await using var app = Build(settings);
        await app.StartAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            onStarted();
            var stopping = app.Lifetime.ApplicationStopping;
            try { await Task.Delay(Timeout.Infinite, CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stopping).Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        finally
        {
            using var grace = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await app.StopAsync(grace.Token).ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
    }

    /// <summary>Serves over newline-delimited JSON-RPC on stdin/stdout; returns when stdin reaches EOF or cancellation is requested.</summary>
    private async Task RunStdioAsync(McpServerSettings settings, Action onStarted, CancellationToken cancellationToken)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings { Args = [] });
        builder.Logging.ClearProviders();
        ConfigureMcp(builder.Services.AddMcpServer(o => o.ServerInfo = ServerInfo()), settings).WithStreamServerTransport(
            new DrainingInputStream(Console.OpenStandardInput(), () => Volatile.Read(ref _inFlight)), Console.OpenStandardOutput());

        using var host = builder.Build();
        await host.StartAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            onStarted();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            try { await host.WaitForShutdownAsync(linked.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        finally
        {
            using var grace = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await host.StopAsync(grace.Token).ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
    }

    private static Implementation ServerInfo() => new()
    {
        Name = "pbtk",
        Version = typeof(McpServerHost).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0"
    };

    private IMcpServerBuilder ConfigureMcp(IMcpServerBuilder mcp, McpServerSettings settings)
    {
        var tools = settings.Tools.ToDictionary(t => t.Name, StringComparer.Ordinal);
        var listed = settings.Tools.Select(t => new Tool
        {
            Name = t.Name,
            Description = t.Description,
            InputSchema = t.InputSchema
        }).ToList();

        return mcp
            .WithListToolsHandler((_, _) => ValueTask.FromResult(new ListToolsResult { Tools = listed }))
            .WithCallToolHandler((request, ct) => new ValueTask<CallToolResult>(HandleCallAsync(
                tools, request.Params?.Name ?? string.Empty,
                request.Params?.Arguments is { } a ? (IReadOnlyDictionary<string, System.Text.Json.JsonElement>)a : null, ct)));
    }

    internal WebApplication Build(McpServerSettings settings)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        builder.Logging.ClearProviders();

        ConfigureMcp(builder.Services
            .AddMcpServer(o => o.ServerInfo = ServerInfo())
            .WithHttpTransport(o => o.Stateless = true), settings);

        var app = builder.Build();
        app.Urls.Add($"http://{McpHostRules.UrlHost(settings.Host)}:{settings.Port}");

        if (McpHostRules.IsLoopback(settings.Host))
        {
            app.Use(async (context, next) =>
            {
                if (!McpHostRules.IsLoopbackHostName(context.Request.Host.Host))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }
                if (context.Request.Headers.TryGetValue("Origin", out var origin) && origin.Count > 0)
                {
                    if (!Uri.TryCreate(origin[0], UriKind.Absolute, out var uri) || !McpHostRules.IsLoopbackHostName(uri.Host))
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return;
                    }
                }
                await next();
            });
        }

        app.MapMcp("/mcp");
        return app;
    }

    internal async Task<CallToolResult> HandleCallAsync(IReadOnlyDictionary<string, McpToolDefinition> tools, string name,
        IReadOnlyDictionary<string, System.Text.Json.JsonElement>? args, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        McpToolResult result;
        Interlocked.Increment(ref _inFlight);
        try
        {
            if (!tools.TryGetValue(name, out var tool))
                result = new McpToolResult($"Unknown tool '{name}'.", true, 1);
            else
                result = await executor.ExecuteAsync(tool, args, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            LogCall(name, 1, watch);
            throw;
        }
        catch (Exception ex)
        {
            result = new McpToolResult($"{ex.Message}\nexit code: 1", true, 1);
        }

        finally
        {
            Interlocked.Decrement(ref _inFlight);
        }

        LogCall(name, result.ExitCode, watch);
        return new CallToolResult
        {
            IsError = result.IsError,
            Content = [new TextContentBlock { Text = result.Text }]
        };
    }

    private void LogCall(string name, int exitCode, Stopwatch watch)
    {
        watch.Stop();
        lock (_callLog)
            _callLog.WriteLine($"tool {name} exit {exitCode} {(long)watch.Elapsed.TotalMilliseconds}ms");
    }
}

/// <summary>
/// Wraps the protocol input so that, at EOF, the server stays up until in-flight tool calls finish and their
/// responses are flushed. Without it a client that writes its requests and closes stdin loses the replies.
/// </summary>
internal sealed class DrainingInputStream(Stream inner, Func<int> inFlight, TimeSpan? grace = null) : Stream
{
    private readonly TimeSpan _grace = grace ?? TimeSpan.FromMilliseconds(300);

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        if (read == 0) await DrainAsync(cancellationToken).ConfigureAwait(false);
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    private async Task DrainAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(_grace, cancellationToken).ConfigureAwait(false);
            while (inFlight() > 0) await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            await Task.Delay(_grace, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
}
