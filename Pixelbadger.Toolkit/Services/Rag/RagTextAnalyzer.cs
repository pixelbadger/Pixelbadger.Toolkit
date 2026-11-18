using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Analysis.En;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Analysis.TokenAttributes;
using Lucene.Net.Analysis.Util;
using Lucene.Net.Util;

namespace Pixelbadger.Toolkit.Services.Rag;

/// <summary>
/// The analyzer shared by indexing and querying of the content and title fields:
/// StandardTokenizer, possessive removal, lower-casing, RAG stop words, Porter stemming.
/// </summary>
public sealed class RagTextAnalyzer : Analyzer
{
    private const LuceneVersion Version = LuceneVersion.LUCENE_48;

    /// <summary>The NLTK English stop-word list minus the negations "no", "nor" and "not".</summary>
    public static readonly IReadOnlyList<string> StopWords = new[]
    {
        "i", "me", "my", "myself", "we", "our", "ours", "ourselves", "you", "you're", "you've", "you'll", "you'd",
        "your", "yours", "yourself", "yourselves", "he", "him", "his", "himself", "she", "she's", "her", "hers",
        "herself", "it", "it's", "its", "itself", "they", "them", "their", "theirs", "themselves", "what", "which",
        "who", "whom", "this", "that", "that'll", "these", "those", "am", "is", "are", "was", "were", "be", "been",
        "being", "have", "has", "had", "having", "do", "does", "did", "doing", "a", "an", "the", "and", "but", "if",
        "or", "because", "as", "until", "while", "of", "at", "by", "for", "with", "about", "against", "between",
        "into", "through", "during", "before", "after", "above", "below", "to", "from", "up", "down", "in", "out",
        "on", "off", "over", "under", "again", "further", "then", "once", "here", "there", "when", "where", "why",
        "how", "all", "any", "both", "each", "few", "more", "most", "other", "some", "such", "only", "own", "same",
        "so", "than", "too", "very", "s", "t", "can", "will", "just", "don", "don't", "should", "should've", "now",
        "d", "ll", "m", "o", "re", "ve", "y", "ain", "aren", "aren't", "couldn", "couldn't", "didn", "didn't",
        "doesn", "doesn't", "hadn", "hadn't", "hasn", "hasn't", "haven", "haven't", "isn", "isn't", "ma", "mightn",
        "mightn't", "mustn", "mustn't", "needn", "needn't", "shan", "shan't", "shouldn", "shouldn't", "wasn",
        "wasn't", "weren", "weren't", "won", "won't", "wouldn", "wouldn't",
    };

    private static readonly CharArraySet StopSet = new(Version, StopWords, false);

    protected override TokenStreamComponents CreateComponents(string fieldName, TextReader reader)
    {
        var source = new StandardTokenizer(Version, reader);
        TokenStream stream = new EnglishPossessiveFilter(Version, source);
        stream = new LowerCaseFilter(Version, stream);
        stream = new StopFilter(Version, stream, StopSet);
        stream = new PorterStemFilter(stream);
        return new TokenStreamComponents(source, stream);
    }

    /// <summary>Returns the analysed terms of <paramref name="text"/> in order, duplicates included.</summary>
    public static IReadOnlyList<string> Tokenize(string text)
    {
        using var analyzer = new RagTextAnalyzer();
        var terms = new List<string>();
        using var stream = analyzer.GetTokenStream("content", new StringReader(text));
        var term = stream.AddAttribute<ICharTermAttribute>();
        stream.Reset();
        while (stream.IncrementToken()) terms.Add(term.ToString());
        stream.End();
        return terms;
    }
}
