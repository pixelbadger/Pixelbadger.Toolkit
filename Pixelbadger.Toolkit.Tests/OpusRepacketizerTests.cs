using FluentAssertions;
using Pixelbadger.Toolkit.Opus;

namespace Pixelbadger.Toolkit.Tests;

public class OpusRepacketizerTests
{
    private static List<byte[]> TwoPackets()
    {
        using var e = new OpusEncoder(48000, 1, OpusApplication.Audio);
        e.Bitrate = 64000;
        return OpusTestHelpers.EncodeFrames(e, OpusTestHelpers.Sine(1, 1920), 1);
    }

    [Fact]
    public void Output_ShouldMergeTwoTwentyMsPacketsIntoOneFortyMsPacket()
    {
        var packets = TwoPackets();
        using var r = new OpusRepacketizer();
        r.Add(packets[0]);
        r.Add(packets[1]);

        r.FrameCount.Should().Be(2);
        var merged = r.Output();
        OpusPacket.GetSampleCount(merged, 48000).Should().Be(1920);
        using var d = new OpusDecoder(48000, 1);
        d.GetSampleCount(merged).Should().Be(1920);
        d.Decode(merged, new short[1920], 1920).Should().Be(1920);
    }

    [Fact]
    public void Output_ShouldSplitBackIntoDecodablePackets_WhenRangesRequested()
    {
        var packets = TwoPackets();
        using var r = new OpusRepacketizer();
        r.Add(packets[0]);
        r.Add(packets[1]);

        var first = r.Output(0, 1);
        var second = r.Output(1, 2);
        OpusPacket.GetSampleCount(first, 48000).Should().Be(960);
        OpusPacket.GetSampleCount(second, 48000).Should().Be(960);
        OpusPacket.Parse(first).Frames[0].Should().Equal(OpusPacket.Parse(packets[0]).Frames[0]);
        OpusPacket.Parse(second).Frames[0].Should().Equal(OpusPacket.Parse(packets[1]).Frames[0]);

        using var d = new OpusDecoder(48000, 1);
        d.Decode(first, new short[960], 960).Should().Be(960);
        d.Decode(second, new short[960], 960).Should().Be(960);
    }

    [Fact]
    public void Reset_ShouldClearFrames_AndAllowReuse()
    {
        var packets = TwoPackets();
        using var r = new OpusRepacketizer();
        r.Add(packets[0]);
        r.FrameCount.Should().Be(1);
        r.Reset();
        r.FrameCount.Should().Be(0);
        r.Add(packets[1]);
        r.Add(packets[0]);
        r.FrameCount.Should().Be(2);
        OpusPacket.GetSampleCount(r.Output(), 48000).Should().Be(1920);
    }

    [Fact]
    public void Add_ShouldKeepPacketContents_WhenCallerBufferIsMutatedAfterwards()
    {
        var packets = TwoPackets();
        var copy = (byte[])packets[0].Clone();
        var expectedFrame = OpusPacket.Parse(copy).Frames[0];
        using var r = new OpusRepacketizer();
        r.Add(packets[0]);
        Array.Clear(packets[0]);
        GC.Collect();
        OpusPacket.Parse(r.Output()).Frames[0].Should().Equal(expectedFrame);
    }

    [Fact]
    public void Add_ShouldThrowOpusException_WhenPacketInvalid()
    {
        using var r = new OpusRepacketizer();
        ((Action)(() => r.Add(new byte[] { 0x03 }))).Should().Throw<OpusException>();
        ((Action)(() => r.Add(ReadOnlySpan<byte>.Empty))).Should().Throw<OpusException>();
        r.FrameCount.Should().Be(0);
    }

    [Fact]
    public void Add_ShouldThrowOpusException_WhenPacketConfigMismatch()
    {
        var packets = TwoPackets();
        using var r = new OpusRepacketizer();
        r.Add(packets[0]);
        // Different TOC config (SILK NB) cannot be combined with the first packet.
        ((Action)(() => r.Add(new byte[] { 0x00, 0, 0 }))).Should().Throw<OpusException>().Where(x => x.ErrorCode == -4);
        r.FrameCount.Should().Be(1);
    }

    [Fact]
    public void Output_ShouldThrowOpusException_WhenRangeInvalid()
    {
        var packets = TwoPackets();
        using var r = new OpusRepacketizer();
        r.Add(packets[0]);
        ((Action)(() => r.Output(1, 5))).Should().Throw<OpusException>().Where(x => x.ErrorCode == -1);
        ((Action)(() => r.Output(1, 1))).Should().Throw<OpusException>();
    }

    [Fact]
    public void Dispose_ShouldBeIdempotent_AndUseAfterDisposeShouldThrow()
    {
        var packets = TwoPackets();
        var r = new OpusRepacketizer();
        r.Add(packets[0]);
        r.Dispose();
        ((Action)(() => r.Dispose())).Should().NotThrow();
        ((Action)(() => r.Add(packets[1]))).Should().Throw<ObjectDisposedException>();
        ((Action)(() => _ = r.FrameCount)).Should().Throw<ObjectDisposedException>();
        ((Action)(() => r.Output())).Should().Throw<ObjectDisposedException>();
        ((Action)(() => r.Output(0, 1))).Should().Throw<ObjectDisposedException>();
        ((Action)(() => r.Reset())).Should().Throw<ObjectDisposedException>();
    }
}
