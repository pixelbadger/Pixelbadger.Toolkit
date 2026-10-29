using FluentAssertions;
using Pixelbadger.Toolkit.Services.Audio;

namespace Pixelbadger.Toolkit.Tests;

public class PcmConverterTests
{
    [Fact]
    public void SupportedRates_ShouldListExactRates()
    {
        PcmConverter.SupportedRates.Should().Equal(8000, 12000, 16000, 24000, 48000);
    }

    [Theory]
    [InlineData(8000)]
    [InlineData(12000)]
    [InlineData(16000)]
    [InlineData(24000)]
    [InlineData(48000)]
    public void ToOpusInput_ShouldProduceExactLength_ForEachSupportedRate(int rate)
    {
        var n = 480;
        var audio = new PcmAudio(rate, 1, Enumerable.Range(0, n).Select(i => (short)i).ToArray());
        PcmConverter.ToOpusInput(audio, 1).Length.Should().Be(n * 48000 / rate);
        var stereo = new PcmAudio(rate, 2, new short[n * 2]);
        PcmConverter.ToOpusInput(stereo, 2).Length.Should().Be(n * 48000 / rate * 2);
    }

    [Fact]
    public void ToOpusInput_ShouldReturnSamplesUnchanged_When48kHzSameChannels()
    {
        var samples = new short[] { 1, -2, 3, 4 };
        PcmConverter.ToOpusInput(new PcmAudio(48000, 2, samples), 2).Should().Equal(samples);
    }

    [Fact]
    public void ToOpusInput_ShouldInterpolateLinearly_WhenUpsampling24kHz()
    {
        var result = PcmConverter.ToOpusInput(new PcmAudio(24000, 1, new short[] { 0, 100, 200 }), 1);
        result.Should().Equal(0, 50, 100, 150, 200, 200);
    }

    [Fact]
    public void ToOpusInput_ShouldInterpolateLinearly_WhenUpsampling8kHz()
    {
        var result = PcmConverter.ToOpusInput(new PcmAudio(8000, 1, new short[] { 0, 600 }), 1);
        result.Should().Equal(0, 100, 200, 300, 400, 500, 600, 600, 600, 600, 600, 600);
    }

    [Fact]
    public void ToOpusInput_ShouldInterpolateEachChannelSeparately_WhenStereoUpsampled()
    {
        var result = PcmConverter.ToOpusInput(new PcmAudio(24000, 2, new short[] { 0, 1000, 100, -1000 }), 2);
        result.Should().Equal(0, 1000, 50, 0, 100, -1000, 100, -1000);
    }

    [Fact]
    public void ToOpusInput_ShouldDuplicate_WhenMonoToStereo()
    {
        PcmConverter.ToOpusInput(new PcmAudio(48000, 1, new short[] { 5, -6 }), 2).Should().Equal(5, 5, -6, -6);
    }

    [Fact]
    public void ToOpusInput_ShouldAverage_WhenStereoToMono()
    {
        PcmConverter.ToOpusInput(new PcmAudio(48000, 2, new short[] { 10, 20, -10, -30, 32767, 32767 }), 1)
            .Should().Equal(15, -20, 32767);
    }

    [Fact]
    public void ToOpusInput_ShouldConvertChannelsAndRate_WhenBothDiffer()
    {
        var result = PcmConverter.ToOpusInput(new PcmAudio(24000, 2, new short[] { 0, 100, 100, 300 }), 1);
        result.Should().Equal(50, 125, 200, 200);
    }

    [Fact]
    public void ToOpusInput_ShouldReturnEmpty_WhenNoSamples()
    {
        PcmConverter.ToOpusInput(new PcmAudio(8000, 1, Array.Empty<short>()), 2).Should().BeEmpty();
    }

    [Fact]
    public void ToOpusInput_ShouldThrowExactMessage_When44100()
    {
        var act = () => PcmConverter.ToOpusInput(new PcmAudio(44100, 1, new short[10]), 1);
        act.Should().Throw<InvalidDataException>().WithMessage(
            "Unsupported WAV sample rate 44100 Hz. Supported rates: 8000, 12000, 16000, 24000, 48000 Hz. Resample first, e.g. ffmpeg -i in.wav -ar 48000 out.wav");
    }

    [Fact]
    public void ToOpusInput_ShouldThrow_WhenTargetChannelsInvalid()
    {
        var act = () => PcmConverter.ToOpusInput(new PcmAudio(48000, 1, new short[2]), 3);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
