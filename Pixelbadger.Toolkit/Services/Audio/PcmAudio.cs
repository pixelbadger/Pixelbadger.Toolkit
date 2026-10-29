namespace Pixelbadger.Toolkit.Services.Audio;

/// <summary>Interleaved 16-bit PCM audio.</summary>
public sealed record PcmAudio(int SampleRate, int Channels, short[] Samples);
