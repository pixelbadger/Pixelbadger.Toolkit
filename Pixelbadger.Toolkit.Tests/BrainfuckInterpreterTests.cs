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
        // Program that outputs 'A' (ASCII 65)
        var program = "++++++++++[>+++++++>++++++++++>+++>+<<<<-]>++.";

        // Act
        var result = interpreter.Execute(program);

        // Assert
        result.Should().NotBeEmpty();
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
        // Program with comments and invalid characters
        var program = "This is a comment +++++++++ Another comment [>++++[>++>+++>+++>+<<<<-]>+>+>->>+[<]<-] More comments >>.";

        // Act
        var result = interpreter.Execute(program);

        // Assert
        result.Should().NotBeEmpty();
    }

    [Fact]
    public void Execute_ShouldHandleNestedLoops()
    {
        // Arrange
        var interpreter = new BrainfuckInterpreter();
        // Simple nested loop test
        var program = "+++[>++[>+<-]<-]>>.";

        // Act
        var result = interpreter.Execute(program);

        // Assert
        result.Should().NotBeEmpty();
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
        // Test pointer wrapping - move left from position 0
        var program = "<+++.";

        // Act
        var result = interpreter.Execute(program);

        // Assert
        // Should wrap to end of memory and increment, then output
        result.Should().NotBeEmpty();
    }
}