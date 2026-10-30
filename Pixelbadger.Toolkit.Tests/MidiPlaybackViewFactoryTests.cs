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
    public void Create_ShouldReturnPlainView_WhenOutputIsRedirected()
    {
        // Under test runners stdout is redirected, so the factory must fall back to the plain view.
        Console.IsOutputRedirected.Should().BeTrue();

        using var view = MidiPlaybackViewFactory.Create(new StringWriter());

        view.Should().BeOfType<PlainMidiPlaybackView>();
    }
}
