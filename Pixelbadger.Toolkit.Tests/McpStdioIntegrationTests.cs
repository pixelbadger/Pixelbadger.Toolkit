using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace Pixelbadger.Toolkit.Tests;

/// <summary>Process-level tests of `pbtk mcp serve --stdio`: newline-delimited JSON-RPC over stdin/stdout.</summary>
public class McpStdioIntegrationTests : IDisposable
{
    private const string HelloWorld =
        "++++++++[>++++[>++>+++>+++>+<<<<-]>+>+>->>+[<]<-]>>.>---.+++++++..+++.>>.<-.<.+++.------.--------.>>+.";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public McpStdioIntegrationTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private static string ToolkitDll()
    {
        var configuration = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)).Parent!.Name;
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../Pixelbadger.Toolkit/bin", configuration, "net10.0", "Pixelbadger.Toolkit.dll"));
    }

    private async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(string input, params string[] args)
    {
        var config = Path.Combine(_dir, "config");
        Directory.CreateDirectory(config);
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
        };
        startInfo.Environment["TERM"] = "xterm";
        startInfo.Environment["NO_COLOR"] = "1";
        startInfo.Environment["HOME"] = _dir;
        startInfo.Environment["XDG_CONFIG_HOME"] = config;
        startInfo.Environment["APPDATA"] = config;
        startInfo.ArgumentList.Add(ToolkitDll());
        foreach (var a in args) startInfo.ArgumentList.Add(a);

        using var process = Process.Start(startInfo)!;
        try
        {
            await process.StandardInput.BaseStream.WriteAsync(new UTF8Encoding(false).GetBytes(input));
            process.StandardInput.Close();
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
            return (process.ExitCode, await stdout, await stderr);
        }
        finally
        {
            if (!process.HasExited) process.Kill(true);
        }
    }

    [Fact]
    public async Task Serve_ShouldAnswerOverStdioAndExitZero_WhenStdinClosesAfterRequests()
    {
        var file = Path.Combine(_dir, "hello.bf");
        await File.WriteAllTextAsync(file, HelloWorld);
        var requests = new object[]
        {
            new { jsonrpc = "2.0", id = 1, method = "initialize", @params = new { protocolVersion = "2025-06-18", capabilities = new { }, clientInfo = new { name = "t", version = "1" } } },
            new { jsonrpc = "2.0", method = "notifications/initialized" },
            new { jsonrpc = "2.0", id = 2, method = "tools/list" },
            new { jsonrpc = "2.0", id = 3, method = "tools/call", @params = new { name = "interpreters_brainfuck", arguments = new { file } } }
        };
        var input = string.Concat(requests.Select(r => JsonSerializer.Serialize(r) + "\n"));

        var (code, stdout, stderr) = await RunAsync(input, "mcp", "serve", "--stdio");

        code.Should().Be(0);
        var messages = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => JsonDocument.Parse(l).RootElement).ToList();
        messages.Should().OnlyContain(m => m.GetProperty("jsonrpc").GetString() == "2.0");
        JsonElement Reply(int id) => messages.Single(m => m.TryGetProperty("id", out var i) && i.GetInt32() == id).GetProperty("result");
        Reply(1).GetProperty("serverInfo").GetProperty("name").GetString().Should().Be("pbtk");
        Reply(2).GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("name").GetString()).Should().Equal(
            "interpreters_brainfuck", "interpreters_ook", "interpreters_bf-to-ook", "rag_ingest", "rag_list", "rag_get", "rag_search", "rag_delete");
        var call = Reply(3);
        call.GetProperty("isError").GetBoolean().Should().BeFalse();
        call.GetProperty("content")[0].GetProperty("text").GetString().Should().Be("Hello World!");
        stderr.Should().Contain("Serving MCP profile 'default' over stdio");
        stderr.Should().MatchRegex(@"tool interpreters_brainfuck exit 0 \d+ms");
    }

    [Fact]
    public async Task Serve_ShouldAnswerSlowCallsBeforeExit_WhenStdinClosesImmediatelyAfterRequests()
    {
        // ~255^3 loop iterations: runs well past the old 300 ms EOF grace period.
        var file = Path.Combine(_dir, "slow.bf");
        await File.WriteAllTextAsync(file, "-[>-[>-[>+<-]<-]<-]>>>.");
        var requests = new object[]
        {
            new { jsonrpc = "2.0", id = 1, method = "initialize", @params = new { protocolVersion = "2025-06-18", capabilities = new { }, clientInfo = new { name = "t", version = "1" } } },
            new { jsonrpc = "2.0", method = "notifications/initialized" },
            new { jsonrpc = "2.0", id = 2, method = "tools/call", @params = new { name = "interpreters_brainfuck", arguments = new { file } } },
            new { jsonrpc = "2.0", id = 3, method = "tools/call", @params = new { name = "interpreters_brainfuck", arguments = new { file } } },
            new { jsonrpc = "2.0", id = 4, method = "tools/call", @params = new { name = "interpreters_brainfuck", arguments = new { file } } }
        };
        var input = string.Concat(requests.Select(r => JsonSerializer.Serialize(r) + "\n"));

        var watch = Stopwatch.StartNew();
        var (code, stdout, stderr) = await RunAsync(input, "mcp", "serve", "--stdio");
        watch.Stop();

        code.Should().Be(0);
        var messages = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => JsonDocument.Parse(l).RootElement).ToList();
        var answered = messages.Where(m => m.TryGetProperty("id", out _)).Select(m => m.GetProperty("id").GetInt32()).Order().ToList();
        answered.Should().Equal(1, 2, 3, 4);
        Regex.Matches(stderr, @"tool interpreters_brainfuck exit 0 (\d+)ms").Select(m => long.Parse(m.Groups[1].Value))
            .Should().HaveCount(3).And.OnlyContain(ms => ms > 400, "each call must outlast the former 300 ms grace");
    }

    [Theory]
    [InlineData("--port", "5999")]
    [InlineData("--host", "127.0.0.1")]
    [InlineData("--allow-remote")]
    public async Task Serve_ShouldFailWithNothingOnStdout_WhenStdioCombinedWithNetworkOptions(params string[] extra)
    {
        var (code, stdout, stderr) = await RunAsync("", ["mcp", "serve", "--stdio", .. extra]);

        code.Should().Be(1);
        stdout.Should().BeEmpty();
        stderr.Replace("\r\n", "\n").Should().Be("Error: --stdio cannot be combined with --host, --port or --allow-remote.\n");
    }
}
