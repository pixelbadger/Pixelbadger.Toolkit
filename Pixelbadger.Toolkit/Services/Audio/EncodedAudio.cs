namespace Pixelbadger.Toolkit.Services.Audio;

/// <summary>
/// Opus packets produced from PCM. <paramref name="SampleCount"/> is N, the input samples per channel;
/// a full encode holds ceil((N + Lookahead)/960) 20 ms packets and EndTrim = Packets*960 - Lookahead - N.
/// <paramref name="Channels"/> is the channel count the packets were encoded with (0 = unknown, not validated).
/// </summary>
public sealed record EncodedAudio(IReadOnlyList<byte[]> Packets, int Lookahead, long SampleCount, int EndTrim, int Channels = 0);
