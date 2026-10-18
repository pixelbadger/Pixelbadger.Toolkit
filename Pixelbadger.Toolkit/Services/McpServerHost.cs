using System.Diagnostics;
using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace Pixelbadger.Toolkit.Services;

public sealed record McpServerSettings(string ProfileName, string Host, int Port, IReadOnlyList<McpToolDefinition> Tools);

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

    public async Task RunAsync(McpServerSettings settings, Action onStarted, CancellationToken cancellationToken)
    {
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

    internal WebApplication Build(McpServerSettings settings)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        builder.Logging.ClearProviders();

        var tools = settings.Tools.ToDictionary(t => t.Name, StringComparer.Ordinal);
        var listed = settings.Tools.Select(t => new Tool
        {
            Name = t.Name,
            Description = t.Description,
            InputSchema = t.InputSchema
        }).ToList();

        var version = typeof(McpServerHost).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

        builder.Services
            .AddMcpServer(o => o.ServerInfo = new Implementation { Name = "pbtk", Version = version })
            .WithHttpTransport(o => o.Stateless = true)
            .WithListToolsHandler((_, _) => ValueTask.FromResult(new ListToolsResult { Tools = listed }))
            .WithCallToolHandler((request, ct) => new ValueTask<CallToolResult>(HandleCallAsync(
                tools, request.Params?.Name ?? string.Empty,
                request.Params?.Arguments is { } a ? (IReadOnlyDictionary<string, System.Text.Json.JsonElement>)a : null, ct)));

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
