using Spectre.Console;

namespace Pixelbadger.Toolkit.Components;

public sealed class SpectrePaintRenderer : IPaintRenderer
{
    private readonly IAnsiConsole _console;
    private readonly bool _interactive;
    private bool _frameSinceLastPrint;
    private bool _printed;

    public SpectrePaintRenderer(IAnsiConsole console, bool interactive)
    {
        _console = console;
        _interactive = interactive;
    }

    public void Begin()
    {
    }

    public void RenderFrame(PixelColor[] pixels, int size, string status)
    {
        if (!_interactive) return;
        var width = _console.Profile.Width;
        var d = PaintGrid.GridSize(width, _console.Profile.Height, size);
        _console.Clear();
        _console.Write(new PaintCanvasRenderable(CanvasDownscaler.Downscale(pixels, size, d), d));
        _console.Write(new Text(PaintGrid.Truncate(status, width)));
        _console.WriteLine();
        _frameSinceLastPrint = true;
    }

    public void LogToolCall(string description)
    {
        if (_interactive) return;
        _console.Write(new Text(description));
        _console.WriteLine();
    }

    public void End(PixelColor[] pixels, int size)
    {
        if (_printed) return;
        _printed = true;
        if (_interactive && _frameSinceLastPrint) return;
        var d = PaintGrid.FinalSize(_console.Profile.Width, size);
        _console.Write(new PaintCanvasRenderable(CanvasDownscaler.Downscale(pixels, size, d), d));
    }

    public void Dispose()
    {
    }
}
