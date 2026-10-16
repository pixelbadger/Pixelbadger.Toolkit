using System.Text;
using Spectre.Console;
using Spectre.Console.Rendering;

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

internal static class PaintGrid
{
    // D = min(256, width, (height - 2) * 2), rounded down to even, minimum 2, never above the source size.
    public static int GridSize(int width, int height, int sourceSize)
    {
        var d = Math.Min(Math.Min(256, width), (height - 2) * 2);
        d = Math.Min(d, sourceSize);
        d -= d % 2;
        return Math.Max(2, d);
    }

    // min(256, width) rounded down to even, minimum 2, never above the source size.
    public static int FinalSize(int width, int sourceSize)
    {
        var d = Math.Min(Math.Min(256, width), sourceSize);
        d -= d % 2;
        return Math.Max(2, d);
    }

    public static string Truncate(string status, int width)
    {
        var clean = new StringBuilder();
        foreach (var ch in status ?? string.Empty)
            clean.Append(char.IsControl(ch) ? ' ' : ch);
        var s = clean.ToString();
        return s.Length > width ? s[..width] : s;
    }
}

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

public static class CanvasDownscaler
{
    // Area average (box filter) with integer bounds: target pixel i covers source
    // floor(i*s/t) .. floor((i+1)*s/t)-1 on each axis. Channels are averaged and rounded half up.
    public static PixelColor[] Downscale(PixelColor[] source, int sourceSize, int targetSize)
    {
        if (targetSize < 1 || targetSize > sourceSize)
            throw new ArgumentOutOfRangeException(nameof(targetSize));
        if (source.Length != sourceSize * sourceSize)
            throw new ArgumentException("Source length must equal sourceSize * sourceSize.", nameof(source));

        if (targetSize == sourceSize) return (PixelColor[])source.Clone();

        var result = new PixelColor[targetSize * targetSize];
        for (var ty = 0; ty < targetSize; ty++)
        {
            var y0 = ty * sourceSize / targetSize;
            var y1 = (ty + 1) * sourceSize / targetSize;
            for (var tx = 0; tx < targetSize; tx++)
            {
                var x0 = tx * sourceSize / targetSize;
                var x1 = (tx + 1) * sourceSize / targetSize;
                long r = 0, g = 0, b = 0;
                var n = (y1 - y0) * (x1 - x0);
                for (var y = y0; y < y1; y++)
                    for (var x = x0; x < x1; x++)
                    {
                        var p = source[y * sourceSize + x];
                        r += p.R; g += p.G; b += p.B;
                    }
                result[ty * targetSize + tx] = new PixelColor(
                    (byte)((r + n / 2) / n), (byte)((g + n / 2) / n), (byte)((b + n / 2) / n));
            }
        }
        return result;
    }
}

public sealed class PaintCanvasRenderable : IRenderable
{
    private readonly PixelColor[] _pixels;
    private readonly int _size;

    public PaintCanvasRenderable(PixelColor[] pixels, int size)
    {
        _pixels = pixels;
        _size = size;
    }

    public Measurement Measure(RenderOptions options, int maxWidth) => new(_size, _size);

    public IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
    {
        for (var r = 0; r < _size / 2; r++)
        {
            for (var c = 0; c < _size; c++)
            {
                var top = _pixels[2 * r * _size + c];
                var bottom = _pixels[(2 * r + 1) * _size + c];
                yield return new Segment("▀",
                    new Style(new Color(top.R, top.G, top.B), new Color(bottom.R, bottom.G, bottom.B)));
            }
            yield return Segment.LineBreak;
        }
    }
}
