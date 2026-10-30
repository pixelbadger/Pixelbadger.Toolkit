using FluentAssertions;
using Pixelbadger.Toolkit.Services.Audio;

namespace Pixelbadger.Toolkit.Tests;

public class AudioPlaybackDefaultsTests
{
    [Fact]
    public void ForCurrentPlatform_ShouldMatchPlatformDefaults_WhenCalled()
    {
        var expected = OperatingSystem.IsWindows() ? ("sdl2", "default")
            : OperatingSystem.IsMacOS() ? ("audiotoolbox", "0")
            : ("pulse", "default");

        AudioPlaybackDefaults.ForCurrentPlatform().Should().Be(expected);
    }
}
