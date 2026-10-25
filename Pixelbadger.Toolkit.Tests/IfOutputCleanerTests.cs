using FluentAssertions;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Tests;

public class IfOutputCleanerTests
{
    // Captured from dfrotz 2.54 running a real story file for the command "se" (-m -p -q -w 80).
    private const string RealRaw =
        "\n\n>\n\n\nThis narrow aperture between two buildings is nearly blocked with piles of\n" +
        "rotting cardboard boxes and overstuffed garbage cans. Ugly, half-crumbling brick\n" +
        "walls to either side totter oppressively over you. The alley ends here at a\n" +
        "tall, wooden fence.\n\n" +
        "High up on the wall of the northern building there is a narrow, transom-style\nwindow.\n\n" +
        ">Please enter a filename [autosave.qzl]: \n\nOk.\n\n>\n\nAre you sure you want to quit? \n";

    private const string RealCleaned =
        "This narrow aperture between two buildings is nearly blocked with piles of\n" +
        "rotting cardboard boxes and overstuffed garbage cans. Ugly, half-crumbling brick\n" +
        "walls to either side totter oppressively over you. The alley ends here at a\n" +
        "tall, wooden fence.\n\n" +
        "High up on the wall of the northern building there is a narrow, transom-style\nwindow.";

    [Fact]
    public void Clean_ShouldReturnGameText_WhenGivenRealDfrotzOutput()
    {
        IfOutputCleaner.Clean(RealRaw).Should().Be(RealCleaned);
    }

    [Fact]
    public void Clean_ShouldHandleCrLf_WhenOutputUsesWindowsLineEndings()
    {
        IfOutputCleaner.Clean(RealRaw.Replace("\n", "\r\n")).Should().Be(RealCleaned);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n\n  \n")]
    [InlineData(">\n>\n")]
    public void Clean_ShouldReturnEmpty_WhenNothingRemains(string raw)
    {
        IfOutputCleaner.Clean(raw).Should().BeEmpty();
    }

    [Fact]
    public void Clean_ShouldCutEverythingFromSaveNoise_Onward()
    {
        IfOutputCleaner.Clean("Hello.\nPlease enter a filename [x]: \nstuff after").Should().Be("Hello.");
    }

    [Fact]
    public void Clean_ShouldDropBarePromptLines_IncludingWhitespaceVariants()
    {
        IfOutputCleaner.Clean("one\n>\n  >  \ntwo").Should().Be("one\ntwo");
    }

    [Fact]
    public void Clean_ShouldStripSingleLeadingMarker_FromTextLines()
    {
        IfOutputCleaner.Clean(">>look\n>Room").Should().Be(">look\nRoom");
    }

    [Fact]
    public void Clean_ShouldTrimTrailingWhitespace_PerLine()
    {
        IfOutputCleaner.Clean("a   \nb\t").Should().Be("a\nb");
    }

    [Fact]
    public void Clean_ShouldCollapseBlankRuns_ToSingleBlankLine()
    {
        IfOutputCleaner.Clean("a\n\n\n\n   \nb").Should().Be("a\n\nb");
    }

    [Fact]
    public void Clean_ShouldTrimLeadingAndTrailingBlankLines()
    {
        IfOutputCleaner.Clean("\n\n  a\n\n\n").Should().Be("  a");
    }

    [Fact]
    public void Clean_ShouldKeepIndentation_OfTextLines()
    {
        IfOutputCleaner.Clean("   [Press 'R' to restore]\ntext").Should().Be("   [Press 'R' to restore]\ntext");
    }
}
