using FluentAssertions;
using Pixelbadger.Toolkit.Components;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.PixelFormats;

namespace Pixelbadger.Toolkit.Tests;

public class PaintAnimationRecorderTests
{
    private const int Size = 4;

    // Distinct pure colour per index: red channel = index % 256, green = index / 256.
    private static PixelColor[] Solid(int index)
        => Enumerable.Repeat(new PixelColor((byte)(index % 256), (byte)(index / 256), 0), Size * Size).ToArray();

    private static Rgb24 Rgb(int index) => new((byte)(index % 256), (byte)(index / 256), 0);

    private static (List<Rgb24> Pixels, List<int> Delays, int Repeat, int Width, int Height) Decode(byte[] gif)
    {
        using var image = Image.Load<Rgb24>(gif);
        return (
            image.Frames.Select(f => f[Size - 1, Size - 1]).ToList(),
            image.Frames.Select(f => f.Metadata.GetGifMetadata().FrameDelay).ToList(),
            image.Metadata.GetGifMetadata().RepeatCount,
            image.Width,
            image.Height);
    }

    [Fact]
    public void Constants_ShouldMatchSpecification()
    {
        PaintAnimationRecorder.MaxFrames.Should().Be(500);
        PaintAnimationRecorder.FrameDelayMs.Should().Be(150);
        PaintAnimationRecorder.FinalFrameDelayMs.Should().Be(2000);
    }

    [Fact]
    public void ToGif_ShouldEncodeFramesInOrderWithDelaysAndInfiniteLoop_WhenFinalDiffers()
    {
        var recorder = new PaintAnimationRecorder(Size);
        recorder.AddFrame(Solid(1));
        recorder.AddFrame(Solid(2));
        recorder.AddFrame(Solid(3));

        var decoded = Decode(recorder.ToGif(Solid(4)));

        decoded.Pixels.Should().Equal(Rgb(1), Rgb(2), Rgb(3), Rgb(4));
        decoded.Delays.Should().Equal(15, 15, 15, 200);
        decoded.Repeat.Should().Be(0);
        (decoded.Width, decoded.Height).Should().Be((Size, Size));
        recorder.FrameCount.Should().Be(3);
    }

    [Fact]
    public void ToGif_ShouldNotAppendFinalFrame_WhenIdenticalToLastKeptFrame()
    {
        var recorder = new PaintAnimationRecorder(Size);
        recorder.AddFrame(Solid(1));
        recorder.AddFrame(Solid(2));

        var decoded = Decode(recorder.ToGif(Solid(2)));

        decoded.Pixels.Should().Equal(Rgb(1), Rgb(2));
        decoded.Delays.Should().Equal(15, 200);
    }

    [Fact]
    public void ToGif_ShouldEncodeOnlyFinalFrame_WhenNoFramesAdded()
    {
        var decoded = Decode(new PaintAnimationRecorder(Size).ToGif(Solid(7)));

        decoded.Pixels.Should().Equal(Rgb(7));
        decoded.Delays.Should().Equal(200);
    }

    [Fact]
    public void AddFrame_ShouldCopyPixels_WhenCallerMutatesArrayAfterwards()
    {
        var recorder = new PaintAnimationRecorder(Size);
        var pixels = Solid(1);
        recorder.AddFrame(pixels);
        Array.Fill(pixels, new PixelColor(9, 9, 9));

        Decode(recorder.ToGif(Solid(1))).Pixels.Should().Equal(Rgb(1));
    }

    [Fact]
    public void AddFrame_ShouldThrow_WhenPixelCountWrong()
    {
        var act = () => new PaintAnimationRecorder(Size).AddFrame(new PixelColor[3]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddFrame_ShouldKeepAllFrames_WhenExactlyMaxFramesAdded()
    {
        var recorder = new PaintAnimationRecorder(Size);
        for (var i = 0; i < 500; i++) recorder.AddFrame(Solid(i));

        recorder.FrameCount.Should().Be(500);
    }

    [Fact]
    public void AddFrame_ShouldDropEverySecondFrame_WhenFiveHundredAndFirstAdded()
    {
        var recorder = new PaintAnimationRecorder(Size);
        for (var i = 0; i <= 500; i++) recorder.AddFrame(Solid(i));

        recorder.FrameCount.Should().Be(251);
        var decoded = Decode(recorder.ToGif(Solid(500)));
        decoded.Pixels.Should().Equal(Enumerable.Range(0, 251).Select(k => Rgb(k * 2)));
    }

    [Fact]
    public void AddFrame_ShouldRecordEverySecondFrame_AfterFirstDecimation()
    {
        var recorder = new PaintAnimationRecorder(Size);
        for (var i = 0; i <= 510; i++) recorder.AddFrame(Solid(i));

        // 0,2,...,500 kept, then 502,504,...,510 (stride 2).
        recorder.FrameCount.Should().Be(256);
        var decoded = Decode(recorder.ToGif(Solid(510)));
        decoded.Pixels.Should().Equal(Enumerable.Range(0, 256).Select(k => Rgb(k * 2)));
    }

    [Fact]
    public void AddFrame_ShouldDoubleStrideAgain_WhenBoundHitASecondTime()
    {
        var recorder = new PaintAnimationRecorder(Size);
        // Stride 2 fills back up to 501 kept frames at offered index 1000, then decimates to 251 (0,4,...,1000), stride 4.
        for (var i = 0; i <= 1000; i++) recorder.AddFrame(Solid(i));
        recorder.FrameCount.Should().Be(251);
        var decoded = Decode(recorder.ToGif(Solid(1000)));
        // Colours wrap at 256 in the red channel but green encodes i / 256, so indices are unique below 65536.
        decoded.Pixels.Should().Equal(Enumerable.Range(0, 251).Select(k => Rgb(k * 4)));

        for (var i = 1001; i <= 1008; i++) recorder.AddFrame(Solid(i));
        Decode(recorder.ToGif(Solid(1008))).Pixels.Should().Equal(
            Enumerable.Range(0, 251).Select(k => Rgb(k * 4)).Concat(new[] { Rgb(1004), Rgb(1008) }));
    }

    [Fact]
    public void ToGif_ShouldAlwaysEndWithFinalCanvas_WhenDecimationDroppedLastPaintingFrame()
    {
        var recorder = new PaintAnimationRecorder(Size);
        for (var i = 0; i <= 501; i++) recorder.AddFrame(Solid(i)); // 501 skipped by stride 2

        var decoded = Decode(recorder.ToGif(Solid(501)));

        decoded.Pixels[^1].Should().Be(Rgb(501));
        decoded.Pixels.Should().HaveCount(252);
        decoded.Delays[^1].Should().Be(200);
    }
}
