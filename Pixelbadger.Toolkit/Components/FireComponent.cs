namespace Pixelbadger.Toolkit.Components;

// Classic IBM PC fire: random heat seeded on the bottom row, averaged upward with cooling.
public class FireComponent : IDemoEffect
{
    private static readonly PixelColor[] Palette = BuildPalette();
    private readonly Random _random = new(1337);
    private byte[] _heat = [];
    private int _width;
    private int _height;

    private static PixelColor[] BuildPalette()
    {
        var palette = new PixelColor[256];
        for (int i = 0; i < 256; i++)
        {
            byte r = (byte)Math.Min(255, i * 3);
            byte g = (byte)Math.Clamp((i - 85) * 3, 0, 255);
            byte b = (byte)Math.Clamp((i - 170) * 3, 0, 255);
            palette[i] = new PixelColor(r, g, b);
        }
        return palette;
    }

    public void RenderFrame(PixelBuffer buffer, int frame)
    {
        if (_width != buffer.Width || _height != buffer.Height)
        {
            _width = buffer.Width;
            _height = buffer.Height;
            _heat = new byte[_width * _height];
        }

        // Seed the bottom row with flickering embers
        for (int x = 0; x < _width; x++)
            _heat[(_height - 1) * _width + x] = (byte)(_random.Next(100) < 55 ? 255 : _random.Next(120));

        int cooling = Math.Max(1, 520 / _height);
        for (int y = 0; y < _height - 1; y++)
        {
            for (int x = 0; x < _width; x++)
            {
                int left = _heat[(y + 1) * _width + (x + _width - 1) % _width];
                int mid = _heat[(y + 1) * _width + x];
                int right = _heat[(y + 1) * _width + (x + 1) % _width];
                int below = _heat[Math.Min(y + 2, _height - 1) * _width + x];
                int value = (left + mid + right + below) * 100 / 400 - _random.Next(cooling + 1);
                _heat[y * _width + x] = (byte)Math.Max(0, value);
            }
        }

        for (int y = 0; y < _height; y++)
            for (int x = 0; x < _width; x++)
                buffer.SetPixel(x, y, Palette[_heat[y * _width + x]]);
    }
}
