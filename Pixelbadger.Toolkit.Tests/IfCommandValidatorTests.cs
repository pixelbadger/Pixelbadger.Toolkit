using FluentAssertions;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Tests;

public class IfCommandValidatorTests
{
    private const string ModelMessage = "Use the save/restore slot tools instead of in-game save, restore, quit or restart.";
    private const string HumanMessage = "Use /save, /restore or /quit.";

    [Theory]
    [InlineData("look")]
    [InlineData("  take lamp  ")]
    [InlineData("ask man about \\ghost")]
    [InlineData("saved game")]
    [InlineData("quite")]
    [InlineData("restoration")]
    [InlineData("take quilt")]
    [InlineData("look at the quiet sea")]
    [InlineData("read restoration notice")]
    [InlineData("open the thenardier box")]
    public void Validate_ShouldReturnNull_WhenCommandIsValid(string command)
    {
        IfCommandValidator.Validate(command, false).Should().BeNull();
        IfCommandValidator.Validate(command, true).Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_ShouldRejectEmpty_WhenCommandIsBlank(string? command)
    {
        IfCommandValidator.Validate(command, false).Should().Be("Command cannot be empty.");
    }

    [Theory]
    [InlineData("look\nsave")]
    [InlineData("look\r\nsave")]
    [InlineData("a\rb")]
    public void Validate_ShouldRejectMultiLine_WhenCommandHasLineBreak(string command)
    {
        IfCommandValidator.Validate(command, false).Should().Be("Command must be a single line.");
    }

    [Fact]
    public void Validate_ShouldAcceptExactlyMaxLength_AndRejectOneMore()
    {
        IfCommandValidator.Validate(new string('a', 200), false).Should().BeNull();
        IfCommandValidator.Validate(new string('a', 201), false).Should().Be("Command is longer than 200 characters.");
    }

    [Fact]
    public void Validate_ShouldTrimBeforeMeasuringLength()
    {
        IfCommandValidator.Validate("  " + new string('a', 200) + "  ", false).Should().BeNull();
    }

    [Theory]
    [InlineData("\\foo")]
    [InlineData("  \\")]
    public void Validate_ShouldRejectBackslash_WhenCommandStartsWithIt(string command)
    {
        IfCommandValidator.Validate(command, true).Should().Be("Commands may not start with a backslash.");
    }

    [Theory]
    [InlineData("save")]
    [InlineData("SAVE")]
    [InlineData("Restore")]
    [InlineData("quit")]
    [InlineData("q")]
    [InlineData("Q")]
    [InlineData("restart")]
    [InlineData("script")]
    [InlineData("unscript")]
    [InlineData("  save  my game")]
    [InlineData("quit now")]
    [InlineData("restore\tslot")]
    [InlineData("look. quit")]
    [InlineData("quit.")]
    [InlineData("x me, restart")]
    [InlineData("n then save")]
    [InlineData("n THEN save")]
    [InlineData("LOOK.QUIT")]
    [InlineData("look,script")]
    [InlineData("look; restore")]
    [InlineData("look! q")]
    [InlineData("look?unscript")]
    [InlineData("(quit)")]
    public void Validate_ShouldRejectMetaVerbs_WithActorSpecificMessage(string command)
    {
        IfCommandValidator.Validate(command, false).Should().Be(ModelMessage);
        IfCommandValidator.Validate(command, true).Should().Be(HumanMessage);
    }
}
