using System.Text;
using System.Text.RegularExpressions;

namespace Pixelbadger.Toolkit.Services;

/// <summary>
/// Byte-level byte-pair-encoding tokenizer in the style of Karpathy's minbpe / GPT-2.
/// The base vocabulary is the 256 possible bytes; training then repeatedly merges the most
/// frequent adjacent token pair into a new token until the target vocabulary size is reached.
///
/// Text is first split into chunks with a GPT-2-style regex so that merges never cross word or
/// whitespace boundaries. Training is word-frequency weighted (it operates over the set of
/// distinct chunks scaled by their counts) so it stays fast even on large, repetitive corpora,
/// and per-chunk encodings are memoized. Because it works on raw UTF-8 bytes, any input is
/// representable — there is no out-of-vocabulary character.
/// </summary>
public sealed partial class BpeTokenizer : ITokenizer
{
    private const int ByteVocabSize = 256;

    // GPT-2 pre-tokenization pattern: contractions, letter runs, digit runs, punctuation runs, whitespace.
    [GeneratedRegex(@"'(?:[sdmt]|ll|ve|re)| ?\p{L}+| ?\p{N}+| ?[^\s\p{L}\p{N}]+|\s+(?!\S)|\s+", RegexOptions.Compiled)]
    private static partial Regex SplitPattern();

    // Ordered merges: _merges[i] = (a, b) mints token id 256 + i.
    private readonly (int A, int B)[] _merges;
    // (a, b) packed -> new token id, for encoding.
    private readonly Dictionary<long, int> _pairToId;
    // id -> the raw bytes it expands to, for decoding.
    private readonly byte[][] _vocab;
    // chunk text -> encoded ids, memoized across a corpus encode.
    private readonly Dictionary<string, int[]> _encodeCache = new();

    public int VocabSize => _vocab.Length;
    public IReadOnlyList<(int A, int B)> Merges => _merges;

    private BpeTokenizer((int A, int B)[] merges)
    {
        _merges = merges;
        _pairToId = new Dictionary<long, int>(merges.Length);

        _vocab = new byte[ByteVocabSize + merges.Length][];
        for (int i = 0; i < ByteVocabSize; i++)
            _vocab[i] = new[] { (byte)i };

        for (int i = 0; i < merges.Length; i++)
        {
            int id = ByteVocabSize + i;
            _pairToId[Pack(merges[i].A, merges[i].B)] = id;
            var left = _vocab[merges[i].A];
            var right = _vocab[merges[i].B];
            var combined = new byte[left.Length + right.Length];
            Buffer.BlockCopy(left, 0, combined, 0, left.Length);
            Buffer.BlockCopy(right, 0, combined, left.Length, right.Length);
            _vocab[id] = combined;
        }
    }

    /// <summary>
    /// Trains a BPE tokenizer on <paramref name="corpus"/> up to a total vocabulary of
    /// <paramref name="vocabSize"/> tokens (which must be at least 256, the byte base). Fewer
    /// merges are performed if the corpus runs out of repeated adjacent pairs.
    /// </summary>
    public static BpeTokenizer Build(string corpus, int vocabSize)
    {
        if (string.IsNullOrEmpty(corpus))
            throw new ArgumentException("Cannot build a tokenizer from an empty corpus.", nameof(corpus));
        if (vocabSize < ByteVocabSize)
            throw new ArgumentException(
                $"vocab-size must be at least {ByteVocabSize} (the byte-level base vocabulary).", nameof(vocabSize));

        int numMerges = vocabSize - ByteVocabSize;

        // Word-frequency-weighted training: collapse the corpus to distinct chunks with counts.
        var wordCounts = new Dictionary<string, int>();
        foreach (Match m in SplitPattern().Matches(corpus))
            wordCounts[m.Value] = wordCounts.GetValueOrDefault(m.Value) + 1;

        // Each distinct chunk becomes a mutable list of token ids (initially raw bytes), plus its count.
        var words = new List<List<int>>(wordCounts.Count);
        var counts = new List<int>(wordCounts.Count);
        foreach (var (word, count) in wordCounts)
        {
            var bytes = Encoding.UTF8.GetBytes(word);
            var ids = new List<int>(bytes.Length);
            foreach (var b in bytes)
                ids.Add(b);
            words.Add(ids);
            counts.Add(count);
        }

        var merges = new List<(int A, int B)>(numMerges);
        var pairCounts = new Dictionary<long, long>();

        for (int merge = 0; merge < numMerges; merge++)
        {
            pairCounts.Clear();
            for (int w = 0; w < words.Count; w++)
            {
                var ids = words[w];
                long count = counts[w];
                for (int i = 0; i + 1 < ids.Count; i++)
                {
                    long key = Pack(ids[i], ids[i + 1]);
                    pairCounts[key] = pairCounts.GetValueOrDefault(key) + count;
                }
            }

            if (pairCounts.Count == 0)
                break;

            // Highest frequency wins; ties broken by the packed pair value for determinism.
            long bestKey = 0;
            long bestCount = -1;
            foreach (var (key, count) in pairCounts)
            {
                if (count > bestCount || (count == bestCount && key < bestKey))
                {
                    bestCount = count;
                    bestKey = key;
                }
            }

            if (bestCount < 2)
                break; // No repeated pair left; further merges would not generalize.

            var (a, b) = Unpack(bestKey);
            int newId = ByteVocabSize + merges.Count;
            merges.Add((a, b));

            foreach (var ids in words)
                MergePairInPlace(ids, a, b, newId);
        }

        return new BpeTokenizer(merges.ToArray());
    }

    public static BpeTokenizer FromState(TokenizerState state)
    {
        if (state.Merges is null)
            throw new ArgumentException("BPE tokenizer state is missing its merges.", nameof(state));
        var merges = new (int A, int B)[state.Merges.Length];
        for (int i = 0; i < state.Merges.Length; i++)
        {
            var pair = state.Merges[i];
            if (pair.Length != 2)
                throw new ArgumentException($"Merge {i} is malformed (expected 2 entries, got {pair.Length}).", nameof(state));
            merges[i] = (pair[0], pair[1]);
        }
        return new BpeTokenizer(merges);
    }

    public TokenizerState ExportState()
    {
        var merges = new int[_merges.Length][];
        for (int i = 0; i < _merges.Length; i++)
            merges[i] = new[] { _merges[i].A, _merges[i].B };
        return new TokenizerState(TokenizerKind.Bpe, Vocabulary: null, merges);
    }

    public int[] Encode(string text)
    {
        if (text.Length == 0)
            return Array.Empty<int>();

        var result = new List<int>();
        foreach (Match m in SplitPattern().Matches(text))
            result.AddRange(EncodeChunk(m.Value));
        return result.ToArray();
    }

    private int[] EncodeChunk(string chunk)
    {
        if (_encodeCache.TryGetValue(chunk, out var cached))
            return cached;

        var bytes = Encoding.UTF8.GetBytes(chunk);
        var ids = new List<int>(bytes.Length);
        foreach (var b in bytes)
            ids.Add(b);

        // Greedily apply the lowest-ranked (earliest learned) merge until none apply.
        while (ids.Count >= 2)
        {
            int bestA = 0, bestB = 0, bestId = int.MaxValue;
            for (int i = 0; i + 1 < ids.Count; i++)
            {
                if (_pairToId.TryGetValue(Pack(ids[i], ids[i + 1]), out var id) && id < bestId)
                {
                    bestId = id;
                    bestA = ids[i];
                    bestB = ids[i + 1];
                }
            }

            if (bestId == int.MaxValue)
                break;

            MergePairInPlace(ids, bestA, bestB, bestId);
        }

        var encoded = ids.ToArray();
        _encodeCache[chunk] = encoded;
        return encoded;
    }

    public string Decode(IReadOnlyList<int> ids)
    {
        var bytes = new List<byte>(ids.Count);
        foreach (var id in ids)
        {
            if (id < 0 || id >= _vocab.Length)
                throw new ArgumentException($"Token id {id} is outside the vocabulary of size {_vocab.Length}.");
            bytes.AddRange(_vocab[id]);
        }
        // Invalid byte sequences decode to the Unicode replacement character (U+FFFD).
        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    /// <summary>Replaces every adjacent (<paramref name="a"/>, <paramref name="b"/>) pair with <paramref name="newId"/>.</summary>
    private static void MergePairInPlace(List<int> ids, int a, int b, int newId)
    {
        int write = 0;
        for (int read = 0; read < ids.Count; read++)
        {
            if (read + 1 < ids.Count && ids[read] == a && ids[read + 1] == b)
            {
                ids[write++] = newId;
                read++; // consume the pair
            }
            else
            {
                ids[write++] = ids[read];
            }
        }
        ids.RemoveRange(write, ids.Count - write);
    }

    private static long Pack(int a, int b) => ((long)a << 32) | (uint)b;

    private static (int A, int B) Unpack(long key) => ((int)(key >> 32), (int)(key & 0xFFFFFFFF));
}
