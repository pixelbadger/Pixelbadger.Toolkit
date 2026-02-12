using FluentAssertions;
using Pixelbadger.Toolkit.Components;

namespace Pixelbadger.Toolkit.Tests;

public class FleschReadingEaseComponentTests : IDisposable
{
    private readonly FleschReadingEaseComponent _component;
    private readonly string _testDirectory;

    public FleschReadingEaseComponentTests()
    {
        _component = new FleschReadingEaseComponent();
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
    public async Task AnalyzeFileAsync_ShouldReturnResult_WhenValidTextFileProvided()
    {
        var inputFile = Path.Combine(_testDirectory, "sample.txt");
        await File.WriteAllTextAsync(inputFile, "The cat sat on the mat.");

        var result = await _component.AnalyzeFileAsync(inputFile);

        result.Sentences.Should().Be(1);
        result.Words.Should().Be(6);
        result.Syllables.Should().Be(6);
        result.Score.Should().Be(116.15);
        result.ReadabilityBand.Should().Be("Very easy");
    }

    [Fact]
    public async Task AnalyzeFileAsync_ShouldThrowFileNotFoundException_WhenInputFileDoesNotExist()
    {
        var inputFile = Path.Combine(_testDirectory, "missing.txt");

        var act = async () => await _component.AnalyzeFileAsync(inputFile);

        await act.Should().ThrowAsync<FileNotFoundException>()
            .WithMessage($"Input file '{inputFile}' does not exist.");
    }

    [Fact]
    public void AnalyzeText_ShouldReturnZeroedResult_WhenInputIsWhitespace()
    {
        var result = _component.AnalyzeText("   \n\t  ");

        result.Score.Should().Be(0);
        result.Sentences.Should().Be(0);
        result.Words.Should().Be(0);
        result.Syllables.Should().Be(0);
        result.ReadabilityBand.Should().Be("N/A (empty input)");
    }

    [Fact]
    public void AnalyzeText_ShouldTreatTextWithoutSentenceTerminators_AsSingleSentence()
    {
        var result = _component.AnalyzeText("Simple text without punctuation");

        result.Sentences.Should().Be(1);
        result.Words.Should().Be(4);
        result.Syllables.Should().BeGreaterThan(0);
    }

    [Fact]
    public void AnalyzeText_ShouldCountMultipleSentenceTerminators_AsMultipleSentences()
    {
        var result = _component.AnalyzeText("One. Two? Three!");

        result.Sentences.Should().Be(3);
        result.Words.Should().Be(3);
        result.Syllables.Should().Be(3);
    }

    [Theory]
    [InlineData("Cat sat.", "Very easy")]
    [InlineData("Antidisestablishmentarianism.", "Very confusing")]
    public void AnalyzeText_ShouldAssignReadabilityBand_WhenScoreCalculated(string input, string expectedBand)
    {
        var result = _component.AnalyzeText(input);

        result.ReadabilityBand.Should().Be(expectedBand);
    }
}
