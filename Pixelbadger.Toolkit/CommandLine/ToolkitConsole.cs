using Spectre.Console;
using Spectre.Console.Rendering;

namespace Pixelbadger.Toolkit.CommandLine;

/// <summary>
/// Per-async-flow output capture so commands can run in-process (for example inside a server) without
/// writing to the process-wide console. In CLI mode nothing is captured and everything falls through.
/// </summary>
public static class ToolkitConsole
{
    private sealed record Captured(IAnsiConsole Console, TextWriter Output, TextWriter Error);

    private static readonly AsyncLocal<Captured?> Current = new();
    private static readonly object RoutingLock = new();
    private static RoutingConsole? _routing;

    public static TextWriter Out => Current.Value?.Output ?? Console.Out;

    public static TextWriter Error => Current.Value?.Error ?? Console.Error;

    /// <summary>Empty stream when the current flow is captured (never the host process's stdin); else the real stdin.</summary>
    public static Stream OpenStandardInput() => Current.Value is not null ? Stream.Null : Console.OpenStandardInput();

    /// <summary>Captures output for the current async flow; dispose to restore the previous values.</summary>
    public static IDisposable Capture(IAnsiConsole console, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        var previous = Current.Value;
        Current.Value = new Captured(console, output, error);
        return new Scope(previous);
    }

    /// <summary>A plain-text Spectre console writing to <paramref name="writer"/>: no ANSI, no colour, very wide.</summary>
    public static IAnsiConsole CreateCaptureConsole(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.No,
            // Default enrichers (GitHub Actions, Azure DevOps, ...) force Ansi on from CI env vars after settings apply.
            Enrichment = new ProfileEnrichment { UseDefaultEnrichers = false },
            Out = new AnsiConsoleOutput(writer)
        });
        console.Profile.Capabilities.Ansi = false;
        console.Profile.Width = 4096;
        return console;
    }

    /// <summary>
    /// Idempotently replaces <see cref="AnsiConsole.Console"/> with a console that routes to the current flow's
    /// captured console when set, else to the console that was current at first install.
    /// </summary>
    public static void InstallRouting()
    {
        lock (RoutingLock)
        {
            if (_routing is not null && ReferenceEquals(AnsiConsole.Console, _routing)) return;
            _routing = new RoutingConsole(AnsiConsole.Console);
            AnsiConsole.Console = _routing;
        }
    }

    private sealed class Scope(Captured? previous) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                Current.Value = previous;
        }
    }

    private sealed class RoutingConsole(IAnsiConsole fallback) : IAnsiConsole
    {
        private IAnsiConsole Target => Current.Value?.Console ?? fallback;

        public Profile Profile => Target.Profile;
        public IAnsiConsoleCursor Cursor => Target.Cursor;
        public IAnsiConsoleInput Input => Target.Input;
        public IExclusivityMode ExclusivityMode => Target.ExclusivityMode;
        public RenderPipeline Pipeline => Target.Pipeline;
        public void Clear(bool home) => Target.Clear(home);
        public void Write(IRenderable renderable) => Target.Write(renderable);
        public void WriteAnsi(Action<AnsiWriter> action) => Target.WriteAnsi(action);
    }
}
