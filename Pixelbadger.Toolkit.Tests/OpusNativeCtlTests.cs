using System.Runtime.InteropServices;
using FluentAssertions;
using Pixelbadger.Toolkit.Opus;
using Pixelbadger.Toolkit.Opus.Native;

namespace Pixelbadger.Toolkit.Tests;

public class OpusNativeCtlTests
{
    [Fact]
    public void UsePaddedCtl_ShouldMatchPlatform_WhenQueried()
    {
        OpusNative.UsePaddedCtl.Should().Be(OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64);
    }

    [Fact]
    public void EncoderCtlHelpers_ShouldSetAndGetValues_WhenValuesInRange()
    {
        using var encoder = new OpusEncoder(48000, 1, OpusApplication.Audio);
        encoder.Bitrate = 40000;
        encoder.Bitrate.Should().Be(40000);
        encoder.Complexity = 3;
        encoder.Complexity.Should().Be(3);
        encoder.Vbr = false;
        encoder.Vbr.Should().BeFalse();
        encoder.FinalRange.Should().Be(0u);
    }

    [Fact]
    public void DecoderCtlHelpers_ShouldSetAndGetValues_WhenValuesInRange()
    {
        using var decoder = new OpusDecoder(48000, 1);
        decoder.Gain = 256;
        decoder.Gain.Should().Be(256);
        decoder.FinalRange.Should().Be(0u);
    }

    [Fact]
    public void EncoderCtlSet_ShouldReturnNegativeCode_WhenValueInvalid()
    {
        using var encoder = new OpusEncoder(48000, 1, OpusApplication.Audio);
        var act = () => encoder.Complexity = 99;
        act.Should().Throw<OpusException>();
    }
}
