namespace Pixelbadger.Toolkit.Services.Audio;

public static class PcmConverter
{
    public const int TargetSampleRate = 48000;

    public static readonly IReadOnlyList<int> SupportedRates = new[] { 8000, 12000, 16000, 24000, 48000 };

    public static short[] ToOpusInput(PcmAudio audio, int targetChannels)
    {
        ArgumentNullException.ThrowIfNull(audio);
        if (audio.Channels is not (1 or 2))
            throw new ArgumentException("Source audio must be mono or stereo.", nameof(audio));
        if (targetChannels is not (1 or 2))
            throw new ArgumentOutOfRangeException(nameof(targetChannels), "Target channels must be 1 or 2.");
        if (!SupportedRates.Contains(audio.SampleRate))
        {
            throw new InvalidDataException(
                $"Unsupported WAV sample rate {audio.SampleRate} Hz. Supported rates: 8000, 12000, 16000, 24000, 48000 Hz. " +
                "Resample first, e.g. ffmpeg -i in.wav -ar 48000 out.wav");
        }

        var frames = audio.Samples.Length / audio.Channels;
        var channelled = ConvertChannels(audio.Samples, frames, audio.Channels, targetChannels);
        var factor = TargetSampleRate / audio.SampleRate;
        if (factor == 1)
            return channelled;

        var outFrames = checked(frames * factor);
        var result = new short[outFrames * targetChannels];
        for (var f = 0; f < outFrames; f++)
        {
            var i = f / factor;
            var rem = f % factor;
            var next = Math.Min(i + 1, frames - 1);
            for (var c = 0; c < targetChannels; c++)
            {
                int a = channelled[i * targetChannels + c];
                int b = channelled[next * targetChannels + c];
                result[f * targetChannels + c] = (short)(a + (b - a) * rem / factor);
            }
        }

        return result;
    }

    private static short[] ConvertChannels(short[] samples, int frames, int from, int to)
    {
        if (from == to)
            return frames * from == samples.Length ? samples : samples.AsSpan(0, frames * from).ToArray();

        var result = new short[frames * to];
        if (from == 1)
        {
            for (var f = 0; f < frames; f++)
            {
                result[f * 2] = samples[f];
                result[f * 2 + 1] = samples[f];
            }
        }
        else
        {
            for (var f = 0; f < frames; f++)
                result[f] = (short)((samples[f * 2] + samples[f * 2 + 1]) / 2);
        }

        return result;
    }
}
