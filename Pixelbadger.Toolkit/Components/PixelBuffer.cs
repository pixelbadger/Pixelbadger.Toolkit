namespace Pixelbadger.Toolkit.Components;

public readonly struct PixelColor(byte r, byte g, byte b)
{
    public byte R { get; } = r;
    public byte G { get; } = g;
    public byte B { get; } = b;

    public static readonly PixelColor Black = new(0, 0, 0);
    public static readonly PixelColor White = new(255, 255, 255);
    public static readonly PixelColor Red = new(255, 0, 0);

    public bool Equals(PixelColor other) => R == other.R && G == other.G && B == other.B;
    public override bool Equals(object? obj) => obj is PixelColor other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(R, G, B);
    public static bool operator ==(PixelColor a, PixelColor b) => a.Equals(b);
    public static bool operator !=(PixelColor a, PixelColor b) => !a.Equals(b);
}

public class PixelBuffer
{
    private readonly PixelColor[] _pixels;

    public int Width { get; }
    public int Height { get; }

    public PixelBuffer(int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

        Width = width;
        Height = height + (height % 2); // ensure even height for half-block rendering
        _pixels = new PixelColor[Width * Height];
    }

    public void SetPixel(int x, int y, PixelColor color)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height) return;
        _pixels[y * Width + x] = color;
    }

    public PixelColor GetPixel(int x, int y)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
            throw new ArgumentOutOfRangeException($"Pixel ({x},{y}) is out of bounds for {Width}x{Height} buffer.");
        return _pixels[y * Width + x];
    }

    public void Clear(PixelColor color = default)
    {
        for (int i = 0; i < _pixels.Length; i++)
            _pixels[i] = color;
    }

    // Returns an ANSI string rendering the pixel buffer using ▀ (U+2580 UPPER HALF BLOCK).
    // Each terminal row represents 2 pixel rows: top pixel = foreground, bottom pixel = background.
    public string Render()
    {
        var sb = new System.Text.StringBuilder(Width * (Height / 2) * 40);
        int terminalRows = Height / 2;

        for (int row = 0; row < terminalRows; row++)
        {
            for (int x = 0; x < Width; x++)
            {
                var top = _pixels[row * 2 * Width + x];
                var bottom = _pixels[(row * 2 + 1) * Width + x];

                // foreground = top pixel, background = bottom pixel
                sb.Append($"\x1b[38;2;{top.R};{top.G};{top.B}m\x1b[48;2;{bottom.R};{bottom.G};{bottom.B}m▀");
            }
            sb.Append("\x1b[0m\n");
        }

        return sb.ToString();
    }
}
