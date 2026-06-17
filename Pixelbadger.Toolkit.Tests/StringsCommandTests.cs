using FluentAssertions;
using Pixelbadger.Toolkit.Commands;

namespace Pixelbadger.Toolkit.Tests;

public class StringsCommandTests
{
    [Theory]
    [InlineData(0,   "0s")]
    [InlineData(1,   "1s")]
    [InlineData(59,  "59s")]
    [InlineData(60,  "1m")]
    [InlineData(61,  "1m 1s")]
    [InlineData(120, "2m")]
    [InlineData(121, "2m 1s")]
    [InlineData(90,  "1m 30s")]
    public void FormatReadingTime_ShouldReturnCorrectString_ForGivenTotalSeconds(int totalSeconds, string expected)
    {
        var result = StringsCommand.FormatReadingTime(totalSeconds);

        result.Should().Be(expected);
    }
}
