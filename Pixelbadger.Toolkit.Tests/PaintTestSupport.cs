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
            Out = new AnsiConsoleOutput(writer)
        });
        console.Profile.Width = width;
        console.Profile.Height = height;
        return console;
    }
}
