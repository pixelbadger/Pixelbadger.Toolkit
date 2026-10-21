using System.Text;
using BenchmarkDotNet.Attributes;
using Pixelbadger.Toolkit.Components;

namespace Pixelbadger.Toolkit.Benchmarks;

[MemoryDiagnoser]
public class StringsBenchmarks
{
    private const int SmallFileBytes = 1024;
    private const int LargeFileBytes = 1024 * 1024;

    private const string ShortText = "The quick brown fox jumps over the lazy dog near the quiet riverbank.";

    private static readonly string[] Words =
    [
        "river", "lantern", "signal", "garden", "pattern", "station", "window", "harbour",
        "measure", "shadow", "portal", "meadow", "compile", "vector", "silence", "bridge",
    ];

    private string _tempDir = null!;
    private string _smallInputPath = null!;
    private string _largeInputPath = null!;
    private string _smallOutputPath = null!;
    private string _largeOutputPath = null!;
    private string _longText = null!;
    private StringReverser _reverser = null!;
    private StringReportComponent _report = null!;

    [GlobalSetup]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "pbtk-benchmarks-strings");
        Directory.CreateDirectory(_tempDir);

        _smallInputPath = Path.Combine(_tempDir, "small-in.txt");
        _largeInputPath = Path.Combine(_tempDir, "large-in.txt");
        _smallOutputPath = Path.Combine(_tempDir, "small-out.txt");
        _largeOutputPath = Path.Combine(_tempDir, "large-out.txt");

        File.WriteAllText(_smallInputPath, GenerateAscii(SmallFileBytes));
        File.WriteAllText(_largeInputPath, GenerateAscii(LargeFileBytes));

        _longText = GenerateLongText(10 * 1024);
        _reverser = new StringReverser();
        _report = new StringReportComponent();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Benchmark]
    public async Task ReverseFile_1KB() =>
        await _reverser.ReverseFileAsync(_smallInputPath, _smallOutputPath);

    [Benchmark]
    public async Task ReverseFile_1MB() =>
        await _reverser.ReverseFileAsync(_largeInputPath, _largeOutputPath);

    [Benchmark]
    public object AnalyzeText_Short() =>
        _report.AnalyzeText(ShortText);

    [Benchmark]
    public object AnalyzeText_Long() =>
        _report.AnalyzeText(_longText);

    // Deterministic ASCII of exactly `length` characters, with spaces and newlines.
    private static string GenerateAscii(int length)
    {
        var sb = new StringBuilder(length);
        int i = 0;
        while (sb.Length < length)
        {
            sb.Append((char)('a' + (i * 7) % 26));
            if (i % 9 == 8) sb.Append(' ');
            if (i % 71 == 70) sb.Append('\n');
            i++;
        }
        sb.Length = length;
        return sb.ToString();
    }

    // Deterministic prose: fixed word cycle, sentences of 8-16 words, paragraph breaks.
    private static string GenerateLongText(int minLength)
    {
        var sb = new StringBuilder(minLength + 256);
        int word = 0;
        while (sb.Length < minLength)
        {
            int sentenceWords = 8 + (word % 9);
            for (int w = 0; w < sentenceWords; w++)
            {
                var token = Words[word % Words.Length];
                if (w == 0) token = char.ToUpperInvariant(token[0]) + token[1..];
                sb.Append(token);
                sb.Append(w == sentenceWords - 1 ? ". " : " ");
                word++;
            }
            if (word % 97 < 8) sb.Append("\n\n");
        }
        return sb.ToString();
    }
}
