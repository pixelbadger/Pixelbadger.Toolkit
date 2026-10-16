using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.PixelFormats;

namespace Pixelbadger.Toolkit.Components;

/// <summary>Collects canvas snapshots (bounded in memory) and encodes them as an infinitely looping animated GIF.</summary>
public sealed class PaintAnimationRecorder
{
    public const int MaxFrames = 500;
    public const int FrameDelayMs = 150;
    public const int FinalFrameDelayMs = 2000;

    private readonly int _size;
    private readonly List<PixelColor[]> _frames = new();
    private int _stride = 1;
    private int _sinceKept;

    public PaintAnimationRecorder(int size)
    {
        if (size <= 0) throw new ArgumentOutOfRangeException(nameof(size));
        _size = size;
    }

    /// <summary>Frames currently kept.</summary>
    public int FrameCount => _frames.Count;

    /// <summary>Copies <paramref name="pixels"/> as a frame, applying the current stride and the memory bound.</summary>
    public void AddFrame(PixelColor[] pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (pixels.Length != _size * _size)
            throw new ArgumentException($"Expected {_size * _size} pixels.", nameof(pixels));

        if (_frames.Count > 0 && ++_sinceKept < _stride)
            return;

        _sinceKept = 0;
        _frames.Add((PixelColor[])pixels.Clone());

        if (_frames.Count > MaxFrames)
        {
            var kept = new List<PixelColor[]>(_frames.Count / 2 + 1);
            for (var i = 0; i < _frames.Count; i += 2)
                kept.Add(_frames[i]);
            _frames.Clear();
            _frames.AddRange(kept);
            _stride *= 2;
        }
    }

    /// <summary>Encodes the kept frames, followed by <paramref name="finalPixels"/> unless it equals the last kept frame.</summary>
    public byte[] ToGif(PixelColor[] finalPixels)
    {
        ArgumentNullException.ThrowIfNull(finalPixels);
        if (finalPixels.Length != _size * _size)
            throw new ArgumentException($"Expected {_size * _size} pixels.", nameof(finalPixels));

        var frames = new List<PixelColor[]>(_frames);
        if (frames.Count == 0 || !frames[^1].AsSpan().SequenceEqual(finalPixels))
            frames.Add(finalPixels);

        using var image = new Image<Rgb24>(_size, _size);
        for (var i = 0; i < frames.Count; i++)
        {
            Image<Rgb24>? extra = null;
            ImageFrame<Rgb24> frame;
            if (i == 0)
            {
                frame = image.Frames.RootFrame;
            }
            else
            {
                extra = new Image<Rgb24>(_size, _size);
                frame = extra.Frames.RootFrame;
            }

            Fill(frame, frames[i]);
            frame.Metadata.GetGifMetadata().FrameDelay =
                (i == frames.Count - 1 ? FinalFrameDelayMs : FrameDelayMs) / 10;

            if (extra is not null)
            {
                image.Frames.AddFrame(frame);
                extra.Dispose();
            }
        }

        image.Metadata.GetGifMetadata().RepeatCount = 0;

        using var ms = new MemoryStream();
        image.SaveAsGif(ms, new GifEncoder { ColorTableMode = FrameColorTableMode.Local });
        return ms.ToArray();
    }

    private void Fill(ImageFrame<Rgb24> frame, PixelColor[] pixels)
    {
        for (var y = 0; y < _size; y++)
        for (var x = 0; x < _size; x++)
        {
            var c = pixels[y * _size + x];
            frame[x, y] = new Rgb24(c.R, c.G, c.B);
        }
    }
}
