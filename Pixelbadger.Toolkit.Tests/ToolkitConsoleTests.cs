using FluentAssertions;
using Pixelbadger.Toolkit.CommandLine;
using Spectre.Console;

namespace Pixelbadger.Toolkit.Tests;

[Collection("AnsiConsole")]
public class ToolkitConsoleTests : IDisposable
{
    private readonly IAnsiConsole _originalConsole = AnsiConsole.Console;

    public void Dispose() => AnsiConsole.Console = _originalConsole;

    [Fact]
    public void OutAndError_ShouldFallBackToProcessConsole_WhenNothingCaptured()
    {
        ToolkitConsole.Out.Should().BeSameAs(Console.Out);
        ToolkitConsole.Error.Should().BeSameAs(Console.Error);
    }

    [Fact]
    public void OpenStandardInput_ShouldBeEmpty_WhenCaptured()
    {
        var o = new StringWriter();
        using (ToolkitConsole.Capture(ToolkitConsole.CreateCaptureConsole(o), o, new StringWriter()))
        {
            using var reader = new StreamReader(ToolkitConsole.OpenStandardInput());
            reader.ReadToEnd().Should().BeEmpty();
        }
    }

    [Fact]
    public void OpenStandardInput_ShouldNotBeNullStream_WhenNothingCaptured()
    {
        using var stream = ToolkitConsole.OpenStandardInput();
        stream.Should().NotBeSameAs(Stream.Null);
    }

    [Fact]
    public void Capture_ShouldRouteOutErrorAndAnsiConsole_WhenInstalled()
    {
        ToolkitConsole.InstallRouting();
        var o = new StringWriter();
        var e = new StringWriter();

        using (ToolkitConsole.Capture(ToolkitConsole.CreateCaptureConsole(o), o, e))
        {
            ToolkitConsole.Out.WriteLine("plain-out");
            ToolkitConsole.Error.WriteLine("plain-err");
            AnsiConsole.MarkupLine("[red]spectre[/]");
        }

        o.ToString().Should().Contain("plain-out").And.Contain("spectre");
        e.ToString().Should().Contain("plain-err");
    }

    [Fact]
    public void Dispose_ShouldRestorePreviousValues_WhenCaptureEnds()
    {
        var o = new StringWriter();
        using (ToolkitConsole.Capture(ToolkitConsole.CreateCaptureConsole(o), o, new StringWriter()))
        {
            ToolkitConsole.Out.Should().BeSameAs(o);
        }

        ToolkitConsole.Out.Should().BeSameAs(Console.Out);
        ToolkitConsole.Error.Should().BeSameAs(Console.Error);
    }

    [Fact]
    public void Capture_ShouldRestoreOuterCapture_WhenNestedCaptureEnds()
    {
        var outer = new StringWriter();
        var inner = new StringWriter();
        using var outerScope = ToolkitConsole.Capture(ToolkitConsole.CreateCaptureConsole(outer), outer, new StringWriter());
        using (ToolkitConsole.Capture(ToolkitConsole.CreateCaptureConsole(inner), inner, new StringWriter()))
        {
            ToolkitConsole.Out.Should().BeSameAs(inner);
        }

        ToolkitConsole.Out.Should().BeSameAs(outer);
    }

    [Fact]
    public async Task Capture_ShouldIsolateOutput_WhenFlowsRunConcurrently()
    {
        ToolkitConsole.InstallRouting();

        async Task<(string Out, string Err)> Flow(string tag)
        {
            var o = new StringWriter();
            var e = new StringWriter();
            using (ToolkitConsole.Capture(ToolkitConsole.CreateCaptureConsole(o), o, e))
            {
                for (var i = 0; i < 20; i++)
                {
                    ToolkitConsole.Out.WriteLine($"{tag}-out-{i}");
                    await Task.Yield();
                    AnsiConsole.WriteLine($"{tag}-ansi-{i}");
                    await Task.Delay(1);
                    ToolkitConsole.Error.WriteLine($"{tag}-err-{i}");
                }
            }
            return (o.ToString(), e.ToString());
        }

        var results = await Task.WhenAll(Flow("A"), Flow("B"));

        results[0].Out.Should().Contain("A-out-19").And.Contain("A-ansi-19").And.NotContain("B-");
        results[0].Err.Should().Contain("A-err-19").And.NotContain("B-");
        results[1].Out.Should().Contain("B-out-19").And.Contain("B-ansi-19").And.NotContain("A-");
        results[1].Err.Should().Contain("B-err-19").And.NotContain("A-");
    }

    [Fact]
    public void InstallRouting_ShouldBeIdempotent_WhenCalledTwice()
    {
        ToolkitConsole.InstallRouting();
        var first = AnsiConsole.Console;

        ToolkitConsole.InstallRouting();

        AnsiConsole.Console.Should().BeSameAs(first);
    }

    [Fact]
    public void CreateCaptureConsole_ShouldEmitNoAnsiAndNotWrap_WhenWritingLongColouredLine()
    {
        var w = new StringWriter();
        var console = ToolkitConsole.CreateCaptureConsole(w);
        var line = new string('x', 200);

        console.MarkupLine($"[red]{line}[/]");

        var text = w.ToString();
        text.Should().NotContain("\x1b");
        text.TrimEnd('\r', '\n').Should().Be(line);
    }
}
