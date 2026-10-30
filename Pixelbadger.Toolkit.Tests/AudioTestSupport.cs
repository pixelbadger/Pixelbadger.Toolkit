using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Pixelbadger.Toolkit.Tests;

/// <summary>Helpers for the audio command tests: WAV files, ffmpeg/ffprobe verification and tone detection.</summary>
internal static class AudioTestSupport
{
    internal const int Rate = 48000;

    internal static short[] Tone(double frequency, int frames, int channels = 1, double amplitude = 12000)
    {
        var pcm = new short[frames * channels];
        for (var i = 0; i < frames; i++)
        {
            var v = (short)(amplitude * Math.Sin(2 * Math.PI * frequency * i / Rate));
            for (var c = 0; c < channels; c++)
                pcm[i * channels + c] = v;
        }

        return pcm;
    }

    internal static void WriteWav(string path, int sampleRate, int channels, short[] samples)
    {
        using var stream = File.Create(path);
        using var w = new BinaryWriter(stream, Encoding.ASCII);
        var dataBytes = samples.Length * 2;
        w.Write(Encoding.ASCII.GetBytes("RIFF"));
        w.Write(36 + dataBytes);
        w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        w.Write(16);
        w.Write((short)1);
        w.Write((short)channels);
        w.Write(sampleRate);
        w.Write(sampleRate * channels * 2);
        w.Write((short)(channels * 2));
        w.Write((short)16);
        w.Write(Encoding.ASCII.GetBytes("data"));
        w.Write(dataBytes);
        foreach (var s in samples) w.Write(s);
    }

    /// <summary>Decodes with ffmpeg's native opus decoder to mono 48 kHz s16le; fails if ffmpeg reports any error.</summary>
    internal static short[] DecodeWithFfmpeg(string path)
    {
        var psi = new ProcessStartInfo("ffmpeg")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var a in new[] { "-v", "error", "-i", path, "-f", "s16le", "-ac", "1", "-ar", "48000", "pipe:1" })
            psi.ArgumentList.Add(a);
        using var process = Process.Start(psi)!;
        var stderr = process.StandardError.ReadToEndAsync();
        using var bytes = new MemoryStream();
        process.StandardOutput.BaseStream.CopyTo(bytes);
        process.WaitForExit();
        if (process.ExitCode != 0 || stderr.Result.Length != 0)
            throw new InvalidOperationException($"ffmpeg failed to decode '{path}' (exit {process.ExitCode}): {stderr.Result}");
        var raw = bytes.ToArray();
        var samples = new short[raw.Length / 2];
        Buffer.BlockCopy(raw, 0, samples, 0, samples.Length * 2);
        return samples;
    }

    internal static (string Codec, int Channels, double Duration) Probe(string path)
    {
        var r = OggTestSupport.Run("ffprobe", "-v", "error", "-show_entries", "format=duration:stream=codec_name,channels",
            "-of", "default=nw=1", path);
        if (r.ExitCode != 0) throw new InvalidOperationException("ffprobe failed: " + r.StdErr);
        var values = r.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim().Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => p[1]);
        return (values["codec_name"], int.Parse(values["channels"], CultureInfo.InvariantCulture),
            double.Parse(values["duration"], CultureInfo.InvariantCulture));
    }

    /// <summary>Goertzel magnitude of <paramref name="frequency"/> in pcm[start..start+count), normalised to a sine amplitude.</summary>
    internal static double Magnitude(short[] pcm, int start, int count, double frequency)
    {
        var coeff = 2 * Math.Cos(2 * Math.PI * frequency / Rate);
        double s1 = 0, s2 = 0;
        for (var i = start; i < start + count; i++)
        {
            var s0 = pcm[i] + coeff * s1 - s2;
            s2 = s1;
            s1 = s0;
        }

        var power = s1 * s1 + s2 * s2 - coeff * s1 * s2;
        return 2 * Math.Sqrt(Math.Max(0, power)) / count;
    }

    /// <summary>True when <paramref name="expected"/> carries at least 10 times the energy-amplitude of <paramref name="other"/> in the window (seconds).</summary>
    internal static bool IsDominant(short[] pcm, double fromSeconds, double toSeconds, double expected, double other)
    {
        var start = (int)(fromSeconds * Rate);
        var count = (int)((toSeconds - fromSeconds) * Rate);
        return Magnitude(pcm, start, count, expected) > 10 * Magnitude(pcm, start, count, other)
               && Magnitude(pcm, start, count, expected) > 1000;
    }
}
