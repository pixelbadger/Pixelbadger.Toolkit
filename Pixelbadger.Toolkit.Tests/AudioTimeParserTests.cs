using FluentAssertions;
using Pixelbadger.Toolkit.Services.Audio;

namespace Pixelbadger.Toolkit.Tests;

public class AudioTimeParserTests
{
    [Theory]
    [InlineData("90", 90000)]
    [InlineData("1:30", 90000)]
    [InlineData("01:01:30.5", 3690500)]
    [InlineData("500ms", 500)]
    [InlineData("2s", 2000)]
    [InlineData("1.5m", 90000)]
    [InlineData("1.013", 1013)]
    [InlineData("2.5", 2500)]
    [InlineData("0", 0)]
    [InlineData(" 3 ", 3000)]
    [InlineData("0:00.250", 250)]
    [InlineData("1.5S", 1500)]
    public void Parse_ShouldReturnExactTime_WhenValid(string text, double milliseconds)
    {
        AudioTimeParser.Parse(text).Should().Be(TimeSpan.FromMilliseconds(milliseconds));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("-1")]
    [InlineData("-1s")]
    [InlineData("1:-5")]
    [InlineData("1:2:3:4")]
    [InlineData(":30")]
    [InlineData("1:")]
    [InlineData("1.5.2")]
    [InlineData("1e3")]
    [InlineData("ms")]
    [InlineData("1,5")]
    [InlineData("1.5:30")]
    [InlineData("99999999999999999999")]
    public void Parse_ShouldThrowFormatException_WhenInvalid(string text)
    {
        var act = () => AudioTimeParser.Parse(text);
        act.Should().Throw<FormatException>().WithMessage(
            $"Invalid time '{text}'. Use [[hh:]mm:]ss[.fff], seconds, or a number with an ms, s or m suffix.");
    }

    [Fact]
    public void Parse_ShouldThrowFormatException_WhenNull()
    {
        var act = () => AudioTimeParser.Parse(null!);
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Parse_ShouldUseInvariantCulture()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            AudioTimeParser.Parse("1.5").Should().Be(TimeSpan.FromMilliseconds(1500));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }
}
