using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using FluentAssertions;

namespace Pixelbadger.Toolkit.Tests;

/// <summary>
/// Process-level tests of `pbtk mcp serve`. The stateless 2.2.0 transport needs no `initialize` before other
/// requests and no protocol-version header; `initialize` returns no Mcp-Session-Id.
/// </summary>
public class McpServerIntegrationTests : IDisposable
{
    private const string HelloWorld =
        "++++++++[>++++[>++>+++>+++>+<<<<-]>+>+>->>+[<]<-]>>.>---.+++++++..+++.>>.<-.<.+++.------.--------.>>+.";

    private readonly string _dir;

    public McpServerIntegrationTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class Server(Process process, int port, StringBuilder stdout, StringBuilder stderr) : IDisposable
    {
        public int Port => port;
        public string Stdout { get { lock (stdout) return stdout.ToString(); } }
        public string Stderr { get { lock (stderr) return stderr.ToString(); } }

        public void Dispose()
        {
            try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { }
            process.WaitForExit(10000);
            process.Dispose();
        }
    }

    private static async Task<Server> StartAsync(params string[] extraArgs)
    {
        var port = FreePort();
        var args = new List<string> { "mcp", "serve", "--port", port.ToString() };
        args.AddRange(extraArgs);
        var process = ToolkitProcess.Start(null, args.ToArray());
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var ready = new TaskCompletionSource();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (stdout) stdout.AppendLine(e.Data);
            if (e.Data.StartsWith("Serving MCP profile")) ready.TrySetResult();
        };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (stderr) stderr.AppendLine(e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        var server = new Server(process, port, stdout, stderr);
        var exited = process.WaitForExitAsync();
        var winner = await Task.WhenAny(ready.Task, exited, Task.Delay(TimeSpan.FromSeconds(60)));
        if (winner != ready.Task)
        {
            var diagnostics = $"stdout: {server.Stdout}\nstderr: {server.Stderr}";
            server.Dispose();
            throw new InvalidOperationException("MCP server did not start. " + diagnostics);
        }
        return server;
    }

    private static async Task<(HttpResponseMessage Response, JsonElement Body)> PostAsync(
        int port, object payload, Action<HttpRequestMessage>? configure = null)
    {
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/mcp")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
        configure?.Invoke(request);
        var response = await client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode || text.Length == 0) return (response, default);

        var json = text;
        if (response.Content.Headers.ContentType?.MediaType == "text/event-stream")
            json = text.Split('\n').First(l => l.StartsWith("data:")).Substring(5).Trim();
        return (response, JsonDocument.Parse(json).RootElement.Clone());
    }

    private static object Call(string tool, object arguments) => new
    {
        jsonrpc = "2.0", id = 2, method = "tools/call", @params = new { name = tool, arguments }
    };

    [Fact]
    public async Task Serve_ShouldInitializeWithoutSessionId_WhenStateless()
    {
        using var server = await StartAsync();

        var (response, body) = await PostAsync(server.Port, new
        {
            jsonrpc = "2.0", id = 1, method = "initialize",
            @params = new { protocolVersion = "2025-06-18", capabilities = new { }, clientInfo = new { name = "t", version = "1" } }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Contains("Mcp-Session-Id").Should().BeFalse();
        body.GetProperty("result").GetProperty("serverInfo").GetProperty("name").GetString().Should().Be("pbtk");
        server.Stdout.Should().Contain($"Serving MCP profile 'default' on http://127.0.0.1:{server.Port}/mcp");
        server.Stdout.Should().Contain("Tools: interpreters_brainfuck, interpreters_ook, interpreters_bf-to-ook");
    }

    [Fact]
    public async Task Serve_ShouldListExactlyDefaultProfileTools_WhenToolsListRequested()
    {
        using var server = await StartAsync();

        var (_, body) = await PostAsync(server.Port, new { jsonrpc = "2.0", id = 1, method = "tools/list" });

        var tools = body.GetProperty("result").GetProperty("tools").EnumerateArray().ToList();
        tools.Select(t => t.GetProperty("name").GetString()).Should()
            .Equal("interpreters_brainfuck", "interpreters_ook", "interpreters_bf-to-ook");
        tools[0].GetProperty("inputSchema").GetProperty("required")[0].GetString().Should().Be("file");
    }

    [Fact]
    public async Task Serve_ShouldReturnProgramOutput_WhenToolCalledWithHelloWorld()
    {
        var file = Path.Combine(_dir, "hello.bf");
        await File.WriteAllTextAsync(file, HelloWorld);
        using var server = await StartAsync();

        var (response, body) = await PostAsync(server.Port, Call("interpreters_brainfuck", new { file }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = body.GetProperty("result");
        result.GetProperty("isError").GetBoolean().Should().BeFalse();
        result.GetProperty("content")[0].GetProperty("text").GetString().Should().Be("Hello World!");
        await WaitForAsync(() => server.Stderr.Contains("tool interpreters_brainfuck exit 0 "));
        server.Stderr.Should().MatchRegex(@"tool interpreters_brainfuck exit 0 \d+ms");
    }

    [Fact]
    public async Task Serve_ShouldReturnErrorResultAndKeepServing_WhenFileIsMissing()
    {
        var file = Path.Combine(_dir, "hello.bf");
        await File.WriteAllTextAsync(file, HelloWorld);
        using var server = await StartAsync();

        var (_, failed) = await PostAsync(server.Port, Call("interpreters_brainfuck", new { file = Path.Combine(_dir, "missing.bf") }));
        var (_, later) = await PostAsync(server.Port, Call("interpreters_brainfuck", new { file }));

        var failure = failed.GetProperty("result");
        failure.GetProperty("isError").GetBoolean().Should().BeTrue();
        failure.GetProperty("content")[0].GetProperty("text").GetString().Should().Contain("Error:").And.EndWith("exit code: 1");
        later.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString().Should().Be("Hello World!");
    }

    [Fact]
    public async Task Serve_ShouldReturnErrorResult_WhenToolIsUnknown()
    {
        using var server = await StartAsync();

        var (_, body) = await PostAsync(server.Port, Call("config_llm", new { }));

        var result = body.GetProperty("result");
        result.GetProperty("isError").GetBoolean().Should().BeTrue();
        result.GetProperty("content")[0].GetProperty("text").GetString().Should().Be("Unknown tool 'config_llm'.");
    }

    [Fact]
    public async Task Serve_ShouldReturn403_WhenHostHeaderIsNotLoopback()
    {
        using var server = await StartAsync();

        var (response, _) = await PostAsync(server.Port, new { jsonrpc = "2.0", id = 1, method = "tools/list" },
            r => r.Headers.Host = "evil.example");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Serve_ShouldReturn403_WhenOriginIsNotLoopback()
    {
        using var server = await StartAsync();

        var (response, _) = await PostAsync(server.Port, new { jsonrpc = "2.0", id = 1, method = "tools/list" },
            r => r.Headers.TryAddWithoutValidation("Origin", "http://evil.example"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Serve_ShouldAccept_WhenOriginAndHostAreLoopback()
    {
        using var server = await StartAsync();

        var (response, _) = await PostAsync(server.Port, new { jsonrpc = "2.0", id = 1, method = "tools/list" },
            r => r.Headers.TryAddWithoutValidation("Origin", $"http://localhost:{server.Port}"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Serve_ShouldExitOneWithError_WhenProfileIsUnknown()
    {
        var (code, stdout, stderr) = await ToolkitProcess.RunAsync(null, "mcp", "serve", "--profile", "ghost", "--port", FreePort().ToString());

        code.Should().Be(1);
        (stdout + stderr).Should().Contain("Error: MCP profile 'ghost' not found.");
    }

    [Fact]
    public async Task Serve_ShouldExitOneWithError_WhenPortIsInUse()
    {
        using var server = await StartAsync();

        var (code, stdout, stderr) = await ToolkitProcess.RunAsync(null, "mcp", "serve", "--port", server.Port.ToString());

        code.Should().Be(1);
        (stdout + stderr).Should().Contain("Error:");
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(50);
    }
}
