using FluentAssertions;
using Pixelbadger.Toolkit.Opus;

namespace Pixelbadger.Toolkit.Tests;

public class OpusCodecFactoryTests
{
    private readonly IOpusCodecFactory _factory = new OpusCodecFactory();

    [Fact]
    public void CreateEncoder_ShouldReturnEncoderWithRequestedFormat()
    {
        using var e = _factory.CreateEncoder(48000, 2, OpusApplication.Audio);
        e.Should().BeOfType<OpusEncoder>();
        e.SampleRate.Should().Be(48000);
        e.Channels.Should().Be(2);
    }

    [Fact]
    public void CreateDecoder_ShouldReturnDecoderWithRequestedFormat()
    {
        using var d = _factory.CreateDecoder(48000, 1);
        d.Should().BeOfType<OpusDecoder>();
        d.SampleRate.Should().Be(48000);
        d.Channels.Should().Be(1);
    }

    [Fact]
    public void CreateEncoder_ShouldThrowOpusException_WhenInvalid()
    {
        var act = () => _factory.CreateEncoder(44100, 1, OpusApplication.Audio);
        act.Should().Throw<OpusException>();
    }

    [Fact]
    public void CreateDecoder_ShouldThrowOpusException_WhenInvalid()
    {
        var act = () => _factory.CreateDecoder(48000, 5);
        act.Should().Throw<OpusException>();
    }

    [Fact]
    public void FactoryCodecs_ShouldRoundTripAFrame()
    {
        using var e = _factory.CreateEncoder(48000, 1, OpusApplication.Audio);
        using var d = _factory.CreateDecoder(48000, 1);
        var buffer = new byte[4000];
        var n = e.Encode(OpusTestHelpers.Sine(1, 960), 960, buffer);
        d.Decode(buffer.AsSpan(0, n), new short[960], 960).Should().Be(960);
    }
}
