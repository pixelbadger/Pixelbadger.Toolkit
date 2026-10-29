using FluentAssertions;
using Moq;
using Pixelbadger.Toolkit.Opus;
using Pixelbadger.Toolkit.Services.Audio;

namespace Pixelbadger.Toolkit.Tests;

public class OpusPcmEncoderTests
{
    private static readonly OpusPcmEncoder Real = new(new OpusCodecFactory());

    [Theory]
    [InlineData(1, 1, 647)]
    [InlineData(959, 2, 649)]
    [InlineData(960, 2, 648)]
    [InlineData(48000, 51, 648)]
    public void Encode_ShouldMatchExactCounts_WhenKnownLengths(int n, int packets, int endTrim)
    {
        var audio = Real.Encode(OpusTestHelpers.Sine(1, n), new OpusEncodeSettings(1));
        audio.Lookahead.Should().Be(312);
        audio.SampleCount.Should().Be(n);
        audio.Channels.Should().Be(1);
        audio.Packets.Should().HaveCount(packets).And.OnlyContain(p => p.Length > 0);
        audio.EndTrim.Should().Be(endTrim);
    }

    [Fact]
    public void Encode_ShouldHandleStereo_WhenTwoChannels()
    {
        var audio = Real.Encode(OpusTestHelpers.Sine(2, 4800), new OpusEncodeSettings(2));
        audio.Packets.Should().HaveCount(6);
        audio.SampleCount.Should().Be(4800);
        audio.Channels.Should().Be(2);
        audio.Packets.Should().OnlyContain(p => p.Length > 0);
    }

    [Fact]
    public void Encode_ShouldThrow_WhenPcmEmpty()
    {
        var act = () => Real.Encode(ReadOnlySpan<short>.Empty, new OpusEncodeSettings(1));
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Encode_ShouldThrow_WhenPcmNotMultipleOfChannels()
    {
        var act = () => Real.Encode(new short[3], new OpusEncodeSettings(2));
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void Encode_ShouldThrow_WhenChannelsInvalid(int channels)
    {
        var act = () => Real.Encode(new short[960], new OpusEncodeSettings(channels));
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(48000, 25)]
    [InlineData(100, 3)]
    [InlineData(5000, 1)]
    public void Encode_ShouldProduceExactlyKPackets_WhenMaxPacketsGiven(int n, int k)
    {
        var audio = Real.Encode(OpusTestHelpers.Sine(1, n), new OpusEncodeSettings(1), k);
        audio.Packets.Should().HaveCount(k);
        audio.EndTrim.Should().Be(0);
        audio.SampleCount.Should().Be(n);
    }

    [Fact]
    public void Encode_ShouldThrow_WhenMaxPacketsNotPositive()
    {
        var act = () => Real.Encode(new short[960], new OpusEncodeSettings(1), 0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Encode_ShouldApplyBitrateAndComplexity_WhenUsingMockFactory()
    {
        var encoder = new FakeEncoder();
        var factory = new Mock<IOpusCodecFactory>();
        factory.Setup(f => f.CreateEncoder(48000, 2, OpusApplication.Audio)).Returns(encoder);

        var audio = new OpusPcmEncoder(factory.Object).Encode(new short[960 * 2 * 2], new OpusEncodeSettings(2, 32000, 5));

        audio.Packets.Should().HaveCount(3);
        encoder.BitrateSets.Should().Equal(32000);
        encoder.ComplexitySets.Should().Equal(5);
        encoder.FrameSizes.Should().Equal(960, 960, 960);
        factory.Verify(f => f.CreateEncoder(48000, 2, OpusApplication.Audio), Times.Once);
        encoder.Disposed.Should().BeTrue();
    }

    [Fact]
    public void Encode_ShouldUseFreshEncoderEachCall_WhenCalledTwice()
    {
        var factory = new Mock<IOpusCodecFactory>();
        factory.Setup(f => f.CreateEncoder(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<OpusApplication>()))
            .Returns(() => new FakeEncoder());
        var enc = new OpusPcmEncoder(factory.Object);
        enc.Encode(new short[10], new OpusEncodeSettings(1));
        enc.Encode(new short[10], new OpusEncodeSettings(1));
        factory.Verify(f => f.CreateEncoder(48000, 1, OpusApplication.Audio), Times.Exactly(2));
    }

    private sealed class FakeEncoder : IOpusEncoder
    {
        public List<int> BitrateSets { get; } = [];
        public List<int> ComplexitySets { get; } = [];
        public List<int> FrameSizes { get; } = [];
        public bool Disposed { get; private set; }
        public int SampleRate => 48000;
        public int Channels => 2;
        public int Encode(ReadOnlySpan<short> pcm, int frameSize, Span<byte> output) { FrameSizes.Add(frameSize); output[0] = 1; return 1; }
        public int EncodeFloat(ReadOnlySpan<float> pcm, int frameSize, Span<byte> output) => throw new NotSupportedException();
        public int Bitrate { get => 0; set => BitrateSets.Add(value); }
        public int Complexity { get => 0; set => ComplexitySets.Add(value); }
        public bool Vbr { get; set; }
        public bool VbrConstraint { get; set; }
        public OpusSignal Signal { get; set; }
        public OpusBandwidth Bandwidth { get; set; }
        public OpusBandwidth MaxBandwidth { get; set; }
        public bool Dtx { get; set; }
        public bool InbandFec { get; set; }
        public int PacketLossPercentage { get; set; }
        public int LsbDepth { get; set; }
        public OpusApplication Application { get; set; }
        public int Lookahead => 312;
        public uint FinalRange => 0;
        public void ResetState() { }
        public void Dispose() => Disposed = true;
    }
}
