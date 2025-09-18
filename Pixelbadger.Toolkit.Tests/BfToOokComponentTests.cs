using FluentAssertions;
using Pixelbadger.Toolkit.Components;

namespace Pixelbadger.Toolkit.Tests;

public class BfToOokComponentTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly BfToOokComponent _component;

    public BfToOokComponentTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDirectory);
        _component = new BfToOokComponent();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    [Fact]
    public async Task TranslateFileAsync_ShouldConvertBrainfuckFileToOokFile()
    {
        // Arrange
        var sourceFile = Path.Combine(_testDirectory, "hello.bf");
        var outputFile = Path.Combine(_testDirectory, "hello.ook");
        var bfProgram = "++++++++[>++++[>++>+++>+++>+<<<<-]>+>+>->>+[<]<-]>>.>---.+++++++..+++.>>.<-.<.+++.------.--------.>>+.>++.";

        await File.WriteAllTextAsync(sourceFile, bfProgram);

        // Act
        await _component.TranslateFileAsync(sourceFile, outputFile);

        // Assert
        File.Exists(outputFile).Should().BeTrue();
        var outputContent = await File.ReadAllTextAsync(outputFile);
        outputContent.Should().NotBeEmpty();
        outputContent.Should().Contain("Ook");
    }

    [Fact]
    public async Task TranslateFileAsync_ShouldCreateValidOokProgram()
    {
        // Arrange
        var sourceFile = Path.Combine(_testDirectory, "simple.bf");
        var outputFile = Path.Combine(_testDirectory, "simple.ook");
        var bfProgram = "><+-.";

        await File.WriteAllTextAsync(sourceFile, bfProgram);

        // Act
        await _component.TranslateFileAsync(sourceFile, outputFile);

        // Assert
        var outputContent = await File.ReadAllTextAsync(outputFile);
        outputContent.Should().Be("Ook. Ook? Ook? Ook. Ook. Ook. Ook! Ook! Ook! Ook.");
    }

    [Fact]
    public async Task TranslateFileAsync_ShouldHandleEmptyFile()
    {
        // Arrange
        var sourceFile = Path.Combine(_testDirectory, "empty.bf");
        var outputFile = Path.Combine(_testDirectory, "empty.ook");

        await File.WriteAllTextAsync(sourceFile, "");

        // Act
        await _component.TranslateFileAsync(sourceFile, outputFile);

        // Assert
        File.Exists(outputFile).Should().BeTrue();
        var outputContent = await File.ReadAllTextAsync(outputFile);
        outputContent.Should().Be("");
    }

    [Fact]
    public async Task TranslateFileAsync_ShouldThrowFileNotFoundException_WhenSourceFileDoesNotExist()
    {
        // Arrange
        var nonExistentSource = Path.Combine(_testDirectory, "nonexistent.bf");
        var outputFile = Path.Combine(_testDirectory, "output.ook");

        // Act & Assert
        var act = async () => await _component.TranslateFileAsync(nonExistentSource, outputFile);
        await act.Should().ThrowAsync<FileNotFoundException>()
            .WithMessage($"Brainfuck program file not found: {nonExistentSource}");
    }

    [Fact]
    public async Task TranslateFileAsync_ShouldOverwriteExistingOutputFile()
    {
        // Arrange
        var sourceFile = Path.Combine(_testDirectory, "test.bf");
        var outputFile = Path.Combine(_testDirectory, "test.ook");
        var bfProgram = "+";

        await File.WriteAllTextAsync(sourceFile, bfProgram);
        await File.WriteAllTextAsync(outputFile, "existing content");

        // Act
        await _component.TranslateFileAsync(sourceFile, outputFile);

        // Assert
        var outputContent = await File.ReadAllTextAsync(outputFile);
        outputContent.Should().Be("Ook. Ook.");
        outputContent.Should().NotContain("existing content");
    }

    [Fact]
    public async Task TranslateFileAsync_ShouldCreateOutputDirectoryIfNotExists()
    {
        // Arrange
        var sourceFile = Path.Combine(_testDirectory, "test.bf");
        var outputDir = Path.Combine(_testDirectory, "subdir");
        var outputFile = Path.Combine(outputDir, "test.ook");
        var bfProgram = "+";

        await File.WriteAllTextAsync(sourceFile, bfProgram);

        // Act
        await _component.TranslateFileAsync(sourceFile, outputFile);

        // Assert
        Directory.Exists(outputDir).Should().BeTrue();
        File.Exists(outputFile).Should().BeTrue();
        var outputContent = await File.ReadAllTextAsync(outputFile);
        outputContent.Should().Be("Ook. Ook.");
    }

    [Fact]
    public async Task TranslateFileAsync_ShouldHandleComplexBrainfuckProgram()
    {
        // Arrange
        var sourceFile = Path.Combine(_testDirectory, "complex.bf");
        var outputFile = Path.Combine(_testDirectory, "complex.ook");
        var bfProgram = "++++++++[>++++[>++>+++>+++>+<<<<-]>+>+>->>+[<]<-]";

        await File.WriteAllTextAsync(sourceFile, bfProgram);

        // Act
        await _component.TranslateFileAsync(sourceFile, outputFile);

        // Assert
        File.Exists(outputFile).Should().BeTrue();
        var outputContent = await File.ReadAllTextAsync(outputFile);
        outputContent.Should().NotBeEmpty();

        // Verify it contains proper Ook tokens
        outputContent.Should().Contain("Ook.");
        outputContent.Should().Contain("Ook!");
        outputContent.Should().Contain("Ook?");

        // Verify round-trip translation works
        var translator = new OokBrainfuckTranslator();
        var backToBf = translator.TranslateOokToBrainfuck(outputContent);
        backToBf.Should().Be(bfProgram);
    }

    [Fact]
    public async Task TranslateFileAsync_ShouldIgnoreInvalidBrainfuckCharacters()
    {
        // Arrange
        var sourceFile = Path.Combine(_testDirectory, "invalid.bf");
        var outputFile = Path.Combine(_testDirectory, "invalid.ook");
        var bfProgram = "+invalid>characters-here.";

        await File.WriteAllTextAsync(sourceFile, bfProgram);

        // Act
        await _component.TranslateFileAsync(sourceFile, outputFile);

        // Assert
        var outputContent = await File.ReadAllTextAsync(outputFile);
        outputContent.Should().Be("Ook. Ook. Ook. Ook? Ook! Ook! Ook! Ook.");
    }
}