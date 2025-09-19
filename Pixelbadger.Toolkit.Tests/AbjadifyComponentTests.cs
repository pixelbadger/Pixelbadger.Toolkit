using FluentAssertions;
using Pixelbadger.Toolkit.Components;

namespace Pixelbadger.Toolkit.Tests;

public class AbjadifyComponentTests : IDisposable
{
    private readonly AbjadifyComponent _component;
    private readonly string _testDirectory;

    public AbjadifyComponentTests()
    {
        _component = new AbjadifyComponent();
        _testDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
            Directory.Delete(_testDirectory, true);
    }

    [Fact]
    public void AbjadifyText_ShouldRemoveVowels_WhenGivenSimpleWord()
    {
        var result = _component.AbjadifyText("hello");
        result.Should().Be("hll");
    }

    [Fact]
    public void AbjadifyText_ShouldPreserveSingleVowelWords_WhenGivenArticles()
    {
        var result = _component.AbjadifyText("a big apple and i");
        result.Should().Be("a bg ppl nd i");
    }

    [Theory]
    [InlineData("a", "a")]
    [InlineData("A", "A")]
    [InlineData("i", "i")]
    [InlineData("I", "I")]
    public void AbjadifyText_ShouldPreserveSingleVowelWords_WhenGivenSpecificArticles(string input, string expected)
    {
        var result = _component.AbjadifyText(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("cat's", "ct's")]
    [InlineData("don't", "dn't")]
    [InlineData("we're", "w'r")]
    [InlineData("they've", "thy'v")]
    [InlineData("he'll", "h'll")]
    [InlineData("she'd", "sh'd")]
    [InlineData("I'm", "I'm")]
    [InlineData("can't", "cn't")]
    public void AbjadifyText_ShouldStripVowelsFromContractions_WhenGivenContractions(string input, string expected)
    {
        var result = _component.AbjadifyText(input);
        result.Should().Be(expected);
    }

    [Fact]
    public void AbjadifyText_ShouldHandleMixedCase_WhenGivenUpperAndLowercase()
    {
        var result = _component.AbjadifyText("Hello World");
        result.Should().Be("Hll Wrld");
    }

    [Fact]
    public void AbjadifyText_ShouldPreservePunctuation_WhenGivenTextWithPunctuation()
    {
        var result = _component.AbjadifyText("Hello, world! How are you?");
        result.Should().Be("Hll, wrld! Hw r y?");
    }

    [Fact]
    public void AbjadifyText_ShouldPreserveWhitespace_WhenGivenTextWithSpaces()
    {
        var result = _component.AbjadifyText("  hello   world  ");
        result.Should().Be("  hll   wrld  ");
    }

    [Fact]
    public void AbjadifyText_ShouldHandleEmptyString_WhenGivenEmptyInput()
    {
        var result = _component.AbjadifyText("");
        result.Should().Be("");
    }

    [Fact]
    public void AbjadifyText_ShouldHandleNullString_WhenGivenNullInput()
    {
        var result = _component.AbjadifyText(null!);
        result.Should().BeNull();
    }

    [Fact]
    public void AbjadifyText_ShouldHandleNumbersAndSymbols_WhenGivenMixedContent()
    {
        var result = _component.AbjadifyText("Test123 @#$ email@example.com");
        result.Should().Be("Tst123 @#$ ml@xmpl.cm");
    }

    [Fact]
    public void AbjadifyText_ShouldHandleComplexSentence_WhenGivenRealWorldExample()
    {
        var result = _component.AbjadifyText("The quick brown fox can't jump over a lazy dog's fence!");
        result.Should().Be("Th qck brwn fx cn't jmp vr a lzy dg's fnc!");
    }

    [Fact]
    public void AbjadifyText_ShouldPreserveWordBoundaries_WhenGivenHyphenatedWords()
    {
        var result = _component.AbjadifyText("state-of-the-art");
        result.Should().Be("stt-f-th-rt");
    }

    [Theory]
    [InlineData("aeiou", "")]
    [InlineData("AEIOU", "")]
    [InlineData("bcdfg", "bcdfg")]
    [InlineData("AeIoU", "")]
    public void AbjadifyText_ShouldHandleVowelOnlyWords_WhenGivenSpecialCases(string input, string expected)
    {
        var result = _component.AbjadifyText(input);
        result.Should().Be(expected);
    }

    [Fact]
    public async Task AbjadifyFileAsync_ShouldProcessFile_WhenValidFileProvided()
    {
        var inputFile = Path.Combine(_testDirectory, "input.txt");
        var outputFile = Path.Combine(_testDirectory, "output.txt");
        var content = "Hello world! This is a test.";

        await File.WriteAllTextAsync(inputFile, content);

        await _component.AbjadifyFileAsync(inputFile, outputFile);

        var result = await File.ReadAllTextAsync(outputFile);
        result.Should().Be("Hll wrld! Ths s a tst.");
    }

    [Fact]
    public async Task AbjadifyFileAsync_ShouldThrowFileNotFoundException_WhenInputFileDoesNotExist()
    {
        var inputFile = Path.Combine(_testDirectory, "nonexistent.txt");
        var outputFile = Path.Combine(_testDirectory, "output.txt");

        var act = async () => await _component.AbjadifyFileAsync(inputFile, outputFile);

        await act.Should().ThrowAsync<FileNotFoundException>()
            .WithMessage($"Input file '{inputFile}' does not exist.");
    }

    [Fact]
    public async Task AbjadifyFileAsync_ShouldCreateOutputFile_WhenOutputDirectoryExists()
    {
        var inputFile = Path.Combine(_testDirectory, "input.txt");
        var outputFile = Path.Combine(_testDirectory, "output.txt");
        var content = "Test content";

        await File.WriteAllTextAsync(inputFile, content);

        await _component.AbjadifyFileAsync(inputFile, outputFile);

        File.Exists(outputFile).Should().BeTrue();
    }

    [Fact]
    public async Task AbjadifyFileAsync_ShouldHandleEmptyFile_WhenInputFileIsEmpty()
    {
        var inputFile = Path.Combine(_testDirectory, "empty.txt");
        var outputFile = Path.Combine(_testDirectory, "output.txt");

        await File.WriteAllTextAsync(inputFile, "");

        await _component.AbjadifyFileAsync(inputFile, outputFile);

        var result = await File.ReadAllTextAsync(outputFile);
        result.Should().Be("");
    }

    [Fact]
    public async Task AbjadifyFileAsync_ShouldHandleLargeFile_WhenInputFileIsLarge()
    {
        var inputFile = Path.Combine(_testDirectory, "large.txt");
        var outputFile = Path.Combine(_testDirectory, "output.txt");
        var content = string.Join(" ", Enumerable.Repeat("hello world", 1000));

        await File.WriteAllTextAsync(inputFile, content);

        await _component.AbjadifyFileAsync(inputFile, outputFile);

        var result = await File.ReadAllTextAsync(outputFile);
        result.Should().StartWith("hll wrld");
        result.Should().Contain("hll wrld");
    }
}