using Pixelbadger.Toolkit.Opus;

namespace Pixelbadger.Toolkit.Services.Audio;

public sealed class OpusPcmEncoder
{
    private const int SampleRate = 48000;
    private const int FrameSize = 960;
    private const int MinFrameSize = 120;
    private const int MaxPacketBytes = 4000;

    private readonly IOpusCodecFactory _factory;

    public OpusPcmEncoder(IOpusCodecFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    /// <summary>Returns <paramref name="pcm"/> preceded by <paramref name="padSamples"/> zero samples per channel.</summary>
    public static ReadOnlySpan<short> PrependSilence(ReadOnlySpan<short> pcm, int padSamples, int channels)
    {
        if (padSamples <= 0) return pcm;
        var result = new short[pcm.Length + (long)padSamples * channels];
        pcm.CopyTo(result.AsSpan(padSamples * channels));
        return result;
    }

    /// <summary>
    /// Encodes interleaved 48 kHz PCM with a fresh encoder. Without <paramref name="exactSamples"/> the input is followed by
    /// Lookahead zero samples so the last real sample is encoded (960-sample frames, EndTrim set). With exactSamples = R,
    /// exactly R samples per channel are encoded (zero-padded when the input is shorter, extra input ignored) using 960-sample
    /// frames first and then 480/240/120-sample frames for the remainder; no lookahead tail is added and EndTrim is 0.
    /// R must be a positive multiple of 120.
    /// </summary>
    public EncodedAudio Encode(ReadOnlySpan<short> pcm48k, OpusEncodeSettings settings, long? exactSamples = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Channels is not (1 or 2))
            throw new ArgumentException("Channels must be 1 or 2", nameof(settings));
        if (pcm48k.Length % settings.Channels != 0)
            throw new ArgumentException("PCM length must be a multiple of the channel count", nameof(pcm48k));
        if (exactSamples is { } r && (r <= 0 || r % MinFrameSize != 0))
            throw new ArgumentOutOfRangeException(nameof(exactSamples), "exactSamples must be a positive multiple of 120");
        var n = pcm48k.Length / settings.Channels;
        if (n == 0)
            throw new ArgumentException("PCM must contain at least one sample", nameof(pcm48k));

        using var encoder = _factory.CreateEncoder(SampleRate, settings.Channels, OpusApplication.Audio);
        encoder.Bitrate = settings.Bitrate;
        encoder.Complexity = settings.Complexity;
        var lookahead = encoder.Lookahead;

        var frames = new List<int>();
        int endTrim;
        long encodedSamples;
        if (exactSamples is { } exact)
        {
            encodedSamples = exact;
            endTrim = 0;
            for (var left = exact; left > 0;)
            {
                var size = left >= FrameSize ? FrameSize : left >= 480 ? 480 : left >= 240 ? 240 : MinFrameSize;
                frames.Add(size);
                left -= size;
            }
        }
        else
        {
            var packetCount = ((long)n + lookahead + FrameSize - 1) / FrameSize;
            encodedSamples = packetCount * FrameSize;
            endTrim = (int)(encodedSamples - lookahead - n);
            for (var p = 0; p < packetCount; p++) frames.Add(FrameSize);
        }

        var ch = settings.Channels;
        var padded = new short[encodedSamples * ch];
        pcm48k[..Math.Min(pcm48k.Length, padded.Length)].CopyTo(padded);

        var packets = new List<byte[]>(frames.Count);
        var buffer = new byte[MaxPacketBytes];
        var offset = 0L;
        foreach (var size in frames)
        {
            var written = encoder.Encode(padded.AsSpan((int)(offset * ch), size * ch), size, buffer);
            packets.Add(buffer.AsSpan(0, written).ToArray());
            offset += size;
        }

        return new EncodedAudio(packets, lookahead, n, endTrim, ch);
    }
}
