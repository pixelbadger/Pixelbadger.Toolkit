namespace Pixelbadger.Toolkit.Components;

/// <summary>Which channels lit at least one dot in a cell.</summary>
[Flags]
public enum ScopeChannels
{
    None = 0,
    Left = 1,
    Right = 2,
    Both = Left | Right,
}

/// <summary>A rendered oscilloscope: braille text rows plus per-cell channel flags for colouring.</summary>
public sealed class OscilloscopeFrame
{
    public OscilloscopeFrame(string[] rows, ScopeChannels[,] cellChannels)
    {
        Rows = rows;
        CellChannels = cellChannels;
    }

    /// <summary>Exactly Height strings of exactly Width braille characters (U+2800 to U+28FF).</summary>
    public string[] Rows { get; }

    /// <summary>Channels lit per cell, indexed [row, column].</summary>
    public ScopeChannels[,] CellChannels { get; }

    public int Height => Rows.Length;

    public int Width => Rows.Length == 0 ? 0 : Rows[0].Length;
}

/// <summary>Pure braille oscilloscope renderer: 2x4 dots per terminal cell, continuous traces.</summary>
public static class OscilloscopeRenderer
{
    private static readonly int[,] DotBits =
    {
        { 0x01, 0x02, 0x04, 0x40 },
        { 0x08, 0x10, 0x20, 0x80 },
    };

    public static OscilloscopeFrame Render(ReadOnlySpan<float> left, ReadOnlySpan<float> right, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

        var dotCols = width * 2;
        var dotRows = height * 4;
        var leftBits = new byte[height, width];
        var rightBits = new byte[height, width];
        Plot(left, dotCols, dotRows, leftBits);
        Plot(right, dotCols, dotRows, rightBits);

        var rows = new string[height];
        var channels = new ScopeChannels[height, width];
        var buffer = new char[width];
        for (var r = 0; r < height; r++)
        {
            for (var c = 0; c < width; c++)
            {
                buffer[c] = (char)(0x2800 + (leftBits[r, c] | rightBits[r, c]));
                var flags = ScopeChannels.None;
                if (leftBits[r, c] != 0) flags |= ScopeChannels.Left;
                if (rightBits[r, c] != 0) flags |= ScopeChannels.Right;
                channels[r, c] = flags;
            }

            rows[r] = new string(buffer);
        }

        return new OscilloscopeFrame(rows, channels);
    }

    private static void Plot(ReadOnlySpan<float> samples, int dotCols, int dotRows, byte[,] bits)
    {
        var previous = -1;
        for (var x = 0; x < dotCols; x++)
        {
            var y = RowFor(Sample(samples, x, dotCols), dotRows);
            var from = previous < 0 ? y : previous;
            var lo = Math.Min(from, y);
            var hi = Math.Max(from, y);
            for (var dy = lo; dy <= hi; dy++)
                bits[dy / 4, x / 2] |= (byte)DotBits[x % 2, dy % 4];
            previous = y;
        }
    }

    private static float Sample(ReadOnlySpan<float> samples, int column, int dotCols)
    {
        if (samples.Length == 0) return 0f;
        if (samples.Length == 1 || dotCols == 1) return samples[0];
        var index = (int)Math.Round((double)column * (samples.Length - 1) / (dotCols - 1));
        return samples[index];
    }

    private static int RowFor(float v, int dotRows)
    {
        if (float.IsNaN(v)) v = 0f;
        v = Math.Clamp(v, -1f, 1f);
        return (int)Math.Round((1.0 - v) / 2.0 * (dotRows - 1), MidpointRounding.AwayFromZero);
    }
}
