using System.Text;
using BenchmarkDotNet.Attributes;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Benchmarks;

[MemoryDiagnoser]
public class TokenizerBenchmarks
{
    private string _corpus = null!;
    private BpeTokenizer _bpe = null!;
    private CharTokenizer _char = null!;
    private int[] _bpeIds = null!;

    [GlobalSetup]
    public void Setup()
    {
        _corpus = GenerateCorpus(50 * 1024);
        _bpe = BpeTokenizer.Build(_corpus, 512);
        _char = CharTokenizer.Build(_corpus);
        _bpeIds = _bpe.Encode(_corpus);
    }

    [Benchmark]
    public BpeTokenizer Bpe_Build() => BpeTokenizer.Build(_corpus, 512);

    [Benchmark]
    public int[] Bpe_Encode() => _bpe.Encode(_corpus);

    [Benchmark]
    public string Bpe_Decode() => _bpe.Decode(_bpeIds);

    [Benchmark]
    public CharTokenizer Char_Build() => CharTokenizer.Build(_corpus);

    [Benchmark]
    public int[] Char_Encode() => _char.Encode(_corpus);

    // Seeded pseudo-prose: words of 2-9 lowercase letters from a fixed 400-word vocabulary.
    private static string GenerateCorpus(int targetBytes)
    {
        var rng = new Random(1337);
        var words = new string[400];
        for (int i = 0; i < words.Length; i++)
        {
            var chars = new char[rng.Next(2, 10)];
            for (int c = 0; c < chars.Length; c++)
                chars[c] = (char)('a' + rng.Next(26));
            words[i] = new string(chars);
        }

        var sb = new StringBuilder(targetBytes + 16);
        while (sb.Length < targetBytes)
        {
            sb.Append(words[rng.Next(words.Length)]);
            sb.Append(rng.Next(10) == 0 ? ". " : " ");
        }
        return sb.ToString();
    }
}
