namespace Pixelbadger.Toolkit.Components;

public class BoingBallComponent
{
    private static readonly PixelColor BackgroundColor = new(180, 180, 180);
    private static readonly PixelColor GridColor = new(120, 120, 120);
    private static readonly PixelColor BallRed = new(220, 40, 40);
    private static readonly PixelColor BallWhite = new(240, 240, 240);

    public void RenderFrame(PixelBuffer buffer, int frame)
    {
        DrawBackground(buffer);

        int radius = Math.Min(buffer.Width, buffer.Height) / 5;
        var (cx, cy, rotation) = ComputeBallState(buffer.Width, buffer.Height, radius, frame);

        DrawBall(buffer, cx, cy, radius, rotation);
    }

    internal static (int cx, int cy, double rotation) ComputeBallState(int width, int height, int radius, int frame)
    {
        // Bounce the ball off the walls using simple reflection
        int diameter = radius * 2;
        int rangeX = width - diameter;
        int rangeY = height - diameter;

        double speedX = 1.7;
        double speedY = 1.3;

        double posX = (frame * speedX) % (rangeX * 2.0);
        if (posX > rangeX) posX = rangeX * 2.0 - posX;

        double posY = (frame * speedY) % (rangeY * 2.0);
        if (posY > rangeY) posY = rangeY * 2.0 - posY;

        int cx = radius + (int)posX;
        int cy = radius + (int)posY;
        double rotation = frame * 0.1;

        return (cx, cy, rotation);
    }

    private static void DrawBackground(PixelBuffer buffer)
    {
        int gridSpacing = 8;

        for (int y = 0; y < buffer.Height; y++)
        {
            for (int x = 0; x < buffer.Width; x++)
            {
                bool onGrid = (x % gridSpacing == 0) || (y % gridSpacing == 0);
                buffer.SetPixel(x, y, onGrid ? GridColor : BackgroundColor);
            }
        }
    }

    private static void DrawBall(PixelBuffer buffer, int cx, int cy, int radius, double rotation)
    {
        int r2 = radius * radius;

        for (int y = cy - radius; y <= cy + radius; y++)
        {
            for (int x = cx - radius; x <= cx + radius; x++)
            {
                int dx = x - cx;
                int dy = y - cy;
                if (dx * dx + dy * dy > r2) continue;

                // Normalized coords in [-1, 1]
                double nx = (double)dx / radius;
                double ny = (double)dy / radius;

                // Rotate the checkerboard pattern
                double rx = nx * Math.Cos(rotation) - ny * Math.Sin(rotation);
                double ry = nx * Math.Sin(rotation) + ny * Math.Cos(rotation);

                int checkX = (int)Math.Floor(rx * 3.5 + 100);
                int checkY = (int)Math.Floor(ry * 3.5 + 100);
                bool isRed = (checkX + checkY) % 2 == 0;

                buffer.SetPixel(x, y, isRed ? BallRed : BallWhite);
            }
        }
    }
}
