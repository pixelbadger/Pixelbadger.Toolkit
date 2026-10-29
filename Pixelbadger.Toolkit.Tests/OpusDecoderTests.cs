using FluentAssertions;
using Pixelbadger.Toolkit.Opus;

namespace Pixelbadger.Toolkit.Tests;

public class OpusDecoderTests
{
    private static byte[] EncodePacket(int channels, int bitrate = 64000)
    {
        using var e = new OpusEncoder(48000, channels, OpusApplication.Audio);
        e.Bitrate = bitrate;
        var buffer = new byte[4000];
        var n = e.Encode(OpusTestHelpers.Sine(channels, 960), 960, buffer);
        return buffer.AsSpan(0, n).ToArray();
    }

    [Fact]
    public void Ctor_ShouldReportProperties_WhenCreated()
    {
        using var d = new OpusDecoder(48000, 2);
        d.SampleRate.Should().Be(48000);
        d.Channels.Should().Be(2);
    }

    [Fact]
    public void Decode_ShouldReturnSamplesPerChannel_WhenValidPacket()
    {
        using var d = new OpusDecoder(48000, 1);
        d.Decode(EncodePacket(1), new short[960], 960).Should().Be(960);
    }

    [Fact]
    public void DecodeFloat_ShouldReturnSamplesPerChannel_WhenValidPacket()
    {
        using var d = new OpusDecoder(48000, 2);
        d.DecodeFloat(EncodePacket(2), new float[1920], 960).Should().Be(960);
    }

    [Fact]
    public void Decode_ShouldPerformConcealment_WhenPacketEmpty()
    {
        using var d = new OpusDecoder(48000, 1);
        d.Decode(EncodePacket(1), new short[960], 960);
        d.Decode(ReadOnlySpan<byte>.Empty, new short[960], 960).Should().Be(960);
    }

    [Fact]
    public void Decode_ShouldThrowOpusException_WhenFrameSizeTooSmallForPacket()
    {
        using var d = new OpusDecoder(48000, 1);
        var act = () => d.Decode(EncodePacket(1), new short[960], 480);
        act.Should().Throw<OpusException>().Where(x => x.ErrorCode == -2 && x.Message.Contains("buffer too small"));
    }

    [Fact]
    public void Decode_ShouldThrowOpusException_WhenPcmBufferShorterThanFrame()
    {
        using var d = new OpusDecoder(48000, 2);
        var act = () => d.Decode(EncodePacket(2), new short[960], 960);
        act.Should().Throw<OpusException>().Where(x => x.ErrorCode == -1);
    }

    [Fact]
    public void Decode_ShouldThrowOpusException_WhenPacketCorrupt()
    {
        using var d = new OpusDecoder(48000, 1);
        // TOC says code 3 (arbitrary frame count) but there is no frame count byte.
        var act = () => d.Decode(new byte[] { 0x03 }, new short[960], 960);
        act.Should().Throw<OpusException>().Where(x => x.ErrorCode == -4 && x.Message.Contains("corrupted"));
    }

    [Theory]
    [InlineData(3, 48000)]
    [InlineData(1, 44100)]
    public void Ctor_ShouldThrowOpusException_WhenArgumentsInvalid(int channels, int rate)
    {
        var act = () => new OpusDecoder(rate, channels);
        act.Should().Throw<OpusException>().Where(x => x.ErrorCode == -1 && x.Message.Contains("invalid argument"));
    }

    [Fact]
    public void GetSampleCount_ShouldReturn960_WhenTwentyMillisecondPacket()
    {
        using var d = new OpusDecoder(48000, 1);
        d.GetSampleCount(EncodePacket(1)).Should().Be(960);
    }

    [Fact]
    public void GetSampleCount_ShouldThrow_WhenPacketEmpty()
    {
        using var d = new OpusDecoder(48000, 1);
        var act = () => d.GetSampleCount(ReadOnlySpan<byte>.Empty);
        act.Should().Throw<OpusException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(256)]
    [InlineData(-256)]
    public void Gain_ShouldRoundTrip(int value)
    {
        using var d = new OpusDecoder(48000, 1);
        d.Gain = value;
        d.Gain.Should().Be(value);
    }

    [Fact]
    public void Gain_ShouldThrowOpusException_WhenOutOfRange()
    {
        using var d = new OpusDecoder(48000, 1);
        var act = () => d.Gain = 40000;
        act.Should().Throw<OpusException>().Where(x => x.Message.Contains("OPUS_SET_GAIN"));
    }

    [Fact]
    public void LastPacketDuration_ShouldMatchDecodedFrame_WhenPacketDecoded()
    {
        using var d = new OpusDecoder(48000, 1);
        d.Decode(EncodePacket(1), new short[960], 960);
        d.LastPacketDuration.Should().Be(960);
    }

    [Fact]
    public void FinalRange_ShouldMatchEncoder_WhenSamePacketDecoded()
    {
        using var e = new OpusEncoder(48000, 1, OpusApplication.Audio);
        var buffer = new byte[4000];
        var n = e.Encode(OpusTestHelpers.Sine(1, 960), 960, buffer);
        using var d = new OpusDecoder(48000, 1);
        d.Decode(buffer.AsSpan(0, n), new short[960], 960);
        d.FinalRange.Should().Be(e.FinalRange);
    }

    [Fact]
    public void Bandwidth_ShouldBeFullband_WhenFullbandPacketDecoded()
    {
        using var e = new OpusEncoder(48000, 1, OpusApplication.Audio);
        e.Bandwidth = OpusBandwidth.Fullband;
        e.Bitrate = 128000;
        var buffer = new byte[4000];
        var n = e.Encode(OpusTestHelpers.Sine(1, 960, 1000), 960, buffer);
        using var d = new OpusDecoder(48000, 1);
        d.Decode(buffer.AsSpan(0, n), new short[960], 960);
        d.Bandwidth.Should().Be(OpusBandwidth.Fullband);
    }

    [Fact]
    public void ResetState_ShouldNotThrow_AndDecodingContinues()
    {
        using var d = new OpusDecoder(48000, 1);
        var packet = EncodePacket(1);
        d.Decode(packet, new short[960], 960);
        d.ResetState();
        d.Decode(packet, new short[960], 960).Should().Be(960);
    }

    [Fact]
    public void Dispose_ShouldBeIdempotent_AndUseAfterDisposeShouldThrow()
    {
        var d = new OpusDecoder(48000, 1);
        d.Dispose();
        ((Action)(() => d.Dispose())).Should().NotThrow();
        ((Action)(() => d.Decode(new byte[] { 1 }, new short[960], 960))).Should().Throw<ObjectDisposedException>();
        ((Action)(() => _ = d.Gain)).Should().Throw<ObjectDisposedException>();
        ((Action)(() => _ = d.SampleRate)).Should().Throw<ObjectDisposedException>();
        ((Action)(() => d.ResetState())).Should().Throw<ObjectDisposedException>();
    }
}
