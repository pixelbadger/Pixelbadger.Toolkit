namespace Pixelbadger.Toolkit.Components;

// Classic IBM PC plasma: summed sine fields indexed into a cycling palette.
public class PlasmaComponent : IDemoEffect
{
    private const int PaletteSize = 256;
    private static readonly PixelColor[] Palette = BuildPalette();

    private static PixelColor[] BuildPalette()
    {
        var palette = new PixelColor[PaletteSize];
        for (int i = 0; i < PaletteSize; i++)
        {
            double t = i * 2.0 * Math.PI / PaletteSize;
            palette[i] = new PixelColor(
                (byte)(127.5 + 127.5 * Math.Sin(t)),
                (byte)(127.5 + 127.5 * Math.Sin(t + 2.0944)),
                (byte)(127.5 + 127.5 * Math.Sin(t + 4.1888)));
        }
        return palette;
    }

    public void RenderFrame(PixelBuffer buffer, int frame)
    {
        double t = frame * 0.07;

        for (int y = 0; y < buffer.Height; y++)
        {
            double fy = y / 8.0;
            for (int x = 0; x < buffer.Width; x++)
            {
                double fx = x / 8.0;
                double v = Math.Sin(fx + t)
                         + Math.Sin(fy * 1.3 - t * 1.1)
                         + Math.Sin((fx + fy + t) * 0.7)
                         + Math.Sin(Math.Sqrt(fx * fx + fy * fy) - t * 1.5);
                int index = (int)((v + 4.0) / 8.0 * (PaletteSize - 1)) & (PaletteSize - 1);
                buffer.SetPixel(x, y, Palette[index]);
            }
        }
    }
}
