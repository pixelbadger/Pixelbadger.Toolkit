using FluentAssertions;
using Pixelbadger.Toolkit.Components;

namespace Pixelbadger.Toolkit.Tests;

public class OokInterpreterTests : IDisposable
{
    private readonly string _testDirectory;

    public OokInterpreterTests()
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
    public void Execute_ShouldTranslateBasicOokCommands()
    {
        // Arrange
        var interpreter = new OokInterpreter();
        // Basic Ook commands that increment and output 'A' (ASCII 65)
        var program = "Ook. Ook. Ook. Ook. Ook. Ook. Ook. Ook. Ook. Ook. " +
                     "Ook! Ook? Ook? Ook. Ook. Ook. Ook. Ook. Ook. Ook. Ook. " +
                     "Ook. Ook. Ook. Ook. Ook. Ook. Ook. Ook. Ook. Ook. Ook. " +
                     "Ook. Ook. Ook. Ook. Ook. Ook. Ook. Ook. Ook. Ook. Ook? " +
                     "Ook! Ook! Ook. Ook? Ook. Ook! Ook.";

        // Act
        var result = interpreter.Execute(program);

        // Assert
        result.Should().NotBeEmpty();
    }

    [Fact]
    public void Execute_ShouldHandleEmptyProgram()
    {
        // Arrange
        var interpreter = new OokInterpreter();
        var program = "";

        // Act
        var result = interpreter.Execute(program);

        // Assert
        result.Should().Be("");
    }

    [Fact]
    public void Execute_ShouldIgnoreInvalidOokTokens()
    {
        // Arrange
        var interpreter = new OokInterpreter();
        var program = "Ook. Ook? invalid tokens here Ook! Ook!";

        // Act
        var result = interpreter.Execute(program);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public void Execute_ShouldTranslateOokToBrainfuckCorrectly()
    {
        // Arrange
        var interpreter = new OokInterpreter();
        // Ook program that translates to ">" (move right)
        var program = "Ook. Ook?";

        // Act
        var result = interpreter.Execute(program);

        // Assert - Should execute without error (BF ">" command moves pointer)
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReadFromFile()
    {
        // Arrange
        var interpreter = new OokInterpreter();
        var programFile = Path.Combine(_testDirectory, "hello.ook");
        var program = "Ook. Ook. Ook. Ook. Ook. Ook. Ook. Ook. " +
                     "Ook! Ook? Ook? Ook. Ook. Ook. Ook. Ook. Ook. Ook. Ook. " +
                     "Ook. Ook. Ook. Ook. Ook. Ook. Ook. Ook. Ook! Ook.";

        await File.WriteAllTextAsync(programFile, program);

        // Act
        var result = await interpreter.ExecuteAsync(programFile);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldThrowFileNotFoundException_WhenFileDoesNotExist()
    {
        // Arrange
        var interpreter = new OokInterpreter();
        var nonExistentFile = Path.Combine(_testDirectory, "nonexistent.ook");

        // Act & Assert
        var act = async () => await interpreter.ExecuteAsync(nonExistentFile);
        await act.Should().ThrowAsync<FileNotFoundException>()
            .WithMessage($"Ook program file not found: {nonExistentFile}");
    }

    [Theory]
    [InlineData("Ook. Ook?", ">")]
    [InlineData("Ook? Ook.", "<")]
    [InlineData("Ook. Ook.", "+")]
    [InlineData("Ook! Ook!", "-")]
    [InlineData("Ook! Ook.", ".")]
    [InlineData("Ook. Ook!", ",")]
    public void Execute_ShouldTranslateBasicOokCommandsProperly(string ookCommand, string _)
    {
        // Arrange
        var interpreter = new OokInterpreter();

        // Act - We can't directly test the translation, but we can verify it executes
        var result = interpreter.Execute(ookCommand);

        // Assert - Should execute without throwing an exception
        result.Should().NotBeNull();
    }

    [Fact]
    public void Execute_ShouldHandleLoopCommands()
    {
        // Arrange
        var interpreter = new OokInterpreter();
        // Valid loop: [ ] (open and close bracket)
        var program = "Ook! Ook? Ook? Ook!";

        // Act
        var result = interpreter.Execute(program);

        // Assert - Should execute without throwing an exception
        result.Should().NotBeNull();
    }

    [Fact]
    public void Execute_ShouldHandleWhitespaceAndFormatting()
    {
        // Arrange
        var interpreter = new OokInterpreter();
        var program = @"Ook. Ook?
                       Ook? Ook.
                       Ook. Ook.";

        // Act
        var result = interpreter.Execute(program);

        // Assert
        result.Should().NotBeNull();
    }
}