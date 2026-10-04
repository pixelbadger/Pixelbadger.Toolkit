using FluentAssertions;
using Pixelbadger.Toolkit.Commands;

namespace Pixelbadger.Toolkit.Tests;

public class DemosceneCommandTests
{
    [Theory]
    [InlineData("kefrens")]
    [InlineData("boing")]
    [InlineData("plasma")]
    [InlineData("fire")]
    [InlineData("copper")]
    [InlineData("showcase")]
    public void Create_ShouldRegisterAction_WithFramesOption(string action)
    {
        var command = DemosceneCommand.Create();

        var sub = command.Subcommands.Single(c => c.Name == action);

        sub.Options.Should().Contain(o => o.Name == "--frames");
    }

    [Theory]
    [InlineData("showcase --frames 3")]
    [InlineData("fire --frames 10")]
    public void Parse_ShouldHaveNoErrors_WhenFramesProvided(string args)
    {
        var result = DemosceneCommand.Create().Parse(args);

        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Parse_ShouldReportError_WhenFramesIsNotAnInteger()
    {
        var result = DemosceneCommand.Create().Parse("showcase --frames abc");

        result.Errors.Should().NotBeEmpty();
    }
}
