using Pixelbadger.Toolkit.Opus;

namespace Pixelbadger.Toolkit.Tests;

internal static class OpusTestHelpers
{
    internal const int Rate = 48000;
    internal const int Frame = 960;

    internal static short[] Sine(int channels, int samplesPerChannel, double frequency = 440, double amplitude = 12000)
    {
        var pcm = new short[samplesPerChannel * channels];
        for (var i = 0; i < samplesPerChannel; i++)
        {
            var v = (short)(amplitude * Math.Sin(2 * Math.PI * frequency * i / Rate));
            for (var c = 0; c < channels; c++)
                pcm[i * channels + c] = v;
        }

        return pcm;
    }

    internal static List<byte[]> EncodeFrames(IOpusEncoder encoder, short[] pcm, int channels)
    {
        var packets = new List<byte[]>();
        var buffer = new byte[4000];
        for (var offset = 0; offset + Frame * channels <= pcm.Length; offset += Frame * channels)
        {
            var n = encoder.Encode(pcm.AsSpan(offset, Frame * channels), Frame, buffer);
            packets.Add(buffer.AsSpan(0, n).ToArray());
        }

        return packets;
    }

    internal static double Correlation(double[] a, double[] b)
    {
        var n = Math.Min(a.Length, b.Length);
        double ma = 0, mb = 0;
        for (var i = 0; i < n; i++) { ma += a[i]; mb += b[i]; }
        ma /= n; mb /= n;
        double sab = 0, saa = 0, sbb = 0;
        for (var i = 0; i < n; i++)
        {
            var da = a[i] - ma; var db = b[i] - mb;
            sab += da * db; saa += da * da; sbb += db * db;
        }

        return sab / Math.Sqrt(saa * sbb);
    }
}
