namespace Pixelbadger.Toolkit.Services.Audio;

/// <summary>
/// What <see cref="OpusSpliceEditor"/> asks its encode callback for. The caller prepends <paramref name="FrontPadSamples"/>
/// zero samples per channel to its PCM and encodes it: <paramref name="ExactSamples"/> null is a full encode, otherwise
/// exactly that many samples per channel (a multiple of 120) are encoded with no lookahead tail.
/// </summary>
public sealed record EncodeRequest(int FrontPadSamples, long? ExactSamples);
