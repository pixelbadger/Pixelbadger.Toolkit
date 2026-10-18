using BenchmarkDotNet.Attributes;
using Pixelbadger.Toolkit.Components;

namespace Pixelbadger.Toolkit.Benchmarks;

[MemoryDiagnoser]
public class BrainfuckBenchmarks
{
    private BrainfuckInterpreter _interpreter = null!;
    private CancellationTokenSource _cts = null!;

    // 72 increment instructions then a single output — tests raw increment throughput
    private const string TinyProgram =
        "+++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++.";

    // Classic Hello World — exercises loop setup, nested increments, and pointer movement
    private const string HelloWorldProgram =
        "++++++++++[>+++++++>++++++++++>+++>+<<<<-]>++.>+.+++++++..+++.>++.<<+++++++++++++++.>.+++.------.--------.>+.>.";

    // Triple-nested multiplication: 10 × 10 × 10 = 1,000 innermost iterations
    private const string MultiplyIntensiveProgram =
        "++++++++++[>++++++++++[>++++++++++<-]<-]>>.";

    [GlobalSetup]
    public void Setup()
    {
        _interpreter = new BrainfuckInterpreter();
        _cts = new CancellationTokenSource();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _cts.Dispose();
    }

    [Benchmark]
    public string Execute_Tiny() =>
        _interpreter.Execute(TinyProgram);

    [Benchmark]
    public string Execute_HelloWorld() =>
        _interpreter.Execute(HelloWorldProgram);

    [Benchmark]
    public string Execute_MultiplyIntensive() =>
        _interpreter.Execute(MultiplyIntensiveProgram);

    // These measure the cancellation check added for the MCP per-call timeout (checked every 4096 instructions)
    [Benchmark]
    public string Execute_MultiplyIntensive_WithCancellationToken() =>
        _interpreter.Execute(MultiplyIntensiveProgram, _cts.Token);

    [Benchmark]
    public string Execute_HelloWorld_WithCancellationToken() =>
        _interpreter.Execute(HelloWorldProgram, _cts.Token);
}
