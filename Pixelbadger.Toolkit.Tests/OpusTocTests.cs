using FluentAssertions;
using Pixelbadger.Toolkit.Opus.Ogg;

namespace Pixelbadger.Toolkit.Tests;

public class OpusTocTests
{
    // Frame duration in 48 kHz samples for every config 0-31 (RFC 6716 table 2).
    private static readonly int[] FrameSamples =
    {
        480, 960, 1920, 2880,   // 0-3   SILK NB
        480, 960, 1920, 2880,   // 4-7   SILK MB
        480, 960, 1920, 2880,   // 8-11  SILK WB
        480, 960,               // 12-13 Hybrid SWB
        480, 960,               // 14-15 Hybrid FB
        120, 240, 480, 960,     // 16-19 CELT NB
        120, 240, 480, 960,     // 20-23 CELT WB
        120, 240, 480, 960,     // 24-27 CELT SWB
        120, 240, 480, 960      // 28-31 CELT FB
    };

    public static IEnumerable<object[]> AllConfigsAndCodes()
    {
        for (var config = 0; config < 32; config++)
            for (var code = 0; code < 4; code++)
                yield return new object[] { config, code };
    }

    [Theory]
    [MemberData(nameof(AllConfigsAndCodes))]
    public void GetSampleCount_ShouldMatchRfcTable_WhenAnyConfigAndCodeGiven(int config, int code)
    {
        var frame = FrameSamples[config];
        var toc = (byte)((config << 3) | code);
        // Stereo bit (0x04) must not affect duration.
        var tocStereo = (byte)(toc | 0x04);

        switch (code)
        {
            case 0:
                OpusToc.GetSampleCount(new[] { toc, (byte)1 }).Should().Be(frame);
                OpusToc.GetSampleCount(new[] { tocStereo, (byte)1 }).Should().Be(frame);
                break;
            case 1:
            case 2:
                OpusToc.GetSampleCount(new[] { toc, (byte)1, (byte)1 }).Should().Be(2 * frame);
                OpusToc.GetSampleCount(new[] { tocStereo, (byte)1, (byte)1 }).Should().Be(2 * frame);
                break;
            default:
                OpusToc.GetSampleCount(new[] { toc, (byte)1 }).Should().Be(frame);
                var maxFrames = 5760 / frame;
                OpusToc.GetSampleCount(new[] { toc, (byte)(maxFrames | 0x80 | 0x40) }).Should().Be(maxFrames * frame);
                var overflow = () => OpusToc.GetSampleCount(new[] { toc, (byte)(maxFrames + 1) });
                overflow.Should().Throw<InvalidDataException>().WithMessage("*120 ms*");
                break;
        }
    }

    [Fact]
    public void GetSampleCount_ShouldThrow_WhenPacketEmpty()
    {
        var act = () => OpusToc.GetSampleCount(ReadOnlySpan<byte>.Empty);
        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void GetSampleCount_ShouldThrow_WhenCodeThreeMissingCountByte()
    {
        var act = () => OpusToc.GetSampleCount(new byte[] { 0x03 });
        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void GetSampleCount_ShouldThrow_WhenCodeThreeFrameCountZero()
    {
        var act = () => OpusToc.GetSampleCount(new byte[] { 0x03, 0x00 });
        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void GetSampleCount_ShouldThrow_WhenCodeThreeFrameCountZeroWithFlagBitsSet()
    {
        var act = () => OpusToc.GetSampleCount(new byte[] { 0x03, 0xC0 });
        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void GetSampleCount_ShouldAccept120MsAndRejectLonger_WhenFrameCountVaries()
    {
        // config 3 (SILK 60 ms): 2 frames = 120 ms ok, 3 frames = 180 ms rejected.
        OpusToc.GetSampleCount(new byte[] { 0x1B, 2 }).Should().Be(5760);
        var act = () => OpusToc.GetSampleCount(new byte[] { 0x1B, 3 });
        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void GetSampleCount_ShouldReturn960_WhenPacketIsTypicalCeltFullband()
    {
        OpusToc.GetSampleCount(new byte[] { 0xFC, 0xFF, 0xFE }).Should().Be(960);
    }
}
