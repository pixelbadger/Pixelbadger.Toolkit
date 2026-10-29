using Pixelbadger.Toolkit.Opus;

namespace Pixelbadger.Toolkit.Services.Audio;

public sealed class OpusPcmEncoder
{
    private const int SampleRate = 48000;
    private const int FrameSize = 960;
    private const int MaxPacketBytes = 4000;

    private readonly IOpusCodecFactory _factory;

    public OpusPcmEncoder(IOpusCodecFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    /// <summary>
    /// Encodes interleaved 48 kHz PCM with a fresh encoder. Without <paramref name="maxPackets"/> the input is followed by
    /// Lookahead zero samples so the last real sample is encoded. With maxPackets = K exactly K packets are produced
    /// from the first K*960 samples per channel (zero-padded when shorter) and EndTrim is 0.
    /// </summary>
    public EncodedAudio Encode(ReadOnlySpan<short> pcm48k, OpusEncodeSettings settings, int? maxPackets = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Channels is not (1 or 2))
            throw new ArgumentException("Channels must be 1 or 2", nameof(settings));
        if (pcm48k.Length % settings.Channels != 0)
            throw new ArgumentException("PCM length must be a multiple of the channel count", nameof(pcm48k));
        if (maxPackets is <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxPackets), "maxPackets must be positive");
        var n = pcm48k.Length / settings.Channels;
        if (n == 0)
            throw new ArgumentException("PCM must contain at least one sample", nameof(pcm48k));

        using var encoder = _factory.CreateEncoder(SampleRate, settings.Channels, OpusApplication.Audio);
        encoder.Bitrate = settings.Bitrate;
        encoder.Complexity = settings.Complexity;
        var lookahead = encoder.Lookahead;

        int packetCount;
        int endTrim;
        if (maxPackets is { } k)
        {
            packetCount = k;
            endTrim = 0;
        }
        else
        {
            packetCount = (int)(((long)n + lookahead + FrameSize - 1) / FrameSize);
            endTrim = packetCount * FrameSize - lookahead - n;
        }

        var ch = settings.Channels;
        var padded = new short[(long)packetCount * FrameSize * ch];
        pcm48k[..Math.Min(pcm48k.Length, padded.Length)].CopyTo(padded);

        var packets = new List<byte[]>(packetCount);
        var buffer = new byte[MaxPacketBytes];
        for (var p = 0; p < packetCount; p++)
        {
            var written = encoder.Encode(padded.AsSpan(p * FrameSize * ch, FrameSize * ch), FrameSize, buffer);
            packets.Add(buffer.AsSpan(0, written).ToArray());
        }

        return new EncodedAudio(packets, lookahead, n, endTrim, ch);
    }
}
