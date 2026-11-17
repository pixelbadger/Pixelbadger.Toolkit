using Pixelbadger.Toolkit.CommandLine;

namespace Pixelbadger.Toolkit.Tests;

internal static class RagTestSupport
{
    public sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>Runs <paramref name="action"/> with ToolkitConsole output captured; returns the exit code and captured stdout/stderr.</summary>
    public static (int Code, string Out, string Err) Capture(Func<int> action)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        ToolkitConsole.InstallRouting();
        using (ToolkitConsole.Capture(ToolkitConsole.CreateCaptureConsole(output), output, error))
        {
            var code = action();
            return (code, output.ToString(), error.ToString());
        }
    }

    public static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pbtk-rag-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
