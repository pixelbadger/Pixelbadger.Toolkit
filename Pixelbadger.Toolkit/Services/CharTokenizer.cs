namespace Pixelbadger.Toolkit.Services;

/// <summary>
/// Character-level tokenizer. The vocabulary is the sorted set of distinct characters in the
/// training corpus, mirroring the approach used in Karpathy's char-level demos.
/// </summary>
public sealed class CharTokenizer
{
    private readonly Dictionary<char, int> _stoi;
    private readonly char[] _itos;

    public int VocabSize => _itos.Length;
    public IReadOnlyList<char> Vocabulary => _itos;

    private CharTokenizer(char[] itos)
    {
        _itos = itos;
        _stoi = new Dictionary<char, int>(itos.Length);
        for (int i = 0; i < itos.Length; i++)
            _stoi[itos[i]] = i;
    }

    public static CharTokenizer Build(string corpus)
    {
        if (string.IsNullOrEmpty(corpus))
            throw new ArgumentException("Cannot build a tokenizer from an empty corpus.", nameof(corpus));

        var itos = corpus.Distinct().OrderBy(c => c).ToArray();
        return new CharTokenizer(itos);
    }

    public static CharTokenizer FromVocabulary(IEnumerable<char> vocabulary)
        => new(vocabulary.ToArray());

    public int[] Encode(string text)
    {
        var ids = new int[text.Length];
        for (int i = 0; i < text.Length; i++)
        {
            if (!_stoi.TryGetValue(text[i], out var id))
                throw new ArgumentException($"Character '{text[i]}' is not present in the model vocabulary.");
            ids[i] = id;
        }
        return ids;
    }

    public string Decode(IReadOnlyList<int> ids)
    {
        var chars = new char[ids.Count];
        for (int i = 0; i < ids.Count; i++)
            chars[i] = _itos[ids[i]];
        return new string(chars);
    }
}
