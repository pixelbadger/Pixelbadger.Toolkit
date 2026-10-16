using Spectre.Console;

namespace Pixelbadger.Toolkit.Tests;

internal static class PaintTestSupport
{
    public static IAnsiConsole CreateConsole(StringWriter writer, int width = 4, int height = 4,
        bool interactive = true, bool ansi = true, ColorSystemSupport colors = ColorSystemSupport.TrueColor)
    {
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = ansi ? AnsiSupport.Yes : AnsiSupport.No,
            ColorSystem = colors,
            Interactive = interactive ? InteractionSupport.Yes : InteractionSupport.No,
            Enrichment = new ProfileEnrichment { UseDefaultEnrichers = false },
            Out = new AnsiConsoleOutput(writer)
        });
        console.Profile.Width = width;
        console.Profile.Height = height;
        // Pin capabilities explicitly so CI environment detection cannot change them.
        console.Profile.Capabilities.Interactive = interactive;
        console.Profile.Capabilities.Ansi = ansi;
        console.Profile.Capabilities.ColorSystem = colors switch
        {
            ColorSystemSupport.NoColors => ColorSystem.NoColors,
            ColorSystemSupport.Legacy => ColorSystem.Legacy,
            ColorSystemSupport.Standard => ColorSystem.Standard,
            ColorSystemSupport.EightBit => ColorSystem.EightBit,
            _ => ColorSystem.TrueColor,
        };
        return console;
    }
}
