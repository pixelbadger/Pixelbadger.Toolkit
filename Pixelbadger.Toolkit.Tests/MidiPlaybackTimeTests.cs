using FluentAssertions;
using Pixelbadger.Toolkit.Services.Audio;

namespace Pixelbadger.Toolkit.Tests;

public class MidiPlaybackTimeTests
{
    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(0.999, "0:00")]
    [InlineData(1, "0:01")]
    [InlineData(59, "0:59")]
    [InlineData(59.9, "0:59")]
    [InlineData(60, "1:00")]
    [InlineData(185, "3:05")]
    [InlineData(3599, "59:59")]
    [InlineData(3600, "1:00:00")]
    [InlineData(3723, "1:02:03")]
    [InlineData(-5, "0:00")]
    public void Format_ShouldReturnExpectedText_WhenGivenSeconds(double seconds, string expected)
    {
        MidiPlaybackTime.Format(TimeSpan.FromSeconds(seconds)).Should().Be(expected);
    }
}
