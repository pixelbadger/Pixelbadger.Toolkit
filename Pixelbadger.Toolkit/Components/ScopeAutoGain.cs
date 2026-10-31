namespace Pixelbadger.Toolkit.Components;

/// <summary>
/// Display-only auto-gain for the live oscilloscope. The envelope follows the recent peak with a ~1 s release,
/// measured in audio frames so the result is deterministic. Not thread-safe; callers synchronise.
/// </summary>
public sealed class ScopeAutoGain
{
    public const float MaxGain = 20f;
    public const float Target = 0.9f;
    public const float SilenceFloor = 1e-4f;
    public const int SampleRate = 48_000;

    private double _envelope;

    public double Envelope => _envelope;

    /// <summary>Current gain: 1 in silence, otherwise clamp(0.9 / envelope, 1, 20).</summary>
    public float Gain => ComputeGain(_envelope);

    /// <summary>Advances the envelope by <paramref name="frames"/> frames of audio whose retained window peaks at <paramref name="windowPeak"/>.</summary>
    public void Observe(float windowPeak, int frames)
    {
        if (float.IsNaN(windowPeak) || float.IsInfinity(windowPeak)) windowPeak = 0f;
        var decayed = _envelope * Math.Exp(-(double)Math.Max(frames, 0) / SampleRate);
        _envelope = Math.Max(Math.Abs(windowPeak), decayed);
    }

    public static float ComputeGain(double envelope)
    {
        if (envelope < SilenceFloor) return 1f;
        return (float)Math.Clamp(Target / envelope, 1.0, MaxGain);
    }

    /// <summary>Peak absolute value over both channels.</summary>
    public static float Peak(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        var peak = 0f;
        foreach (var s in left) { var a = Math.Abs(s); if (a > peak) peak = a; }
        foreach (var s in right) { var a = Math.Abs(s); if (a > peak) peak = a; }
        return peak;
    }

    /// <summary>Scaled copy, clamped to [-1, 1].</summary>
    public static float[] Apply(ReadOnlySpan<float> samples, float gain)
    {
        var result = new float[samples.Length];
        for (var i = 0; i < result.Length; i++)
        {
            var v = samples[i];
            if (float.IsNaN(v)) v = 0f;
            result[i] = Math.Clamp(v * gain, -1f, 1f);
        }

        return result;
    }
}
