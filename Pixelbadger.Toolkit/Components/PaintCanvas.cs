using System.Globalization;
using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Pixelbadger.Toolkit.Components;

/// <summary>A 256x256 RGB canvas, initially white, origin top-left.</summary>
public class PaintCanvas
{
    public const int Size = 256;
    public const int MaxPixelsPerCall = 4096;
    public const int MaxThickness = 16;
    public const int MaxRegionSide = 32;
    private const int MaxCoordinate = 1_000_000;

    private readonly PixelColor[] _pixels = new PixelColor[Size * Size];

    public PaintCanvas()
    {
        Array.Fill(_pixels, PixelColor.White);
    }

    public PixelColor GetPixel(int x, int y)
    {
        if (x < 0 || x >= Size || y < 0 || y >= Size)
            throw new ArgumentOutOfRangeException(nameof(x), $"Pixel ({x},{y}) is outside the {Size}x{Size} canvas.");
        return _pixels[y * Size + x];
    }

    public PixelColor[] Snapshot() => (PixelColor[])_pixels.Clone();

    public int SetPixels(IReadOnlyList<(int X, int Y, PixelColor Color)> pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (pixels.Count > MaxPixelsPerCall)
            throw new ArgumentException($"Too many pixels: {pixels.Count}. At most {MaxPixelsPerCall} per call.", nameof(pixels));

        var painted = new bool[Size * Size];
        var count = 0;
        foreach (var (x, y, color) in pixels)
        {
            if (!InCanvas(x, y)) continue;
            var i = y * Size + x;
            _pixels[i] = color;
            if (!painted[i]) { painted[i] = true; count++; }
        }
        return count;
    }

    public int FillRect(int x, int y, int width, int height, PixelColor color)
    {
        if (width < 1 || height < 1)
            throw new ArgumentException($"Width and height must be at least 1 (got {width}x{height}).");

        var x0 = Math.Max(0L, x);
        var y0 = Math.Max(0L, y);
        var x1 = Math.Min((long)Size, (long)x + width);
        var y1 = Math.Min((long)Size, (long)y + height);
        var count = 0;
        for (var py = y0; py < y1; py++)
            for (var px = x0; px < x1; px++)
            {
                _pixels[py * Size + px] = color;
                count++;
            }
        return count;
    }

    public int DrawLine(int x0, int y0, int x1, int y1, PixelColor color, int thickness = 1)
    {
        if (thickness < 1 || thickness > MaxThickness)
            throw new ArgumentException($"Thickness must be between 1 and {MaxThickness} (got {thickness}).");
        if (Math.Abs((long)x0) > MaxCoordinate || Math.Abs((long)y0) > MaxCoordinate ||
            Math.Abs((long)x1) > MaxCoordinate || Math.Abs((long)y1) > MaxCoordinate)
            throw new ArgumentException($"Line coordinates must be within +/-{MaxCoordinate}.");

        var painted = new bool[Size * Size];
        var count = 0;
        var offset = (thickness - 1) / 2;

        var dx = Math.Abs(x1 - x0);
        var dy = -Math.Abs(y1 - y0);
        var sx = x0 < x1 ? 1 : -1;
        var sy = y0 < y1 ? 1 : -1;
        var err = dx + dy;
        var px = x0;
        var py = y0;
        while (true)
        {
            var left = px - offset;
            var top = py - offset;
            for (var yy = Math.Max(0, top); yy < Math.Min(Size, top + thickness); yy++)
                for (var xx = Math.Max(0, left); xx < Math.Min(Size, left + thickness); xx++)
                {
                    var i = yy * Size + xx;
                    _pixels[i] = color;
                    if (!painted[i]) { painted[i] = true; count++; }
                }

            if (px == x1 && py == y1) break;
            var e2 = 2 * err;
            if (e2 >= dy) { err += dy; px += sx; }
            if (e2 <= dx) { err += dx; py += sy; }
        }
        return count;
    }

    public int FillEllipse(int cx, int cy, int rx, int ry, PixelColor color)
    {
        if (rx < 0 || ry < 0)
            throw new ArgumentException($"Radii must be non-negative (got rx={rx}, ry={ry}).");

        var ax = rx + 0.5;
        var ay = ry + 0.5;
        var xMin = (int)Math.Max(0L, (long)cx - rx);
        var xMax = (int)Math.Min(Size - 1L, (long)cx + rx);
        var yMin = (int)Math.Max(0L, (long)cy - ry);
        var yMax = (int)Math.Min(Size - 1L, (long)cy + ry);
        var count = 0;
        for (var y = yMin; y <= yMax; y++)
            for (var x = xMin; x <= xMax; x++)
            {
                var nx = (x - (double)cx) / ax;
                var ny = (y - (double)cy) / ay;
                if (nx * nx + ny * ny <= 1.0)
                {
                    _pixels[y * Size + x] = color;
                    count++;
                }
            }
        return count;
    }

    public string ReadRegion(int x, int y, int width, int height)
    {
        if (width < 1 || width > MaxRegionSide || height < 1 || height > MaxRegionSide)
            throw new ArgumentException($"Region width and height must be between 1 and {MaxRegionSide} (got {width}x{height}).");
        if (x < 0 || y < 0 || (long)x + width > Size || (long)y + height > Size)
            throw new ArgumentException($"Region x={x} y={y} width={width} height={height} must lie fully inside the {Size}x{Size} canvas.");

        var sb = new StringBuilder();
        sb.Append($"Region x={x} y={y} width={width} height={height} (rows top to bottom, pixels left to right)");
        for (var row = y; row < y + height; row++)
        {
            sb.Append('\n').Append("y=").Append(row).Append(':');
            for (var col = x; col < x + width; col++)
                sb.Append(' ').Append(FormatColor(_pixels[row * Size + col]));
        }
        return sb.ToString();
    }

    public byte[] ToPng()
    {
        using var image = new Image<Rgb24>(Size, Size);
        for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
            {
                var c = _pixels[y * Size + x];
                image[x, y] = new Rgb24(c.R, c.G, c.B);
            }
        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }

    public static bool TryParseColor(string? text, out PixelColor color)
    {
        color = default;
        if (text is null) return false;
        var s = text.StartsWith('#') ? text[1..] : text;
        if (s.Length != 6) return false;
        foreach (var ch in s)
            if (!Uri.IsHexDigit(ch)) return false;
        color = new PixelColor(
            byte.Parse(s.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(s.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(s.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        return true;
    }

    public static string FormatColor(PixelColor color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static bool InCanvas(int x, int y) => x >= 0 && x < Size && y >= 0 && y < Size;
}
