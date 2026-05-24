using BenchmarkDotNet.Attributes;
using Pixelbadger.Toolkit.Components;

namespace Pixelbadger.Toolkit.Benchmarks;

[MemoryDiagnoser]
public class AbjadifyBenchmarks
{
    private AbjadifyComponent _component = null!;

    private const string ShortText = "The quick brown fox jumps over the lazy dog.";

    private const string ParagraphText =
        "Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed do eiusmod tempor " +
        "incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud " +
        "exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat. Duis aute irure " +
        "dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur.";

    private static readonly string DocumentText = string.Concat(Enumerable.Repeat(ParagraphText + " ", 20));

    [GlobalSetup]
    public void Setup()
    {
        _component = new AbjadifyComponent();
    }

    [Benchmark]
    public string Abjadify_ShortText() =>
        _component.AbjadifyText(ShortText);

    [Benchmark]
    public string Abjadify_Paragraph() =>
        _component.AbjadifyText(ParagraphText);

    [Benchmark]
    public string Abjadify_Document() =>
        _component.AbjadifyText(DocumentText);
}
