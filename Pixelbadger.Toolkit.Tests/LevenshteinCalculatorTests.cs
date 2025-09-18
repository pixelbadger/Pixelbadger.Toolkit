using FluentAssertions;
using Pixelbadger.Toolkit.Components;

namespace Pixelbadger.Toolkit.Tests;

public class LevenshteinCalculatorTests : IDisposable
{
    private readonly string _testDirectory;

    public LevenshteinCalculatorTests()
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
    public async Task CalculateDistanceAsync_ShouldReturnZero_ForIdenticalStrings()
    {
        // Arrange
        var calculator = new LevenshteinCalculator();

        // Act
        var result = await calculator.CalculateDistanceAsync("hello", "hello");

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public async Task CalculateDistanceAsync_ShouldReturnCorrectDistance_ForDifferentStrings()
    {
        // Arrange
        var calculator = new LevenshteinCalculator();

        // Act
        var result = await calculator.CalculateDistanceAsync("kitten", "sitting");

        // Assert
        result.Should().Be(3);
    }

    [Fact]
    public async Task CalculateDistanceAsync_ShouldHandleEmptyStrings()
    {
        // Arrange
        var calculator = new LevenshteinCalculator();

        // Act
        var result1 = await calculator.CalculateDistanceAsync("", "hello");
        var result2 = await calculator.CalculateDistanceAsync("hello", "");
        var result3 = await calculator.CalculateDistanceAsync("", "");

        // Assert
        result1.Should().Be(5);
        result2.Should().Be(5);
        result3.Should().Be(0);
    }

    [Fact]
    public async Task CalculateDistanceAsync_ShouldWorkWithFiles()
    {
        // Arrange
        var calculator = new LevenshteinCalculator();
        var file1 = Path.Combine(_testDirectory, "file1.txt");
        var file2 = Path.Combine(_testDirectory, "file2.txt");

        await File.WriteAllTextAsync(file1, "hello");
        await File.WriteAllTextAsync(file2, "world");

        // Act
        var result = await calculator.CalculateDistanceAsync(file1, file2);

        // Assert
        result.Should().Be(4);
    }

    [Fact]
    public async Task CalculateDistanceAsync_ShouldMixFileAndString()
    {
        // Arrange
        var calculator = new LevenshteinCalculator();
        var file1 = Path.Combine(_testDirectory, "file1.txt");

        await File.WriteAllTextAsync(file1, "hello");

        // Act
        var result = await calculator.CalculateDistanceAsync(file1, "world");

        // Assert
        result.Should().Be(4);
    }

    [Theory]
    [InlineData("", "abc", 3)]
    [InlineData("abc", "", 3)]
    [InlineData("abc", "abc", 0)]
    [InlineData("a", "b", 1)]
    [InlineData("ab", "ba", 2)]
    [InlineData("saturday", "sunday", 3)]
    public async Task CalculateDistanceAsync_ShouldReturnCorrectDistances_ForVariousInputs(
        string input1, string input2, int expectedDistance)
    {
        // Arrange
        var calculator = new LevenshteinCalculator();

        // Act
        var result = await calculator.CalculateDistanceAsync(input1, input2);

        // Assert
        result.Should().Be(expectedDistance);
    }
}