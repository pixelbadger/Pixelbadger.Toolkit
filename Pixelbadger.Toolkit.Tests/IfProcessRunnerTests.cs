using FluentAssertions;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Tests;

public class IfProcessRunnerTests
{
    private readonly IfProcessRunner _runner = new();
    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task RunAsync_ShouldPassStdinToProcess_AndCaptureStdout()
    {
        var result = await _runner.RunAsync("/bin/sh", ["-c", "cat"], "hello\nworld\n", Generous, default);

        result.ExitCode.Should().Be(0);
        result.TimedOut.Should().BeFalse();
        result.StdOut.Should().Be("hello\nworld\n");
    }

    [Fact]
    public async Task RunAsync_ShouldCaptureStderr_IntoSameBuffer()
    {
        var result = await _runner.RunAsync("/bin/sh", ["-c", "echo out; echo err 1>&2"], "", Generous, default);

        result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries).Should().BeEquivalentTo("out", "err");
    }

    [Fact]
    public async Task RunAsync_ShouldReportExitCode()
    {
        var result = await _runner.RunAsync("/bin/sh", ["-c", "exit 3"], "", Generous, default);

        result.ExitCode.Should().Be(3);
        result.TimedOut.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_ShouldPassArgumentsVerbatim_WithoutShellInterpretation()
    {
        var result = await _runner.RunAsync("/bin/sh", ["-c", "printf '%s|\\n' \"$@\"", "sh", "a b", "$HOME", "x;y"], "", Generous, default);

        result.StdOut.Should().Be("a b|\n$HOME|\nx;y|\n");
    }

    [Fact]
    public async Task RunAsync_ShouldRoundTripUtf8()
    {
        var result = await _runner.RunAsync("/bin/sh", ["-c", "cat"], "café — ✓\n", Generous, default);

        result.StdOut.Should().Be("café — ✓\n");
    }

    [Fact]
    public async Task RunAsync_ShouldHandleLargeInput_WithoutDeadlock()
    {
        var input = string.Concat(Enumerable.Repeat(new string('x', 99) + "\n", 5000));

        var result = await _runner.RunAsync("/bin/sh", ["-c", "cat"], input, Generous, default);

        result.StdOut.Length.Should().Be(input.Length);
    }

    [Fact]
    public async Task RunAsync_ShouldReportTimedOut_AndKillProcess_WhenTimeoutElapses()
    {
        var started = DateTime.UtcNow;

        var result = await _runner.RunAsync("/bin/sh", ["-c", "sleep 30"], "", TimeSpan.FromMilliseconds(300), default);

        result.TimedOut.Should().BeTrue();
        (DateTime.UtcNow - started).Should().BeLessThan(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task RunAsync_ShouldKillChildProcesses_WhenTimeoutElapses()
    {
        var marker = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var result = await _runner.RunAsync("/bin/sh", ["-c", $"(sleep 2; touch {marker}) & wait"], "", TimeSpan.FromMilliseconds(300), default);
            result.TimedOut.Should().BeTrue();

            await Task.Delay(2500);
            File.Exists(marker).Should().BeFalse();
        }
        finally
        {
            if (File.Exists(marker)) File.Delete(marker);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldThrowOperationCanceled_WhenTokenCancelled()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        var act = () => _runner.RunAsync("/bin/sh", ["-c", "sleep 30"], "", Generous, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task RunAsync_ShouldThrowOperationCanceled_WhenTokenAlreadyCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => _runner.RunAsync("/bin/sh", ["-c", "sleep 30"], "", Generous, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task RunAsync_ShouldNotFail_WhenProcessExitsWithoutReadingStdin()
    {
        var input = new string('x', 1_000_000);

        var result = await _runner.RunAsync("/bin/sh", ["-c", "echo done"], input, Generous, default);

        result.StdOut.Should().Be("done\n");
    }
}
