using System.Globalization;
using Pixelbadger.Toolkit.Opus.Ogg;

namespace Pixelbadger.Toolkit.Services.Audio;

/// <summary>Validation, parsing and formatting shared by the audio record, insert and overwrite components.</summary>
internal static class AudioCommandSupport
{
    internal const int SampleRate = 48000;
    internal const int MinBitrate = 6000;
    internal const int MaxBitrate = 510000;

    internal static void ValidateBitrate(int bitrate)
    {
        if (bitrate is < MinBitrate or > MaxBitrate)
            throw new InvalidOperationException($"--bitrate must be between {MinBitrate} and {MaxBitrate}");
    }

    internal static void ValidateChannels(int channels)
    {
        if (channels is not (1 or 2))
            throw new InvalidOperationException("--channels must be 1 or 2");
    }

    internal static long ToSamples(TimeSpan time) =>
        (long)Math.Round(time.TotalSeconds * SampleRate, MidpointRounding.AwayFromZero);

    internal static TimeSpan? ParseOptionalTime(string? text) => text is null ? null : AudioTimeParser.Parse(text);

    internal static string Seconds(long samples) =>
        ((double)samples / SampleRate).ToString("0.000", CultureInfo.InvariantCulture);

    internal static OggOpusFile ReadOpusFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"File '{path}' not found");
        using var stream = File.OpenRead(path);
        return OggOpusFile.Read(stream);
    }

    internal static void WriteOpusFile(string path, OggOpusFile file) =>
        AtomicFile.Replace(path, stream => file.Write(stream, (uint)Random.Shared.NextInt64(1, uint.MaxValue)));

    internal static void RequireAudio(short[] pcm)
    {
        if (pcm.Length == 0)
            throw new InvalidOperationException("No audio was captured");
    }
}
