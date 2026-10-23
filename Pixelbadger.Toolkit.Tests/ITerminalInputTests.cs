using FluentAssertions;
using Pixelbadger.Toolkit.Components;

namespace Pixelbadger.Toolkit.Tests;

public class ITerminalInputTests
{
    [Fact]
    public void ConsoleTerminalInput_ShouldImplementITerminalInput_WhenConstructed()
    {
        var input = new ConsoleTerminalInput();

        typeof(ITerminalInput).IsAssignableFrom(input.GetType()).Should().BeTrue();
        input.Should().BeAssignableTo<ITerminalInput>();
    }

    [Fact]
    public void Size_ShouldMatchConsoleWindowDimensions_WhenReadThroughWrapper()
    {
        ITerminalInput input = new ConsoleTerminalInput();

        var expectedWidth = Console.WindowWidth;
        var expectedHeight = Console.WindowHeight;

        input.Size.Should().Be((expectedWidth, expectedHeight));
    }
}
