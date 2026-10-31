using FluentAssertions;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Services.Audio;

namespace Pixelbadger.Toolkit.Tests;

public class MidiPlaybackViewFactoryTests
{
    [Theory]
    [InlineData(false, false, true, true)]
    [InlineData(true, false, true, false)]
    [InlineData(false, true, true, false)]
    [InlineData(false, false, false, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, true, true, false)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    public void ShouldUseLiveView_ShouldMatchTruthTable_WhenGivenEnvironment(bool captured, bool redirected, bool interactive, bool expected)
    {
        MidiPlaybackViewFactory.ShouldUseLiveView(captured, redirected, interactive).Should().Be(expected);
    }

    [Fact]
    public void Create_ShouldReturnPlainView_WhenOutputIsCaptured()
    {
        var output = new StringWriter();
        using var _ = Pixelbadger.Toolkit.CommandLine.ToolkitConsole.Capture(
            Pixelbadger.Toolkit.CommandLine.ToolkitConsole.CreateCaptureConsole(output), output, new StringWriter());

        using var view = MidiPlaybackViewFactory.Create(output);

        view.Should().BeOfType<PlainMidiPlaybackView>();
    }
}
