using System.CommandLine;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Pixelbadger.Toolkit.CommandLine;
using Pixelbadger.Toolkit.Commands;
using Pixelbadger.Toolkit.Services;
using static Pixelbadger.Toolkit.Tests.RagTestSupport;

namespace Pixelbadger.Toolkit.Tests;

public class RagMcpTests : IDisposable
{
    private readonly string _dir = NewTempDir();

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void Build_ShouldExposeFiveRagToolsWithOptionKeys_WhenCataloguing()
    {
        var root = ToolkitCommandTree.Create(new InMemoryConfigStore());
        var expected = new Dictionary<string, string[]>
        {
            ["ingest"] = ["file", "id", "title", "chunk-size", "chunk-overlap", "index", "index-path", "format"],
            ["list"] = ["page", "page-size", "index", "index-path", "format"],
            ["get"] = ["id", "index", "index-path", "format"],
            ["search"] = ["query", "page", "page-size", "index", "index-path", "format"],
            ["delete"] = ["id", "index", "index-path", "format"],
        };

        foreach (var (action, keys) in expected)
        {
            var tool = McpToolCatalogue.Build(root, McpEntryResolver.Resolve(root, $"rag/{action}")!).Single();
            tool.Name.Should().Be($"rag_{action}");
            var schema = JsonNode.Parse(tool.InputSchema.GetRawText())!;
            var props = schema["properties"]!.AsObject();
            props.Select(p => p.Key).Should().BeEquivalentTo(keys);
            props["format"]!["enum"]!.AsArray().Select(n => n!.GetValue<string>()).Should().Equal("text", "json");
            if (action == "ingest")
            {
                props["chunk-size"]!["type"]!.GetValue<string>().Should().Be("integer");
                schema["required"]!.AsArray().Select(n => n!.GetValue<string>()).Should().Equal("file");
            }
        }
    }

    [Fact]
    public void Mcp_ShouldResolveWholeTopic_WhenEntryIsRag()
    {
        var root = ToolkitCommandTree.Create(new InMemoryConfigStore());

        var tools = McpToolCatalogue.Build(root, McpEntryResolver.Resolve(root, "rag")!);

        tools.Select(t => t.Name).Should().Equal("rag_ingest", "rag_list", "rag_get", "rag_search", "rag_delete");
    }

    [Fact]
    public void ConfigTree_ShouldMirrorRagOptionsExceptIdAndTitle_WhenBuilt()
    {
        var root = ToolkitCommandTree.Create(new InMemoryConfigStore());

        var tree = ConfigTree.Build(root);

        var ingest = tree.Find("rag", "ingest")!;
        ingest.Options.Keys.Should().BeEquivalentTo("chunk-size", "chunk-overlap", "index", "index-path", "format");
        ingest.Options.Keys.Should().NotContain(["id", "title", "file"]);
        tree.Find("rag", "list")!.Options.Keys.Should().BeEquivalentTo("page", "page-size", "index", "index-path", "format");
        tree.Find("rag", "search")!.Options.Keys.Should().BeEquivalentTo("page", "page-size", "index", "index-path", "format");
        tree.Find("rag", "get")!.Options.Keys.Should().BeEquivalentTo("index", "index-path", "format");
        tree.Find("rag", "delete")!.Options.Keys.Should().BeEquivalentTo("index", "index-path", "format");
    }

    [Fact]
    public async Task ConfigRagIngest_ShouldRejectId_WhenSettingDefault()
    {
        using var store = new InMemoryConfigStore();

        var result = await McpCommandTestSupport.RunAsync(store, ["config", "rag", "ingest", "--id", new string('a', 32)]);

        result.Code.Should().Be(1);
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<JsonElement> PostAsync(int port, object payload)
    {
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/mcp")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
        var response = await client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue(text);
        var json = response.Content.Headers.ContentType?.MediaType == "text/event-stream"
            ? text.Split('\n').First(l => l.StartsWith("data:")).Substring(5).Trim()
            : text;
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private static async Task<(bool IsError, string Text)> CallAsync(int port, string tool, object arguments)
    {
        var body = await PostAsync(port, new { jsonrpc = "2.0", id = 2, method = "tools/call", @params = new { name = tool, arguments } });
        var result = body.GetProperty("result");
        return (result.GetProperty("isError").GetBoolean(), result.GetProperty("content")[0].GetProperty("text").GetString()!);
    }

    [Fact]
    public async Task Serve_ShouldListAndRunAllRagTools_WhenProfileIncludesRag()
    {
        var port = FreePort();
        var configHome = Path.Combine(_dir, "config");
        Directory.CreateDirectory(configHome);
        var saved = await ToolkitProcess.RunWithConfigHomeAsync(null, configHome, "mcp", "save", "--name", "rag-tools", "--include", "rag");
        saved.ExitCode.Should().Be(0, saved.StandardError);
        var process = ToolkitProcess.StartWithConfigHome(null, configHome, "mcp", "serve", "--profile", "rag-tools", "--port", port.ToString());
        var ready = new TaskCompletionSource();
        var stdout = new StringBuilder();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (stdout) stdout.AppendLine(e.Data);
            if (e.Data.StartsWith("Serving MCP profile")) ready.TrySetResult();
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            var winner = await Task.WhenAny(ready.Task, process.WaitForExitAsync(), Task.Delay(TimeSpan.FromSeconds(60)));
            if (winner != ready.Task) throw new InvalidOperationException("MCP server did not start. " + stdout);

            var list = await PostAsync(port, new { jsonrpc = "2.0", id = 1, method = "tools/list" });
            var tools = list.GetProperty("result").GetProperty("tools").EnumerateArray().ToList();
            tools.Select(t => t.GetProperty("name").GetString()).Should().Equal("rag_ingest", "rag_list", "rag_get", "rag_search", "rag_delete");
            tools[0].GetProperty("inputSchema").GetProperty("properties").GetProperty("chunk-size").GetProperty("type").GetString().Should().Be("integer");

            var index = Path.Combine(_dir, "idx");
            var file = Path.Combine(_dir, "a.txt");
            await File.WriteAllTextAsync(file, "Pelicans and badgers.\n\nAnother paragraph.");

            var ingest = await CallAsync(port, "rag_ingest", new Dictionary<string, object> { ["file"] = file, ["title"] = "Birds", ["format"] = "json", ["index-path"] = index });
            ingest.IsError.Should().BeFalse(ingest.Text);
            var id = JsonDocument.Parse(ingest.Text).RootElement.GetProperty("id").GetString()!;

            var search = await CallAsync(port, "rag_search", new Dictionary<string, object> { ["query"] = "badgers", ["format"] = "json", ["index-path"] = index });
            JsonDocument.Parse(search.Text).RootElement.GetProperty("results")[0].GetProperty("id").GetString().Should().Be(id);

            var listed = await CallAsync(port, "rag_list", new Dictionary<string, object> { ["format"] = "json", ["index-path"] = index });
            JsonDocument.Parse(listed.Text).RootElement.GetProperty("total").GetInt32().Should().Be(1);

            var got = await CallAsync(port, "rag_get", new Dictionary<string, object> { ["id"] = id, ["format"] = "json", ["index-path"] = index });
            JsonDocument.Parse(got.Text).RootElement.GetProperty("title").GetString().Should().Be("Birds");

            var deleted = await CallAsync(port, "rag_delete", new Dictionary<string, object> { ["id"] = id, ["format"] = "json", ["index-path"] = index });
            JsonDocument.Parse(deleted.Text).RootElement.GetProperty("deleted").GetBoolean().Should().BeTrue();

            var missing = await CallAsync(port, "rag_get", new Dictionary<string, object> { ["id"] = id, ["index-path"] = index });
            missing.IsError.Should().BeTrue();
            missing.Text.Should().Contain($"Document '{id}' not found.");
        }
        finally
        {
            try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { }
            process.WaitForExit(10000);
            process.Dispose();
        }
    }
}
