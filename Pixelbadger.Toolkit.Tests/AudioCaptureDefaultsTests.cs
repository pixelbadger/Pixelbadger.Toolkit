using FluentAssertions;
using Pixelbadger.Toolkit.Services.Audio;

namespace Pixelbadger.Toolkit.Tests;

public class AudioCaptureDefaultsTests
{
    [Fact]
    public void ForCurrentPlatform_ShouldReturnPlatformPair_WhenQueriedOnThisOperatingSystem()
    {
        var (format, device) = AudioCaptureDefaults.ForCurrentPlatform();
        if (OperatingSystem.IsWindows())
        {
            format.Should().Be("dshow");
            device.Should().BeNull();
        }
        else if (OperatingSystem.IsMacOS())
        {
            format.Should().Be("avfoundation");
            device.Should().Be(":0");
        }
        else
        {
            format.Should().Be("pulse");
            device.Should().Be("default");
        }
    }
}
