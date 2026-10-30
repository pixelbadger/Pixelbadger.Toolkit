using FluentAssertions;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Services.Audio;

namespace Pixelbadger.Toolkit.Tests;

public class PlainMidiPlaybackViewTests
{
    private static readonly MidiPlaybackInfo Info = new("song.mid", "bank.sf2", TimeSpan.FromSeconds(185));

    [Fact]
    public void Start_ShouldPrintPlayingLine_WhenCalled()
    {
        var writer = new StringWriter { NewLine = "\n" };
        using var view = new PlainMidiPlaybackView(writer);

        view.Start(Info);

        writer.ToString().Should().Be("Playing 'song.mid' (3:05) with 'bank.sf2'\n");
    }

    [Fact]
    public void Update_ShouldPrintNothing_WhenCalled()
    {
        var writer = new StringWriter();
        using var view = new PlainMidiPlaybackView(writer);
        view.Start(Info);
        var before = writer.ToString();

        view.Update(new float[960], new float[960], TimeSpan.FromSeconds(1));

        writer.ToString().Should().Be(before);
    }

    [Fact]
    public void Finish_ShouldPrintPlayedLine_WhenCalled()
    {
        var writer = new StringWriter { NewLine = "\n" };
        using var view = new PlainMidiPlaybackView(writer);
        view.Start(Info);

        view.Finish(TimeSpan.FromSeconds(186.4));

        writer.ToString().Should().EndWith("Played 'song.mid' (3:06)\n");
    }

    [Fact]
    public void Stop_ShouldPrintStoppedLine_WhenCalled()
    {
        var writer = new StringWriter { NewLine = "\n" };
        using var view = new PlainMidiPlaybackView(writer);
        view.Start(Info);

        view.Stop(TimeSpan.FromSeconds(65.5));

        writer.ToString().Should().EndWith("Stopped at 1:05\n");
    }

    [Fact]
    public void Dispose_ShouldPrintNothing_WhenCalledWithoutFinish()
    {
        var writer = new StringWriter();
        var view = new PlainMidiPlaybackView(writer);
        view.Start(Info);
        var before = writer.ToString();

        view.Dispose();
        view.Dispose();

        writer.ToString().Should().Be(before);
    }
}
