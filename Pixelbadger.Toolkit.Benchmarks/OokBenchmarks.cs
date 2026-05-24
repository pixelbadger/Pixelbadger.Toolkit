using BenchmarkDotNet.Attributes;
using Pixelbadger.Toolkit.Components;

namespace Pixelbadger.Toolkit.Benchmarks;

[MemoryDiagnoser]
public class OokBenchmarks
{
    private OokBrainfuckTranslator _translator = null!;
    private OokInterpreter _interpreter = null!;

    // 72 increments + output (~150 chars of BF)
    private const string ShortBrainfuck =
        "+++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++.";

    // Classic Hello World (~110 chars of BF, translates to ~660 Ook tokens)
    private const string HelloWorldBrainfuck =
        "++++++++++[>+++++++>++++++++++>+++>+<<<<-]>++.>+.+++++++..+++.>++.<<+++++++++++++++.>.+++.------.--------.>+.>.";

    private string _shortOok = null!;
    private string _helloWorldOok = null!;

    [GlobalSetup]
    public void Setup()
    {
        _translator = new OokBrainfuckTranslator();
        _interpreter = new OokInterpreter();
        _shortOok = _translator.TranslateBrainfuckToOok(ShortBrainfuck);
        _helloWorldOok = _translator.TranslateBrainfuckToOok(HelloWorldBrainfuck);
    }

    [Benchmark]
    public string TranslateBrainfuckToOok_Short() =>
        _translator.TranslateBrainfuckToOok(ShortBrainfuck);

    [Benchmark]
    public string TranslateBrainfuckToOok_HelloWorld() =>
        _translator.TranslateBrainfuckToOok(HelloWorldBrainfuck);

    [Benchmark]
    public string TranslateOokToBrainfuck_Short() =>
        _translator.TranslateOokToBrainfuck(_shortOok);

    [Benchmark]
    public string TranslateOokToBrainfuck_HelloWorld() =>
        _translator.TranslateOokToBrainfuck(_helloWorldOok);

    [Benchmark]
    public string ExecuteOok_Short() =>
        _interpreter.Execute(_shortOok);

    [Benchmark]
    public string ExecuteOok_HelloWorld() =>
        _interpreter.Execute(_helloWorldOok);
}
