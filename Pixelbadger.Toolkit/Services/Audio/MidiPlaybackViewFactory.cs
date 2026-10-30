using Pixelbadger.Toolkit.CommandLine;
using Pixelbadger.Toolkit.Components;
using Spectre.Console;

namespace Pixelbadger.Toolkit.Services.Audio;

/// <summary>Chooses between the live terminal view and the plain-line view.</summary>
public static class MidiPlaybackViewFactory
{
    public static IMidiPlaybackView Create(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return ShouldUseLiveView(ToolkitConsole.IsCaptured, Console.IsOutputRedirected, AnsiConsole.Profile.Capabilities.Interactive)
            ? new LiveMidiPlaybackView(AnsiConsole.Console)
            : new PlainMidiPlaybackView(output);
    }

    internal static bool ShouldUseLiveView(bool captured, bool outputRedirected, bool interactive)
        => !captured && !outputRedirected && interactive;
}
