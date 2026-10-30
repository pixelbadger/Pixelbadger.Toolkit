using FluentAssertions;
using Pixelbadger.Toolkit.Opus;

namespace Pixelbadger.Toolkit.Tests;

public class OpusEncoderTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void EncodeDecode_ShouldRoundTripSineWithHighCorrelation_WhenMonoOrStereo(int channels)
    {
        var input = OpusTestHelpers.Sine(channels, OpusTestHelpers.Rate);
        using var encoder = new OpusEncoder(48000, channels, OpusApplication.Audio);
        encoder.Bitrate = 96000;
        using var decoder = new OpusDecoder(48000, channels);
        var packets = OpusTestHelpers.EncodeFrames(encoder, input, channels);
        packets.Should().HaveCount(50);

        var decoded = new List<short>();
        var frame = new short[OpusTestHelpers.Frame * channels];
        foreach (var p in packets)
        {
            var n = decoder.Decode(p, frame, OpusTestHelpers.Frame);
            n.Should().Be(OpusTestHelpers.Frame);
            decoded.AddRange(frame);
        }

        decoded.Count.Should().Be(input.Length);
        var look = encoder.Lookahead;
        look.Should().BeGreaterThan(0);
        var a = new List<double>();
        var b = new List<double>();
        for (var i = 0; i + look < OpusTestHelpers.Rate; i++)
        {
            a.Add(input[i * channels]);
            b.Add(decoded[(i + look) * channels]);
        }

        OpusTestHelpers.Correlation(a.ToArray(), b.ToArray()).Should().BeGreaterThan(0.9);
    }

    [Fact]
    public void EncodeFloat_ShouldProducePacket_WhenValidFrame()
    {
        using var encoder = new OpusEncoder(48000, 1, OpusApplication.Audio);
        var pcm = new float[960];
        var output = new byte[4000];
        var n = encoder.EncodeFloat(pcm, 960, output);
        n.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Ctor_ShouldReportProperties_WhenCreated()
    {
        using var encoder = new OpusEncoder(48000, 2, OpusApplication.Voip);
        encoder.SampleRate.Should().Be(48000);
        encoder.Channels.Should().Be(2);
        encoder.Application.Should().Be(OpusApplication.Voip);
        encoder.Lookahead.Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData(6000)]
    [InlineData(64000)]
    [InlineData(256000)]
    public void Bitrate_ShouldRoundTrip_WhenValueInRange(int value)
    {
        using var e = new OpusEncoder(48000, 1, OpusApplication.Audio);
        e.Bitrate = value;
        e.Bitrate.Should().Be(value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(10)]
    public void Complexity_ShouldRoundTrip_WhenValueInRange(int value)
    {
        using var e = new OpusEncoder(48000, 1, OpusApplication.Audio);
        e.Complexity = value;
        e.Complexity.Should().Be(value);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BooleanSettings_ShouldRoundTrip_WhenToggled(bool value)
    {
        using var e = new OpusEncoder(48000, 1, OpusApplication.Audio);
        e.Vbr = value;
        e.Vbr.Should().Be(value);
        e.VbrConstraint = value;
        e.VbrConstraint.Should().Be(value);
        e.Dtx = value;
        e.Dtx.Should().Be(value);
        e.InbandFec = value;
        e.InbandFec.Should().Be(value);
    }

    [Theory]
    [InlineData(OpusSignal.Voice)]
    [InlineData(OpusSignal.Music)]
    [InlineData(OpusSignal.Auto)]
    public void Signal_ShouldRoundTrip_WhenValueSet(OpusSignal value)
    {
        using var e = new OpusEncoder(48000, 1, OpusApplication.Audio);
        e.Signal = value;
        e.Signal.Should().Be(value);
    }

    // OPUS_GET_BANDWIDTH reports the bandwidth of the last encoded frame, so a forced value reads back after encoding.
    [Theory]
    [InlineData(OpusBandwidth.Narrowband)]
    [InlineData(OpusBandwidth.Wideband)]
    [InlineData(OpusBandwidth.SuperWideband)]
    [InlineData(OpusBandwidth.Fullband)]
    public void Bandwidth_ShouldReadBackForcedValue_WhenFrameEncoded(OpusBandwidth value)
    {
        using var e = new OpusEncoder(48000, 1, OpusApplication.Audio);
        e.Bandwidth = value;
        e.Encode(OpusTestHelpers.Sine(1, 960), 960, new byte[4000]);
        e.Bandwidth.Should().Be(value);
    }

    [Fact]
    public void Bandwidth_ShouldAcceptMediumband_WhenForced()
    {
        using var e = new OpusEncoder(48000, 1, OpusApplication.Audio);
        var act = () => e.Bandwidth = OpusBandwidth.Mediumband;
        act.Should().NotThrow();
    }

    [Fact]
    public void Bandwidth_ShouldReportConcreteBandwidth_WhenSetToAuto()
    {
        using var e = new OpusEncoder(48000, 1, OpusApplication.Audio);
        e.Bandwidth = OpusBandwidth.Auto;
        e.Encode(OpusTestHelpers.Sine(1, 960), 960, new byte[4000]);
        e.Bandwidth.Should().BeOneOf(OpusBandwidth.Narrowband, OpusBandwidth.Mediumband, OpusBandwidth.Wideband, OpusBandwidth.SuperWideband, OpusBandwidth.Fullband);
    }

    [Theory]
    [InlineData(OpusBandwidth.Narrowband)]
    [InlineData(OpusBandwidth.Wideband)]
    [InlineData(OpusBandwidth.Fullband)]
    public void MaxBandwidth_ShouldRoundTrip_WhenValueSet(OpusBandwidth value)
    {
        using var e = new OpusEncoder(48000, 1, OpusApplication.Audio);
        e.MaxBandwidth = value;
        e.MaxBandwidth.Should().Be(value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(25)]
    [InlineData(100)]
    public void PacketLossPercentage_ShouldRoundTrip_WhenValueInRange(int value)
    {
        using var e = new OpusEncoder(48000, 1, OpusApplication.Audio);
        e.PacketLossPercentage = value;
        e.PacketLossPercentage.Should().Be(value);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(24)]
    public void LsbDepth_ShouldRoundTrip_WhenValueInRange(int value)
    {
        using var e = new OpusEncoder(48000, 1, OpusApplication.Audio);
        e.LsbDepth = value;
        e.LsbDepth.Should().Be(value);
    }

    [Fact]
    public void Application_ShouldRoundTrip_WhenChangedBeforeEncoding()
    {
        using var e = new OpusEncoder(48000, 1, OpusApplication.Audio);
        e.Application = OpusApplication.Voip;
        e.Application.Should().Be(OpusApplication.Voip);
    }

    [Fact]
    public void FinalRange_ShouldBeNonZero_WhenPacketEncoded()
    {
        using var e = new OpusEncoder(48000, 1, OpusApplication.Audio);
        e.Encode(OpusTestHelpers.Sine(1, 960), 960, new byte[4000]);
        e.FinalRange.Should().NotBe(0u);
    }

    [Fact]
    public void ResetState_ShouldMakeEncodingDeterministic_WhenCalledBetweenIdenticalFrames()
    {
        using var e = new OpusEncoder(48000, 1, OpusApplication.Audio);
        var pcm = OpusTestHelpers.Sine(1, 960);
        var o1 = new byte[4000];
        var o2 = new byte[4000];
        var n1 = e.Encode(pcm, 960, o1);
        e.ResetState();
        var n2 = e.Encode(pcm, 960, o2);
        n2.Should().Be(n1);
        o2.AsSpan(0, n2).SequenceEqual(o1.AsSpan(0, n1)).Should().BeTrue();
    }

    [Fact]
    public void Encode_ShouldThrowOpusExceptionWithStrerrorText_WhenFrameSizeInvalid()
    {
        using var e = new OpusEncoder(48000, 1, OpusApplication.Audio);
        var act = () => e.Encode(new short[4000], 100, new byte[4000]);
        act.Should().Throw<OpusException>().Where(x => x.ErrorCode == -1 && x.Message.Contains("invalid argument"));
    }

    [Fact]
    public void Encode_ShouldThrowOpusException_WhenPcmShorterThanFrame()
    {
        using var e = new OpusEncoder(48000, 2, OpusApplication.Audio);
        var act = () => e.Encode(new short[960], 960, new byte[4000]);
        act.Should().Throw<OpusException>().Where(x => x.ErrorCode == -1);
    }

    [Fact]
    public void EncodeFloat_ShouldThrowOpusException_WhenFrameSizeInvalid()
    {
        using var e = new OpusEncoder(48000, 1, OpusApplication.Audio);
        var act = () => e.EncodeFloat(new float[4000], 100, new byte[4000]);
        act.Should().Throw<OpusException>().Where(x => x.Message.Contains("invalid argument"));
    }

    [Fact]
    public void Encode_ShouldThrowOpusException_WhenOutputBufferTooSmall()
    {
        using var e = new OpusEncoder(48000, 1, OpusApplication.Audio);
        var act = () => e.Encode(OpusTestHelpers.Sine(1, 960), 960, Span<byte>.Empty);
        act.Should().Throw<OpusException>();
    }

    [Theory]
    [InlineData(3, 48000)]
    [InlineData(1, 44100)]
    [InlineData(0, 48000)]
    public void Ctor_ShouldThrowOpusException_WhenArgumentsInvalid(int channels, int rate)
    {
        var act = () => new OpusEncoder(rate, channels, OpusApplication.Audio);
        act.Should().Throw<OpusException>().Where(x => x.ErrorCode == -1 && x.Message.Contains("invalid argument"));
    }

    [Fact]
    public void Setters_ShouldThrowOpusException_WhenValueOutOfRange()
    {
        using var e = new OpusEncoder(48000, 1, OpusApplication.Audio);
        var act = () => e.Complexity = 11;
        act.Should().Throw<OpusException>().Where(x => x.Message.Contains("OPUS_SET_COMPLEXITY") && x.Message.Contains("invalid argument"));
    }

    [Fact]
    public void Dispose_ShouldBeIdempotentAndUseShouldThrow_WhenDisposed()
    {
        var e = new OpusEncoder(48000, 1, OpusApplication.Audio);
        e.Dispose();
        var dispose = () => e.Dispose();
        dispose.Should().NotThrow();
        ((Action)(() => _ = e.Bitrate)).Should().Throw<ObjectDisposedException>();
        ((Action)(() => e.Encode(new short[960], 960, new byte[4000]))).Should().Throw<ObjectDisposedException>();
        ((Action)(() => e.ResetState())).Should().Throw<ObjectDisposedException>();
        ((Action)(() => _ = e.FinalRange)).Should().Throw<ObjectDisposedException>();
    }
}
