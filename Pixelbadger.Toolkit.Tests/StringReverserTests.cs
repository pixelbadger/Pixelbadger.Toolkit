using FluentAssertions;
using Pixelbadger.Toolkit.Components;

namespace Pixelbadger.Toolkit.Tests;

public class StringReverserTests : IDisposable
{
    private readonly string _testDirectory;

    public StringReverserTests()
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
    public async Task ReverseFileAsync_ShouldReverseSimpleString()
    {
        // Arrange
        var reverser = new StringReverser();
        var inputFile = Path.Combine(_testDirectory, "input.txt");
        var outputFile = Path.Combine(_testDirectory, "output.txt");
        var inputContent = "hello";

        await File.WriteAllTextAsync(inputFile, inputContent);

        // Act
        await reverser.ReverseFileAsync(inputFile, outputFile);

        // Assert
        var result = await File.ReadAllTextAsync(outputFile);
        result.Should().Be("olleh");
    }

    [Fact]
    public async Task ReverseFileAsync_ShouldReverseMultilineString()
    {
        // Arrange
        var reverser = new StringReverser();
        var inputFile = Path.Combine(_testDirectory, "input.txt");
        var outputFile = Path.Combine(_testDirectory, "output.txt");
        var inputContent = "hello\nworld";

        await File.WriteAllTextAsync(inputFile, inputContent);

        // Act
        await reverser.ReverseFileAsync(inputFile, outputFile);

        // Assert
        var result = await File.ReadAllTextAsync(outputFile);
        result.Should().Be("dlrow\nolleh");
    }

    [Fact]
    public async Task ReverseFileAsync_ShouldHandleEmptyFile()
    {
        // Arrange
        var reverser = new StringReverser();
        var inputFile = Path.Combine(_testDirectory, "input.txt");
        var outputFile = Path.Combine(_testDirectory, "output.txt");

        await File.WriteAllTextAsync(inputFile, "");

        // Act
        await reverser.ReverseFileAsync(inputFile, outputFile);

        // Assert
        var result = await File.ReadAllTextAsync(outputFile);
        result.Should().Be("");
    }

    [Fact]
    public async Task ReverseFileAsync_ShouldThrowFileNotFoundException_WhenInputFileDoesNotExist()
    {
        // Arrange
        var reverser = new StringReverser();
        var inputFile = Path.Combine(_testDirectory, "nonexistent.txt");
        var outputFile = Path.Combine(_testDirectory, "output.txt");

        // Act & Assert
        var act = async () => await reverser.ReverseFileAsync(inputFile, outputFile);
        await act.Should().ThrowAsync<FileNotFoundException>()
            .WithMessage($"Input file '{inputFile}' does not exist.");
    }
}