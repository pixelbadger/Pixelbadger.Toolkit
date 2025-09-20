using FluentAssertions;
using Pixelbadger.Toolkit.Components;

namespace Pixelbadger.Toolkit.Tests;

public class BrainfuckInterpreterTests : IDisposable
{
    private readonly string _testDirectory;

    public BrainfuckInterpreterTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    [Fact]
    public void Execute_ShouldOutputHelloWorld()
    {
        // Arrange
        var interpreter = new BrainfuckInterpreter();
        // Classic "Hello World!" program in Brainfuck
        var program = "++++++++[>++++[>++>+++>+++>+<<<<-]>+>+>->>+[<]<-]>>.>---.+++++++..+++.>>.<-.<.+++.------.--------.>>+.>++.";

        // Act
        var result = interpreter.Execute(program);

        // Assert
        result.Should().Be("Hello World!\n");
    }

    [Fact]
    public void Execute_ShouldHandleSimpleOutput()
    {
        // Arrange
        var interpreter = new BrainfuckInterpreter();
        // Program that outputs 'H' (ASCII 72): 8 * 9 = 72
        var program = "++++++++++[>+++++++>++++++++++>+++>+<<<<-]>++.";

        // Act
        var result = interpreter.Execute(program);

        // Assert
        result.Should().Be("H");
    }

    [Fact]
    public void Execute_ShouldHandleEmptyProgram()
    {
        // Arrange
        var interpreter = new BrainfuckInterpreter();
        var program = "";

        // Act
        var result = interpreter.Execute(program);

        // Assert
        result.Should().Be("");
    }

    [Fact]
    public void Execute_ShouldIgnoreNonBrainfuckCharacters()
    {
        // Arrange
        var interpreter = new BrainfuckInterpreter();
        // Program with comments that should output 'P' when comments are ignored: 8 * 10 = 80
        var program = "This is a comment ++++++++ Another comment [>++++++++++<-] More comments >.";

        // Act
        var result = interpreter.Execute(program);

        // Assert
        result.Should().Be("P"); // ASCII 80 (8 * 10)
    }

    [Fact]
    public void Execute_ShouldHandleNestedLoops()
    {
        // Arrange
        var interpreter = new BrainfuckInterpreter();
        // Nested loop: 3 * (2 * 1) = 6, then output ASCII 6 + 42 = 48 ('0')
        var program = "+++[>++[>+<-]<-]>>++++++++++++++++++++++++++++++++++++++++++.";

        // Act
        var result = interpreter.Execute(program);

        // Assert
        result.Should().Be("0"); // ASCII 48 (6 + 42)
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReadFromFile()
    {
        // Arrange
        var interpreter = new BrainfuckInterpreter();
        var programFile = Path.Combine(_testDirectory, "hello.bf");
        var program = "++++++++[>++++[>++>+++>+++>+<<<<-]>+>+>->>+[<]<-]>>.>---.+++++++..+++.>>.<-.<.+++.------.--------.>>+.>++.";

        await File.WriteAllTextAsync(programFile, program);

        // Act
        var result = await interpreter.ExecuteAsync(programFile);

        // Assert
        result.Should().Be("Hello World!\n");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldThrowFileNotFoundException_WhenFileDoesNotExist()
    {
        // Arrange
        var interpreter = new BrainfuckInterpreter();
        var nonExistentFile = Path.Combine(_testDirectory, "nonexistent.bf");

        // Act & Assert
        var act = async () => await interpreter.ExecuteAsync(nonExistentFile);
        await act.Should().ThrowAsync<FileNotFoundException>()
            .WithMessage($"Brainfuck program file not found: {nonExistentFile}");
    }

    [Fact]
    public void Execute_ShouldHandleDataPointerWrapAround()
    {
        // Arrange
        var interpreter = new BrainfuckInterpreter();
        // Test pointer wrapping - move left from position 0, increment 3 times, output
        var program = "<+++.";

        // Act
        var result = interpreter.Execute(program);

        // Assert
        // Should wrap to end of memory, increment to 3, then output ASCII 3 (ETX control character)
        result.Should().Be("\u0003");
    }

    [Theory]
    [InlineData("+.", "\u0001")] // 1
    [InlineData("++.", "\u0002")] // 2
    [InlineData("+++.", "\u0003")] // 3
    [InlineData("++++.", "\u0004")] // 4
    [InlineData("+++++.", "\u0005")] // 5
    [InlineData("++++++.", "\u0006")] // 6
    [InlineData("+++++++.", "\u0007")] // 7
    [InlineData("++++++++.", "\u0008")] // 8
    [InlineData("+++++++++.", "\u0009")] // 9
    [InlineData("++++++++++.", "\u000A")] // 10 = \n
    public void Execute_ShouldOutputCorrectLowAsciiCharacters(string program, string expectedOutput)
    {
        // Arrange
        var interpreter = new BrainfuckInterpreter();

        // Act
        var result = interpreter.Execute(program);

        // Assert
        result.Should().Be(expectedOutput);
    }

    [Fact]
    public void Execute_ShouldHandleMultipleOutputs()
    {
        // Arrange
        var interpreter = new BrainfuckInterpreter();
        // Program that outputs "He": 72 (H) and 101 (e)
        var program = "++++++++++[>+++++++>++++++++++<<-]>++.>+.";

        // Act
        var result = interpreter.Execute(program);

        // Assert
        result.Should().Be("He");
    }

    [Fact]
    public void Execute_ShouldHandleSimpleCharacterOutput()
    {
        // Arrange
        var interpreter = new BrainfuckInterpreter();
        // Program that outputs 'A' (ASCII 65): 5 * 13 = 65
        var program = "+++++[>+++++++++++++<-]>.";

        // Act
        var result = interpreter.Execute(program);

        // Assert
        result.Should().Be("A");
    }

    [Fact]
    public void Execute_ShouldHandleLoopWithNoExecution()
    {
        // Arrange
        var interpreter = new BrainfuckInterpreter();
        // Loop that should never execute because cell starts at 0
        var program = "[+++.]";

        // Act
        var result = interpreter.Execute(program);

        // Assert
        result.Should().Be("");
    }
}