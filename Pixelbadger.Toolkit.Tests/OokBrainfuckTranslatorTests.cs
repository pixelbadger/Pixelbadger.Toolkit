using FluentAssertions;
using Pixelbadger.Toolkit.Components;

namespace Pixelbadger.Toolkit.Tests;

public class OokBrainfuckTranslatorTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly OokBrainfuckTranslator _translator;

    public OokBrainfuckTranslatorTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDirectory);
        _translator = new OokBrainfuckTranslator();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    [Theory]
    [InlineData("Ook. Ook?", ">")]
    [InlineData("Ook? Ook.", "<")]
    [InlineData("Ook. Ook.", "+")]
    [InlineData("Ook! Ook!", "-")]
    [InlineData("Ook! Ook.", ".")]
    [InlineData("Ook. Ook!", ",")]
    [InlineData("Ook! Ook?", "[")]
    [InlineData("Ook? Ook!", "]")]
    public void TranslateOokToBrainfuck_ShouldTranslateBasicCommands(string ookCommand, string expectedBf)
    {
        // Act
        var result = _translator.TranslateOokToBrainfuck(ookCommand);

        // Assert
        result.Should().Be(expectedBf);
    }

    [Theory]
    [InlineData(">", "Ook. Ook?")]
    [InlineData("<", "Ook? Ook.")]
    [InlineData("+", "Ook. Ook.")]
    [InlineData("-", "Ook! Ook!")]
    [InlineData(".", "Ook! Ook.")]
    [InlineData(",", "Ook. Ook!")]
    [InlineData("[", "Ook! Ook?")]
    [InlineData("]", "Ook? Ook!")]
    public void TranslateBrainfuckToOok_ShouldTranslateBasicCommands(string bfCommand, string expectedOok)
    {
        // Act
        var result = _translator.TranslateBrainfuckToOok(bfCommand);

        // Assert
        result.Should().Be(expectedOok);
    }

    [Fact]
    public void TranslateOokToBrainfuck_ShouldHandleMultipleCommands()
    {
        // Arrange
        var ookProgram = "Ook. Ook? Ook? Ook. Ook. Ook.";
        var expectedBf = "><+";

        // Act
        var result = _translator.TranslateOokToBrainfuck(ookProgram);

        // Assert
        result.Should().Be(expectedBf);
    }

    [Fact]
    public void TranslateBrainfuckToOok_ShouldHandleMultipleCommands()
    {
        // Arrange
        var bfProgram = "><+";
        var expectedOok = "Ook. Ook? Ook? Ook. Ook. Ook.";

        // Act
        var result = _translator.TranslateBrainfuckToOok(bfProgram);

        // Assert
        result.Should().Be(expectedOok);
    }

    [Fact]
    public void TranslateOokToBrainfuck_ShouldHandleEmptyProgram()
    {
        // Act
        var result = _translator.TranslateOokToBrainfuck("");

        // Assert
        result.Should().Be("");
    }

    [Fact]
    public void TranslateBrainfuckToOok_ShouldHandleEmptyProgram()
    {
        // Act
        var result = _translator.TranslateBrainfuckToOok("");

        // Assert
        result.Should().Be("");
    }

    [Fact]
    public void TranslateOokToBrainfuck_ShouldIgnoreInvalidTokens()
    {
        // Arrange
        var ookProgram = "Ook. Ook? invalid tokens Ook? Ook.";
        var expectedBf = "><";

        // Act
        var result = _translator.TranslateOokToBrainfuck(ookProgram);

        // Assert
        result.Should().Be(expectedBf);
    }

    [Fact]
    public void TranslateBrainfuckToOok_ShouldIgnoreInvalidCharacters()
    {
        // Arrange
        var bfProgram = ">invalid<";
        var expectedOok = "Ook. Ook? Ook? Ook.";

        // Act
        var result = _translator.TranslateBrainfuckToOok(bfProgram);

        // Assert
        result.Should().Be(expectedOok);
    }

    [Fact]
    public void TranslateOokToBrainfuck_ShouldHandleWhitespaceAndFormatting()
    {
        // Arrange
        var ookProgram = @"Ook. Ook?
                          Ook? Ook.
                          Ook. Ook.";
        var expectedBf = "><+";

        // Act
        var result = _translator.TranslateOokToBrainfuck(ookProgram);

        // Assert
        result.Should().Be(expectedBf);
    }

    [Fact]
    public void TranslateOokToBrainfuck_ShouldHandleOddNumberOfTokens()
    {
        // Arrange - Only one Ook token, should be ignored
        var ookProgram = "Ook.";

        // Act
        var result = _translator.TranslateOokToBrainfuck(ookProgram);

        // Assert
        result.Should().Be("");
    }

    [Fact]
    public async Task TranslateOokFileToBrainfuckAsync_ShouldReadFileAndTranslate()
    {
        // Arrange
        var ookFile = Path.Combine(_testDirectory, "test.ook");
        var ookProgram = "Ook. Ook? Ook? Ook.";
        await File.WriteAllTextAsync(ookFile, ookProgram);

        // Act
        var result = await _translator.TranslateOokFileToBrainfuckAsync(ookFile);

        // Assert
        result.Should().Be("><");
    }

    [Fact]
    public async Task TranslateBrainfuckFileToOokAsync_ShouldReadFileAndTranslate()
    {
        // Arrange
        var bfFile = Path.Combine(_testDirectory, "test.bf");
        var bfProgram = "><";
        await File.WriteAllTextAsync(bfFile, bfProgram);

        // Act
        var result = await _translator.TranslateBrainfuckFileToOokAsync(bfFile);

        // Assert
        result.Should().Be("Ook. Ook? Ook? Ook.");
    }

    [Fact]
    public async Task TranslateOokFileToBrainfuckAsync_ShouldThrowFileNotFoundException_WhenFileDoesNotExist()
    {
        // Arrange
        var nonExistentFile = Path.Combine(_testDirectory, "nonexistent.ook");

        // Act & Assert
        var act = async () => await _translator.TranslateOokFileToBrainfuckAsync(nonExistentFile);
        await act.Should().ThrowAsync<FileNotFoundException>()
            .WithMessage($"Ook program file not found: {nonExistentFile}");
    }

    [Fact]
    public async Task TranslateBrainfuckFileToOokAsync_ShouldThrowFileNotFoundException_WhenFileDoesNotExist()
    {
        // Arrange
        var nonExistentFile = Path.Combine(_testDirectory, "nonexistent.bf");

        // Act & Assert
        var act = async () => await _translator.TranslateBrainfuckFileToOokAsync(nonExistentFile);
        await act.Should().ThrowAsync<FileNotFoundException>()
            .WithMessage($"Brainfuck program file not found: {nonExistentFile}");
    }

    [Fact]
    public void RoundTripTranslation_ShouldMaintainIntegrity()
    {
        // Arrange
        var originalBf = ">[+.>]<-";

        // Act - Convert BF to Ook and back to BF
        var ookResult = _translator.TranslateBrainfuckToOok(originalBf);
        var bfResult = _translator.TranslateOokToBrainfuck(ookResult);

        // Assert
        bfResult.Should().Be(originalBf);
    }

    [Fact]
    public void ReverseRoundTripTranslation_ShouldMaintainIntegrity()
    {
        // Arrange
        var originalOok = "Ook. Ook? Ook! Ook? Ook. Ook. Ook? Ook! Ook? Ook.";

        // Act - Convert Ook to BF and back to Ook
        var bfResult = _translator.TranslateOokToBrainfuck(originalOok);
        var ookResult = _translator.TranslateBrainfuckToOok(bfResult);

        // Assert - Should produce semantically equivalent Ook code
        var normalizedOriginal = _translator.TranslateOokToBrainfuck(originalOok);
        var normalizedResult = _translator.TranslateOokToBrainfuck(ookResult);
        normalizedResult.Should().Be(normalizedOriginal);
    }
}