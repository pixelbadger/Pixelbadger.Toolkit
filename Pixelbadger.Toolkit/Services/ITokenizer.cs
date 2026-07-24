namespace Pixelbadger.Toolkit.Services;

/// <summary>The tokenization scheme a checkpoint was trained with.</summary>
public enum TokenizerKind
{
    /// <summary>One token per distinct character in the corpus.</summary>
    Char,
    /// <summary>Byte-level byte-pair encoding: subword tokens learned by merging frequent byte pairs.</summary>
    Bpe
}

/// <summary>
/// A serialized, reconstructable description of a trained tokenizer. Persisted inside the
/// checkpoint config so <c>gpt complete</c> can rebuild the exact tokenizer used for training.
/// </summary>
/// <param name="Kind">Which tokenizer produced this state.</param>
/// <param name="Vocabulary">For <see cref="TokenizerKind.Char"/>: the ordered character vocabulary. Null otherwise.</param>
/// <param name="Merges">For <see cref="TokenizerKind.Bpe"/>: ordered <c>[a, b]</c> pairs; pair <c>i</c> mints token id <c>256 + i</c>. Null otherwise.</param>
public record TokenizerState(TokenizerKind Kind, string? Vocabulary, int[][]? Merges);

/// <summary>Maps between text and integer token ids for a GPT model.</summary>
public interface ITokenizer
{
    int VocabSize { get; }

    /// <summary>Encodes text into token ids.</summary>
    int[] Encode(string text);

    /// <summary>Decodes token ids back into text.</summary>
    string Decode(IReadOnlyList<int> ids);

    /// <summary>Exports a serializable description sufficient to reconstruct this tokenizer.</summary>
    TokenizerState ExportState();
}

/// <summary>Reconstructs an <see cref="ITokenizer"/> from persisted <see cref="TokenizerState"/>.</summary>
public static class Tokenizers
{
    public static ITokenizer Restore(TokenizerState state) => state.Kind switch
    {
        TokenizerKind.Char => CharTokenizer.FromState(state),
        TokenizerKind.Bpe => BpeTokenizer.FromState(state),
        _ => throw new ArgumentOutOfRangeException(nameof(state), $"Unknown tokenizer kind '{state.Kind}'.")
    };

    /// <summary>Builds a fresh tokenizer of the requested kind from a training corpus.</summary>
    public static ITokenizer Build(TokenizerKind kind, string corpus, int vocabSize) => kind switch
    {
        TokenizerKind.Char => CharTokenizer.Build(corpus),
        TokenizerKind.Bpe => BpeTokenizer.Build(corpus, vocabSize),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), $"Unknown tokenizer kind '{kind}'.")
    };
}
