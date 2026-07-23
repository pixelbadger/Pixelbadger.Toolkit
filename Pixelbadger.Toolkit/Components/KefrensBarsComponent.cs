namespace Pixelbadger.Toolkit.Components;

public class KefrensBarsComponent
{
    private static readonly PixelColor Background = new(10, 10, 20);

    private static readonly PixelColor[] BarColors =
    [
        new(255, 60, 60),
        new(255, 160, 40),
        new(220, 220, 40),
        new(60, 220, 60),
        new(40, 200, 220),
        new(60, 100, 255),
        new(180, 60, 255),
        new(255, 60, 180),
    ];

    public void RenderFrame(PixelBuffer buffer, int frame)
    {
        buffer.Clear(Background);

        int numBars = BarColors.Length;
        int barHalfWidth = Math.Max(buffer.Width / 10, 4);
        double omega = 0.04;

        for (int b = 0; b < numBars; b++)
        {
            double phase = b * (2.0 * Math.PI / numBars);
            double sineValue = Math.Sin(frame * omega + phase);
            int centerX = (int)((sineValue + 1.0) / 2.0 * (buffer.Width - 1));

            var color = BarColors[b];

            for (int x = centerX - barHalfWidth; x <= centerX + barHalfWidth; x++)
            {
                if (x < 0 || x >= buffer.Width) continue;

                // Brightness falloff from center
                double distance = Math.Abs(x - centerX);
                double brightness = 1.0 - distance / (barHalfWidth + 1.0);
                byte r = (byte)(color.R * brightness);
                byte g = (byte)(color.G * brightness);
                byte bVal = (byte)(color.B * brightness);
                var pixelColor = new PixelColor(r, g, bVal);

                for (int y = 0; y < buffer.Height; y++)
                    buffer.SetPixel(x, y, pixelColor);
            }
        }
    }
}
