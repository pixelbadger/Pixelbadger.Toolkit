namespace Pixelbadger.Toolkit.Components;

// Classic Amiga copper bars: gradient raster bars drawn per scanline (as the Copper
// would reprogram the background colour), weaving in front of and behind each other.
public class CopperBarsComponent : IDemoEffect
{
    private static readonly (double R, double G, double B)[] BarHues =
    [
        (1.0, 0.25, 0.1),
        (0.2, 0.6, 1.0),
        (0.3, 1.0, 0.3),
        (1.0, 0.8, 0.1),
        (0.9, 0.2, 1.0),
    ];

    public void RenderFrame(PixelBuffer buffer, int frame)
    {
        // Dark blue vertical gradient backdrop
        for (int y = 0; y < buffer.Height; y++)
        {
            byte shade = (byte)(8 + 24 * y / buffer.Height);
            var row = new PixelColor(0, 0, shade);
            for (int x = 0; x < buffer.Width; x++)
                buffer.SetPixel(x, y, row);
        }

        int barHeight = Math.Max(buffer.Height / 6, 6);
        var order = Enumerable.Range(0, BarHues.Length)
            .Select(i => (Index: i, Depth: Math.Cos(frame * 0.045 + i * 2.0 * Math.PI / BarHues.Length)))
            .OrderBy(b => b.Depth);

        foreach (var (index, _) in order)
        {
            double sine = Math.Sin(frame * 0.045 + index * 2.0 * Math.PI / BarHues.Length);
            int center = (int)((sine + 1.0) / 2.0 * (buffer.Height - 1));
            var (hr, hg, hb) = BarHues[index];

            for (int dy = -barHeight / 2; dy <= barHeight / 2; dy++)
            {
                int y = center + dy;
                if (y < 0 || y >= buffer.Height) continue;

                // Metallic copper gradient: dark edge, bright highlight at the middle
                double t = 1.0 - Math.Abs(dy) / (barHeight / 2.0 + 1.0);
                double light = Math.Pow(t, 1.5);
                double highlight = Math.Pow(t, 6.0) * 0.6;
                var color = new PixelColor(
                    ToByte(hr * light + highlight),
                    ToByte(hg * light + highlight),
                    ToByte(hb * light + highlight));

                for (int x = 0; x < buffer.Width; x++)
                    buffer.SetPixel(x, y, color);
            }
        }
    }

    private static byte ToByte(double v) => (byte)(Math.Clamp(v, 0.0, 1.0) * 255);
}
