using System.Diagnostics;

namespace Pixelbadger.Toolkit.Tests;

internal static class OggTestSupport
{
    public static (int ExitCode, string StdOut, string StdErr) Run(string exe, params string[] args)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEndAsync();
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, output, err.Result);
    }

    public static void GenerateOpus(string path, int channels = 1, double seconds = 1)
    {
        var r = Run("ffmpeg", "-v", "error", "-y", "-f", "lavfi", "-i",
            $"sine=frequency=440:duration={seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            "-c:a", "libopus", "-b:a", "64k", "-ac", channels.ToString(), path);
        if (r.ExitCode != 0) throw new InvalidOperationException("ffmpeg failed: " + r.StdErr);
    }
}
