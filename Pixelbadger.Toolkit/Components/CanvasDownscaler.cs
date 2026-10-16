using System.Text;

namespace Pixelbadger.Toolkit.Components;

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
