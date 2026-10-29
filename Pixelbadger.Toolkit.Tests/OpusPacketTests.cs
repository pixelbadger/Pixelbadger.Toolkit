using FluentAssertions;
using Pixelbadger.Toolkit.Opus;

namespace Pixelbadger.Toolkit.Tests;

public class OpusPacketTests
{
    private static byte[] Packet(int channels, int frameSize = 960)
    {
        using var e = new OpusEncoder(48000, channels, OpusApplication.Audio);
        e.Bandwidth = OpusBandwidth.Fullband;
        e.Bitrate = 96000;
        var buffer = new byte[4000];
        var n = e.Encode(OpusTestHelpers.Sine(channels, frameSize, 1000), frameSize, buffer);
        return buffer.AsSpan(0, n).ToArray();
    }

    [Fact]
    public void Inspection_ShouldMatchEncodedMonoPacket()
    {
        var p = Packet(1);
        OpusPacket.GetFrameCount(p).Should().Be(1);
        OpusPacket.GetSampleCount(p, 48000).Should().Be(960);
        OpusPacket.GetSamplesPerFrame(p, 48000).Should().Be(960);
        OpusPacket.GetChannelCount(p).Should().Be(1);
        OpusPacket.GetBandwidth(p).Should().Be(OpusBandwidth.Fullband);
    }

    [Fact]
    public void GetChannelCount_ShouldBeTwo_WhenStereoPacket()
    {
        OpusPacket.GetChannelCount(Packet(2)).Should().Be(2);
    }

    [Theory]
    [InlineData(0x00, 1101, 480)]  // SILK NB 10ms: 10ms @48k = 480
    [InlineData(0x08, 1101, 960)]  // SILK NB 20ms
    [InlineData(0x48, 1103, 960)]  // SILK WB 20ms (config 9)
    [InlineData(0x78, 1105, 960)]  // Hybrid FB 20ms (config 15)
    [InlineData(0xD0, 1104, 480)]  // CELT SWB 10ms (config 26)
    [InlineData(0xF8, 1105, 960)]  // CELT FB 20ms (config 31)
    public void Inspection_ShouldMatchKnownTocBytes(byte toc, int bandwidth, int samples)
    {
        var packet = new byte[] { toc, 0, 0 };
        OpusPacket.GetBandwidth(packet).Should().Be((OpusBandwidth)bandwidth);
        OpusPacket.GetSamplesPerFrame(packet, 48000).Should().Be(samples);
        OpusPacket.GetSampleCount(packet, 48000).Should().Be(samples);
        OpusPacket.GetFrameCount(packet).Should().Be(1);
    }

    [Fact]
    public void GetSampleCount_ShouldScaleWithSampleRate()
    {
        var p = Packet(1);
        OpusPacket.GetSampleCount(p, 16000).Should().Be(320);
    }

    [Fact]
    public void Inspection_ShouldThrowOpusException_WhenPacketEmpty()
    {
        ((Action)(() => OpusPacket.GetFrameCount(ReadOnlySpan<byte>.Empty))).Should().Throw<OpusException>();
        ((Action)(() => OpusPacket.GetSampleCount(ReadOnlySpan<byte>.Empty, 48000))).Should().Throw<OpusException>();
        ((Action)(() => OpusPacket.GetSamplesPerFrame(ReadOnlySpan<byte>.Empty, 48000))).Should().Throw<OpusException>();
        ((Action)(() => OpusPacket.GetBandwidth(ReadOnlySpan<byte>.Empty))).Should().Throw<OpusException>();
        ((Action)(() => OpusPacket.GetChannelCount(ReadOnlySpan<byte>.Empty))).Should().Throw<OpusException>();
        ((Action)(() => OpusPacket.Parse(ReadOnlySpan<byte>.Empty))).Should().Throw<OpusException>();
    }

    [Fact]
    public void GetSampleCount_ShouldThrowOpusException_WhenSampleRateInvalid()
    {
        // Code 3 packet missing its frame count byte is invalid.
        ((Action)(() => OpusPacket.GetFrameCount(new byte[] { 0x03 }))).Should().Throw<OpusException>().Where(x => x.ErrorCode == -4);
    }

    [Fact]
    public void Parse_ShouldReturnTocAndSingleFrame_WhenPacketEncoded()
    {
        var p = Packet(1);
        var info = OpusPacket.Parse(p);
        info.Toc.Should().Be(p[0]);
        info.PayloadOffset.Should().Be(1);
        info.Frames.Should().HaveCount(1);
        info.Frames[0].Should().Equal(p.AsSpan(1).ToArray());
    }

    [Fact]
    public void Parse_ShouldReturnTwoFrames_WhenRepacketizedPacket()
    {
        var p = Packet(1);
        using var r = new OpusRepacketizer();
        r.Add(p);
        r.Add(p);
        var merged = r.Output();
        var info = OpusPacket.Parse(merged);
        info.Frames.Should().HaveCount(2);
        info.Frames[0].Should().Equal(p.AsSpan(1).ToArray());
        info.Frames[1].Should().Equal(p.AsSpan(1).ToArray());
        (info.Toc & 0x03).Should().Be(1); // code 1: two equal-size frames
    }

    [Fact]
    public void PadUnpad_ShouldRoundTrip_WhenPaddedToLargerLength()
    {
        var p = Packet(1);
        var padded = OpusPacket.Pad(p, p.Length + 50);
        padded.Should().HaveCount(p.Length + 50);
        OpusPacket.GetSampleCount(padded, 48000).Should().Be(960);

        var unpadded = OpusPacket.Unpad(padded);
        OpusPacket.GetSampleCount(unpadded, 48000).Should().Be(960);
        OpusPacket.Parse(unpadded).Frames[0].Should().Equal(p.AsSpan(1).ToArray());
        unpadded.Length.Should().BeLessThanOrEqualTo(p.Length + 2);
    }

    [Fact]
    public void Pad_ShouldThrowOpusException_WhenNewLengthShorterThanPacket()
    {
        var p = Packet(1);
        ((Action)(() => OpusPacket.Pad(p, p.Length - 1))).Should().Throw<OpusException>().Where(x => x.ErrorCode == -1);
    }

    [Fact]
    public void Pad_ShouldReturnSamePacket_WhenNewLengthEqualsLength()
    {
        var p = Packet(1);
        OpusPacket.Pad(p, p.Length).Should().Equal(p);
    }
}
