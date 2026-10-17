using Spectre.Console;
using Spectre.Console.Rendering;

namespace Pixelbadger.Toolkit.Components;

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
