using System.Text;
using Spectre.Console;

namespace Pixelbadger.Toolkit.Components;

public sealed class DeltaAnsiPaintRenderer : IPaintRenderer
{
    private const string Esc = "\u001b";
    private readonly IAnsiConsole _console;
    private (PixelColor Top, PixelColor Bottom)[]? _previous;
    private int _prevWidth = -1;
    private int _prevHeight = -1;
    private int _prevGrid = -1;
    private PixelColor[]? _lastPixels;
    private int _lastSize;
    private bool _begun;
    private bool _ended;

    public DeltaAnsiPaintRenderer(IAnsiConsole console)
    {
        _console = console;
    }

    public PaintFrameStats? LastFrameStats { get; private set; }

    private TextWriter Out => _console.Profile.Out.Writer;

    public void Begin()
    {
        _begun = true;
        _ended = false;
        _previous = null;
        Out.Write($"{Esc}[?1049h{Esc}[?25l{Esc}[?7l{Esc}[2J{Esc}[H");
        Out.Flush();
    }

    public void RenderFrame(PixelColor[] pixels, int size, string status)
    {
        _lastPixels = pixels;
        _lastSize = size;
        var width = _console.Profile.Width;
        var height = _console.Profile.Height;
        var d = PaintGrid.GridSize(width, height, size);
        var grid = CanvasDownscaler.Downscale(pixels, size, d);
        var rows = d / 2;

        var full = _previous is null || width != _prevWidth || height != _prevHeight || d != _prevGrid;
        var sb = new StringBuilder();
        var changed = 0;
        var current = new (PixelColor Top, PixelColor Bottom)[rows * d];
        PixelColor? fg = null, bg = null;
        int lastRow = -1, lastCol = -1;

        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < d; c++)
            {
                var top = grid[2 * r * d + c];
                var bottom = grid[(2 * r + 1) * d + c];
                var cell = (top, bottom);
                current[r * d + c] = cell;
                if (!full && _previous![r * d + c] == cell) continue;

                changed++;
                if (lastRow != r || lastCol != c - 1)
                    sb.Append($"{Esc}[{r + 1};{c + 1}H");

                if (top == bottom)
                {
                    if (bg != bottom)
                    {
                        sb.Append($"{Esc}[48;2;{bottom.R};{bottom.G};{bottom.B}m");
                        bg = bottom;
                    }
                    sb.Append(' ');
                }
                else
                {
                    var parts = new List<string>(2);
                    if (fg != top) { parts.Add($"38;2;{top.R};{top.G};{top.B}"); fg = top; }
                    if (bg != bottom) { parts.Add($"48;2;{bottom.R};{bottom.G};{bottom.B}"); bg = bottom; }
                    if (parts.Count > 0)
                        sb.Append($"{Esc}[{string.Join(';', parts)}m");
                    sb.Append('▀');
                }
                lastRow = r;
                lastCol = c;
            }
        }

        if (changed > 0 || full)
        {
            var body = sb.ToString();
            var frame = $"{Esc}[?2026h{(full ? $"{Esc}[2J" : string.Empty)}{body}{Esc}[?2026l";
            Out.Write(frame);
            LastFrameStats = new PaintFrameStats(changed, Encoding.UTF8.GetByteCount(frame));
        }
        else
        {
            LastFrameStats = new PaintFrameStats(0, 0);
        }

        _previous = current;
        _prevWidth = width;
        _prevHeight = height;
        _prevGrid = d;

        var text = PaintGrid.Truncate(status, width);
        Out.Write($"{Esc}[{rows + 1};1H{Esc}[0m{text.PadRight(width)}");
        Out.Flush();
    }

    public void LogToolCall(string description)
    {
    }

    public void End(PixelColor[] pixels, int size)
    {
        if (_ended) return;
        _ended = true;
        Restore();
        WriteFinalFrame(pixels, size);
    }

    public void Dispose()
    {
        if (!_begun || _ended) return;
        _ended = true;
        Restore();
        if (_lastPixels is not null)
            WriteFinalFrame(_lastPixels, _lastSize);
    }

    private void Restore()
    {
        Out.Write($"{Esc}[0m{Esc}[?7h{Esc}[?25h{Esc}[?1049l");
        Out.Flush();
    }

    private void WriteFinalFrame(PixelColor[] pixels, int size)
    {
        var d = PaintGrid.FinalSize(_console.Profile.Width, size);
        _console.Write(new PaintCanvasRenderable(CanvasDownscaler.Downscale(pixels, size, d), d));
        Out.Flush();
    }
}
