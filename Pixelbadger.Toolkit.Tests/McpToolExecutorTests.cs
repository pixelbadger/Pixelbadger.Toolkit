using System.CommandLine;
using System.Text.Json;
using FluentAssertions;
using Pixelbadger.Toolkit.CommandLine;
using Pixelbadger.Toolkit.Commands;
using Pixelbadger.Toolkit.Services;
using Spectre.Console;

namespace Pixelbadger.Toolkit.Tests;

[Collection("AnsiConsole")]
public class McpToolExecutorTests : IDisposable
{
    private const string HelloWorld =
        "++++++++[>++++[>++>+++>+++>+<<<<-]>+>+>->>+[<]<-]>>.>---.+++++++..+++.>>.<-.<.+++.------.--------.>>+.";

    private readonly string _dir;
    private readonly IAnsiConsole _originalConsole = AnsiConsole.Console;
    private readonly TextWriter _originalOut = Console.Out;
    private readonly TextWriter _originalErr = Console.Error;
    private readonly InMemoryConfigStore _store = new();
    private readonly RootCommand _root;

    public McpToolExecutorTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_dir);
        ToolkitConsole.InstallRouting();
        _root = ToolkitCommandTree.Create(_store);
    }

    public void Dispose()
    {
        Console.SetOut(_originalOut);
        Console.SetError(_originalErr);
        AnsiConsole.Console = _originalConsole;
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private McpToolDefinition Tool(string entry) =>
        McpToolCatalogue.Build(_root, McpEntryResolver.Resolve(_root, entry)!).Single();

    private static IReadOnlyDictionary<string, JsonElement> Args(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

    private McpToolExecutor Executor(TimeSpan? timeout = null, Func<RootCommand>? factory = null) =>
        new(_store, timeout, factory);

    private string Program(string text)
    {
        var path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".bf");
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnProgramOutput_WhenBrainfuckHelloWorldRuns()
    {
        var file = Program(HelloWorld);

        var result = await Executor().ExecuteAsync(Tool("interpreters/brainfuck"), Args($$"""{"file":{{JsonSerializer.Serialize(file)}}}"""));

        result.Text.Should().Be("Hello World!");
        result.IsError.Should().BeFalse();
        result.ExitCode.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnErrorWithExitCode_WhenFileIsMissing()
    {
        var result = await Executor().ExecuteAsync(Tool("interpreters/brainfuck"),
            Args($$"""{"file":{{JsonSerializer.Serialize(Path.Combine(_dir, "missing.bf"))}}}"""));

        result.IsError.Should().BeTrue();
        result.ExitCode.Should().Be(1);
        result.Text.Should().Contain("Error:").And.EndWith("exit code: 1");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldCaptureParseErrorAndNotWriteToRealConsole_WhenRequiredOptionMissing()
    {
        var realOut = new StringWriter();
        var realErr = new StringWriter();
        Console.SetOut(realOut);
        Console.SetError(realErr);

        var result = await Executor().ExecuteAsync(Tool("interpreters/brainfuck"), null);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("--file").And.EndWith("exit code: 1");
        realOut.ToString().Should().BeEmpty();
        realErr.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnArgumentErrorWithoutRunning_WhenArgumentIsUnknown()
    {
        var result = await Executor().ExecuteAsync(Tool("interpreters/brainfuck"), Args("""{"file":"x","nope":1}"""));

        result.Should().Be(new McpToolResult("Unknown argument 'nope' for tool interpreters_brainfuck.", true, 1));
    }

    [Fact]
    public async Task ExecuteAsync_ShouldApplyConfiguredDefault_WhenOptionIsOmitted()
    {
        var tool = Tool("strings/report");
        var explicitResult = await Executor().ExecuteAsync(tool, Args("""{"string":"The cat sat on the mat."}"""));
        _store.Seed("strings", "report", "string", "The cat sat on the mat.");

        var result = await Executor().ExecuteAsync(tool, null);

        explicitResult.IsError.Should().BeFalse(explicitResult.Text);
        result.IsError.Should().BeFalse(result.Text);
        result.Text.Should().Be(explicitResult.Text);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldIsolateOutput_WhenTwentyMixedCallsRunConcurrently()
    {
        var executor = Executor();
        var brainfuck = Tool("interpreters/brainfuck");
        var lev = Tool("strings/levenshtein-distance");

        var calls = Enumerable.Range(0, 20).Select(async i =>
        {
            if (i % 2 == 0)
            {
                var ch = (char)('A' + i);
                var file = Program(new string('+', ch) + ".");
                var r = await executor.ExecuteAsync(brainfuck, Args($$"""{"file":{{JsonSerializer.Serialize(file)}}}"""));
                return (r, Expected: ch.ToString(), Exact: true);
            }
            else
            {
                // distance between "a" and "a" + i 'b's is exactly i
                var r = await executor.ExecuteAsync(lev,
                    Args($$"""{"string1":"a","string2":"a{{new string('b', i)}}"}"""));
                return (r, Expected: i.ToString(), Exact: false);
            }
        }).ToArray();

        var results = await Task.WhenAll(calls);

        foreach (var (r, expected, exact) in results)
        {
            r.IsError.Should().BeFalse(r.Text);
            if (exact) r.Text.Should().Be(expected); else r.Text.Should().Contain(expected);
        }
        // Each distance result names only its own distance.
        results.Where(x => !x.Exact).Select(x => x.r.Text).Distinct().Should().HaveCount(10);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnTimeoutError_WhenCommandOutlivesTimeout()
    {
        var cancelled = new TaskCompletionSource<bool>();
        RootCommand Factory()
        {
            var slow = new Command("slow", "Never finishes");
            slow.SetAction(async (_, ct) =>
            {
                try { await Task.Delay(Timeout.Infinite, ct); }
                catch (OperationCanceledException) { cancelled.SetResult(true); throw; }
                return 0;
            });
            return new RootCommand { slow };
        }
        var tool = McpToolCatalogue.Describe(Factory().Subcommands.Single());

        var result = await Executor(TimeSpan.FromMilliseconds(200), Factory).ExecuteAsync(tool, null);

        result.Should().Be(new McpToolResult("Tool slow timed out after 0.2 s.", true, 1));
        (await Task.WhenAny(cancelled.Task, Task.Delay(5000))).Should().Be(cancelled.Task, "the abandoned command is cancelled");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnExceptionMessageWithExitOne_WhenCommandThrows()
    {
        RootCommand Factory()
        {
            var boom = new Command("boom", "Throws");
            boom.SetAction((_, _) => throw new InvalidOperationException("kaboom"));
            return new RootCommand { boom };
        }
        var tool = McpToolCatalogue.Describe(Factory().Subcommands.Single());

        var result = await Executor(factory: Factory).ExecuteAsync(tool, null);

        result.IsError.Should().BeTrue();
        result.Text.Should().StartWith("stderr:\n").And.Contain("kaboom").And.EndWith("exit code: 1");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldThrowCancellation_WhenCallerCancels()
    {
        RootCommand Factory()
        {
            var slow = new Command("slow", "Never finishes");
            slow.SetAction(async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return 0; });
            return new RootCommand { slow };
        }
        var tool = McpToolCatalogue.Describe(Factory().Subcommands.Single());
        using var cts = new CancellationTokenSource(100);

        var act = () => Executor(factory: Factory).ExecuteAsync(tool, null, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData("", "", 0, "(no output)", false)]
    [InlineData("out\n", "", 0, "out\n", false)]
    [InlineData("out", "err\n", 0, "out\nstderr:\nerr\n", false)]
    [InlineData("out\n", "err\n", 2, "out\nstderr:\nerr\nexit code: 2", true)]
    [InlineData("", "err", 1, "stderr:\nerr\nexit code: 1", true)]
    [InlineData("", "", 3, "exit code: 3", true)]
    public void Format_ShouldComposeText_WhenStreamsAndExitCodeVary(string stdout, string stderr, int code, string text, bool isError)
    {
        McpToolExecutor.Format(stdout, stderr, code).Should().Be(new McpToolResult(text, isError, code));
    }
}
