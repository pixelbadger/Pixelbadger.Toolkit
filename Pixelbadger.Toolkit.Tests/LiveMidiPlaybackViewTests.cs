using FluentAssertions;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Services.Audio;
using Spectre.Console;

namespace Pixelbadger.Toolkit.Tests;

public class LiveMidiPlaybackViewTests
{
    private static readonly MidiPlaybackInfo Info = new("a[b].mid", "bank.sf2", TimeSpan.FromSeconds(185));

    /// <summary>StringWriter whose writes and reads share one lock (Spectre's refresh thread writes concurrently).</summary>
    private sealed class LockedWriter : StringWriter
    {
        private readonly object _lock = new();
        public override void Write(char value) { lock (_lock) base.Write(value); }
        public override void Write(char[] buffer, int index, int count) { lock (_lock) base.Write(buffer, index, count); }
        public override void Write(string? value) { lock (_lock) base.Write(value); }
        public override void Write(ReadOnlySpan<char> buffer) { lock (_lock) base.Write(buffer); }
        public override void WriteLine(string? value) { lock (_lock) base.WriteLine(value); }
        public override void Flush() { lock (_lock) base.Flush(); }
        public override string ToString() { lock (_lock) return base.ToString(); }
    }

    private static (IAnsiConsole Console, StringWriter Out) NewConsole()
    {
        var writer = new LockedWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.TrueColor,
            Interactive = InteractionSupport.Yes,
            Out = new AnsiConsoleOutput(writer),
        });
        return (console, writer);
    }

    private static float[] Sine(int n) => Enumerable.Range(0, n).Select(i => (float)Math.Sin(2 * Math.PI * 4 * i / n)).ToArray();

    private static string Plain(string s) => System.Text.RegularExpressions.Regex.Replace(s, "\u001b\\[[0-9;?]*[A-Za-z]", "");

    private static void WaitFor(StringWriter w, Func<string, bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            var text = w.ToString();
            if (predicate(text)) return;
            Thread.Sleep(10);
        }

        throw new TimeoutException("Expected output did not appear: " + Plain(w.ToString()));
    }

    [Fact]
    public void Start_ShouldRenderScopeAndProgress_WhenRunning()
    {
        var (console, output) = NewConsole();
        using var view = new LiveMidiPlaybackView(console, TimeSpan.FromMilliseconds(5));
        view.Start(Info);
        view.Update(Sine(960), Sine(960), TimeSpan.FromSeconds(65));

        WaitFor(output, t => Plain(t).Contains("1:05 / 3:05") && Plain(t).Any(ch => ch > '⠀' && ch <= '⣿'));

        view.Stop(TimeSpan.FromSeconds(65));
        var text = Plain(output.ToString());
        text.Should().Contain("Playing 'a[b].mid' with 'bank.sf2'");
        text.Should().Contain("╭").And.Contain("━");
    }

    [Fact]
    public void Finish_ShouldPrintPlayedLine_WhenCalled()
    {
        var (console, output) = NewConsole();
        var view = new LiveMidiPlaybackView(console, TimeSpan.FromMilliseconds(5));
        view.Start(Info);
        view.Update(Sine(960), Sine(960), TimeSpan.FromSeconds(1));

        view.Finish(TimeSpan.FromSeconds(185.9));
        view.Dispose();

        Plain(output.ToString()).Should().Contain("Played 'a[b].mid' (3:05)");
        Plain(output.ToString()).Should().NotContain("Stopped");
    }

    [Fact]
    public void Stop_ShouldPrintStoppedLine_WhenCalled()
    {
        var (console, output) = NewConsole();
        var view = new LiveMidiPlaybackView(console, TimeSpan.FromMilliseconds(5));
        view.Start(Info);

        view.Stop(TimeSpan.FromSeconds(12.2));
        view.Dispose();

        Plain(output.ToString()).Should().Contain("Stopped at 0:12");
        Plain(output.ToString()).Should().NotContain("Played");
    }

    [Fact]
    public void Dispose_ShouldPrintNoSummaryLine_WhenCalledWithoutFinish()
    {
        var (console, output) = NewConsole();
        var view = new LiveMidiPlaybackView(console, TimeSpan.FromMilliseconds(5));
        view.Start(Info);
        view.Update(Sine(960), Sine(960), TimeSpan.FromSeconds(1));

        view.Dispose();
        view.Dispose();

        var text = Plain(output.ToString());
        text.Should().NotContain("Played").And.NotContain("Stopped");
    }

    [Fact]
    public void Update_ShouldBeHarmless_WhenCalledAfterDispose()
    {
        var (console, output) = NewConsole();
        var view = new LiveMidiPlaybackView(console, TimeSpan.FromMilliseconds(5));
        view.Start(Info);
        view.Dispose();
        var before = output.ToString();

        view.Update(Sine(960), Sine(960), TimeSpan.FromSeconds(2));
        Thread.Sleep(50);

        output.ToString().Should().Be(before);
    }

    [Fact]
    public void Dispose_ShouldBeHarmless_WhenNeverStarted()
    {
        var (console, output) = NewConsole();
        var view = new LiveMidiPlaybackView(console);

        view.Dispose();
        view.Finish(TimeSpan.Zero);

        output.ToString().Should().BeEmpty();
    }

    private static float[] Ramp(int start, int n) => Enumerable.Range(start, n).Select(i => i / 100000f).ToArray();

    [Fact]
    public void Update_ShouldKeepMostRecentFrames_WhenCalledWithSmallBlocks()
    {
        var (console, _) = NewConsole();
        using var view = new LiveMidiPlaybackView(console);

        for (var i = 0; i < 30; i++)
            view.Update(Ramp(i * 100, 100), Ramp(i * 100 + 50000, 100), TimeSpan.FromMilliseconds(i * 2));

        var (left, right) = view.WindowSnapshot();
        left.Should().Equal(Ramp(3000 - 960, 960));
        right.Should().Equal(Ramp(3000 - 960 + 50000, 960));
    }

    [Fact]
    public void Update_ShouldAppendWithoutDropping_WhenWindowNotYetFull()
    {
        var (console, _) = NewConsole();
        using var view = new LiveMidiPlaybackView(console);

        for (var i = 0; i < 3; i++)
            view.Update(Ramp(i * 100, 100), Ramp(i * 100, 100), TimeSpan.Zero);

        var (left, _) = view.WindowSnapshot();
        left.Should().Equal(Ramp(0, 300));
    }

    [Fact]
    public void Update_ShouldKeepLastWindowOfFrames_WhenBlockIsLargerThanWindow()
    {
        var (console, _) = NewConsole();
        using var view = new LiveMidiPlaybackView(console);

        view.Update(Ramp(0, 100), Ramp(0, 100), TimeSpan.Zero);
        view.Update(Ramp(1000, 1024), Ramp(1000, 1024), TimeSpan.Zero);

        var (left, right) = view.WindowSnapshot();
        left.Should().Equal(Ramp(1000 + 1024 - 960, 960));
        right.Should().Equal(Ramp(1000 + 1024 - 960, 960));
    }

    [Theory]
    [InlineData(60, 56)]
    [InlineData(100, 96)]
    [InlineData(14, 10)]
    [InlineData(12, 10)]
    public void Scope_ShouldBeConsoleWidthMinusFourWide_WhenRendered(int consoleWidth, int expectedScopeWidth)
    {
        var (console, output) = NewConsole();
        console.Profile.Width = consoleWidth;
        using var view = new LiveMidiPlaybackView(console, TimeSpan.FromMilliseconds(5));
        view.Start(Info);

        WaitFor(output, t => Plain(t).Contains('╮'));
        view.Stop(TimeSpan.Zero);

        // The panel border is the scope width plus the two frame columns.
        var top = Plain(output.ToString()).Split('\n').Select(l => l.TrimEnd())
            .First(l => l.StartsWith('╭') && l.EndsWith('╮'));
        top.Length.Should().Be(expectedScopeWidth + 2);
    }

    [Fact]
    public void Update_ShouldShowFullBar_WhenElapsedExceedsLength()
    {
        var (console, output) = NewConsole();
        using var view = new LiveMidiPlaybackView(console, TimeSpan.FromMilliseconds(5));
        view.Start(new MidiPlaybackInfo("s.mid", "b.sf2", TimeSpan.FromSeconds(2)));
        view.Update(Sine(960), Sine(960), TimeSpan.FromSeconds(4));

        WaitFor(output, t => Plain(t).Contains("0:04 / 0:02"));
        view.Finish(TimeSpan.FromSeconds(4));

        Plain(output.ToString()).Should().Contain("Played 's.mid' (0:04)");
    }
}
