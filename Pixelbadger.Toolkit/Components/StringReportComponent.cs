using System.Text.RegularExpressions;

namespace Pixelbadger.Toolkit.Components;

public class StringReportComponent
{
    private static readonly Regex WordRegex = new Regex(@"[A-Za-z]+(?:'[A-Za-z]+)?", RegexOptions.Compiled);
    private static readonly Regex SentenceRegex = new Regex(@"[.!?]+", RegexOptions.Compiled);
    private static readonly Regex ParagraphRegex = new Regex(@"\n\s*\n", RegexOptions.Compiled);

    private readonly FleschReadingEaseComponent _fleschComponent;

    public StringReportComponent() : this(new FleschReadingEaseComponent()) { }

    public StringReportComponent(FleschReadingEaseComponent fleschComponent)
    {
        _fleschComponent = fleschComponent;
    }

    public async Task<StringReportResult> AnalyzeFileAsync(string inputFilePath)
    {
        if (string.IsNullOrWhiteSpace(inputFilePath))
            throw new ArgumentException("Input file path cannot be null or empty.", nameof(inputFilePath));

        if (!File.Exists(inputFilePath))
            throw new FileNotFoundException($"Input file '{inputFilePath}' does not exist.");

        var content = await File.ReadAllTextAsync(inputFilePath);
        return AnalyzeText(content);
    }

    public StringReportResult AnalyzeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new StringReportResult(
                Characters: 0,
                CharactersNoSpaces: 0,
                Words: 0,
                UniqueWords: 0,
                Sentences: 0,
                Paragraphs: 0,
                AverageWordsPerSentence: 0,
                AverageSentencesPerParagraph: 0,
                EstimatedPages: 0,
                EstimatedReadingTimeSeconds: 0,
                FleschReadingEase: 0,
                ReadabilityBand: "N/A (empty input)",
                LongestWord: string.Empty,
                MostCommonWord: string.Empty);
        }

        var characterCount = text.Length;
        var characterCountNoSpaces = text.Count(c => !char.IsWhiteSpace(c));

        var wordMatches = WordRegex.Matches(text);
        var words = wordMatches.Select(m => m.Value.ToLowerInvariant()).ToList();
        var wordCount = words.Count;
        var uniqueWordCount = words.Distinct().Count();

        var sentenceCount = Math.Max(1, SentenceRegex.Matches(text).Count);
        var paragraphCount = Math.Max(1, ParagraphRegex.Matches(text).Count + 1);

        var averageWordsPerSentence = Math.Round((double)wordCount / sentenceCount, 1);
        var averageSentencesPerParagraph = Math.Round((double)sentenceCount / paragraphCount, 1);

        var estimatedPageCount = (int)Math.Ceiling((double)wordCount / 250);
        var estimatedReadingTimeSeconds = (int)Math.Ceiling((double)wordCount / 238 * 60);

        var flesch = _fleschComponent.AnalyzeText(text);

        var longestWord = wordMatches.Count > 0
            ? wordMatches.OrderByDescending(m => m.Value.Length).First().Value
            : string.Empty;

        var mostCommonWord = words.Count > 0
            ? words.GroupBy(w => w).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key
            : string.Empty;

        return new StringReportResult(
            Characters: characterCount,
            CharactersNoSpaces: characterCountNoSpaces,
            Words: wordCount,
            UniqueWords: uniqueWordCount,
            Sentences: sentenceCount,
            Paragraphs: paragraphCount,
            AverageWordsPerSentence: averageWordsPerSentence,
            AverageSentencesPerParagraph: averageSentencesPerParagraph,
            EstimatedPages: estimatedPageCount,
            EstimatedReadingTimeSeconds: estimatedReadingTimeSeconds,
            FleschReadingEase: flesch.Score,
            ReadabilityBand: flesch.ReadabilityBand,
            LongestWord: longestWord,
            MostCommonWord: mostCommonWord);
    }
}

public record StringReportResult(
    int Characters,
    int CharactersNoSpaces,
    int Words,
    int UniqueWords,
    int Sentences,
    int Paragraphs,
    double AverageWordsPerSentence,
    double AverageSentencesPerParagraph,
    int EstimatedPages,
    int EstimatedReadingTimeSeconds,
    double FleschReadingEase,
    string ReadabilityBand,
    string LongestWord,
    string MostCommonWord);
