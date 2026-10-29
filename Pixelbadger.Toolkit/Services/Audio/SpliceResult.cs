using Pixelbadger.Toolkit.Opus.Ogg;

namespace Pixelbadger.Toolkit.Services.Audio;

/// <summary>Outcome of a splice edit. Samples are 48 kHz per channel in output time.</summary>
public sealed record SpliceResult(OggOpusFile File, long RequestedSample, long SnappedSample, long ReplacedSamples);
