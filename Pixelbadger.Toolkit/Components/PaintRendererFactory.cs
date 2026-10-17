using Spectre.Console;

namespace Pixelbadger.Toolkit.Components;

public sealed record PaintFrameStats(int ChangedCells, int BytesWritten);

public interface IPaintRenderer : IDisposable
{
    void Begin();
    void RenderFrame(PixelColor[] pixels, int size, string status);
    void LogToolCall(string description);
    void End(PixelColor[] pixels, int size);
}

public static class PaintRendererFactory
{
    public static IPaintRenderer Create(IAnsiConsole console)
    {
        var caps = console.Profile.Capabilities;
        if (caps.Interactive && caps.Ansi && caps.ColorSystem == ColorSystem.TrueColor)
            return new DeltaAnsiPaintRenderer(console);
        return new SpectrePaintRenderer(console, caps.Interactive);
    }
}
