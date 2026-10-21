using BenchmarkDotNet.Attributes;
using Pixelbadger.Toolkit.Components;

namespace Pixelbadger.Toolkit.Benchmarks;

[MemoryDiagnoser]
public class OokTranslationBenchmarks
{
    // Same programs as BrainfuckBenchmarks.cs
    private const string HelloWorldBrainfuck =
        "++++++++++[>+++++++>++++++++++>+++>+<<<<-]>++.>+.+++++++..+++.>++.<<+++++++++++++++.>.+++.------.--------.>+.>.";

    private const string MultiplyIntensiveBrainfuck =
        "++++++++++[>++++++++++[>++++++++++<-]<-]>>.";

    private OokBrainfuckTranslator _translator = null!;
    private string _helloWorldOok = null!;
    private string _multiplyOok = null!;

    [GlobalSetup]
    public void Setup()
    {
        _translator = new OokBrainfuckTranslator();
        _helloWorldOok = _translator.TranslateBrainfuckToOok(HelloWorldBrainfuck);
        _multiplyOok = _translator.TranslateBrainfuckToOok(MultiplyIntensiveBrainfuck);
    }

    [Benchmark]
    public string BrainfuckToOok_HelloWorld() =>
        _translator.TranslateBrainfuckToOok(HelloWorldBrainfuck);

    [Benchmark]
    public string BrainfuckToOok_MultiplyIntensive() =>
        _translator.TranslateBrainfuckToOok(MultiplyIntensiveBrainfuck);

    [Benchmark]
    public string OokToBrainfuck_HelloWorld() =>
        _translator.TranslateOokToBrainfuck(_helloWorldOok);

    [Benchmark]
    public string OokToBrainfuck_MultiplyIntensive() =>
        _translator.TranslateOokToBrainfuck(_multiplyOok);
}
