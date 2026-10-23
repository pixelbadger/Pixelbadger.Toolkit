namespace Pixelbadger.Toolkit.Components;

public interface ITerminalInput
{
    bool KeyAvailable { get; }                 // non-blocking
    ConsoleKeyInfo ReadKey();                  // blocking, intercepted (not echoed)
    (int Width, int Height) Size { get; }      // current terminal size in cells
}

public sealed class ConsoleTerminalInput : ITerminalInput
{
    public bool KeyAvailable => Console.KeyAvailable;

    public ConsoleKeyInfo ReadKey() => Console.ReadKey(true);

    public (int Width, int Height) Size => (Console.WindowWidth, Console.WindowHeight);
}
