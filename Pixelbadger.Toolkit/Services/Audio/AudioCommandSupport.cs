using System.Globalization;
using Pixelbadger.Toolkit.Opus;
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

    /// <summary>Length and last-write time (UTC) of an edited file when it was read, to detect a concurrent change before it is replaced.</summary>
    internal sealed record FileStamp(long Length, DateTime LastWriteUtc);

    /// <summary>True when cancelling aborts the take (a fixed length or a WAV); live capture without a duration treats cancelling as "stop and save".</summary>
    internal static bool AbortsOnCancel(AudioInputRequest request) => request.Duration is not null || request.FromWav is not null;

    internal static (OggOpusFile File, FileStamp Stamp) ReadOpusFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"File '{path}' not found");
        var stamp = StampOf(path);
        using var stream = File.OpenRead(path);
        return (OggOpusFile.Read(stream), stamp);
    }

    private static FileStamp StampOf(string path)
    {
        var info = new FileInfo(path);
        if (info.LinkTarget is not null && info.ResolveLinkTarget(returnFinalTarget: true) is FileInfo target)
            info = target;
        return new FileStamp(info.Length, info.LastWriteTimeUtc);
    }

    private static void VerifyUnchanged(string path, FileStamp expected)
    {
        if (!File.Exists(path) || StampOf(path) != expected)
            throw new InvalidOperationException($"File '{path}' changed while recording; no changes were written");
    }

    /// <summary>
    /// Writes <paramref name="file"/> atomically over <paramref name="path"/>. In abort mode a cancellation (checked again while
    /// the temp file is complete) stops the write before anything is replaced; a changed <paramref name="expected"/> stamp fails it.
    /// </summary>
    internal static void WriteOpusFile(string path, OggOpusFile file, AudioInputRequest request, CancellationToken cancellationToken, FileStamp? expected = null)
    {
        var abort = AbortsOnCancel(request);
        if (abort)
            cancellationToken.ThrowIfCancellationRequested();
        AtomicFile.Replace(path, stream =>
        {
            file.Write(stream, (uint)Random.Shared.NextInt64(1, uint.MaxValue));
            AudioSeams.AfterWrite?.Invoke(stream);
            if (abort)
                cancellationToken.ThrowIfCancellationRequested();
            if (expected is not null)
                VerifyUnchanged(path, expected);
        });
    }

    internal static void EnsureOpusAvailable() => (AudioSeams.EnsureOpus ?? OpusLibrary.EnsureAvailable)();

    internal static void RequireAudio(short[] pcm)
    {
        if (pcm.Length == 0)
            throw new InvalidOperationException("No audio was captured");
    }
}

/// <summary>Per-async-flow overrides so tests can replace the library check, the ffmpeg lookup and the file write without touching process-wide state.</summary>
internal static class AudioSeams
{
    private static readonly AsyncLocal<Action?> EnsureOpusValue = new();
    private static readonly AsyncLocal<Func<string?>?> FfmpegLocatorValue = new();
    private static readonly AsyncLocal<Action<Stream>?> AfterWriteValue = new();

    internal static Action? EnsureOpus => EnsureOpusValue.Value;
    internal static Func<string?>? FfmpegLocator => FfmpegLocatorValue.Value;
    internal static Action<Stream>? AfterWrite => AfterWriteValue.Value;

    internal static IDisposable Override(Action? ensureOpus = null, Func<string?>? ffmpegLocator = null, Action<Stream>? afterWrite = null)
    {
        var previous = (EnsureOpusValue.Value, FfmpegLocatorValue.Value, AfterWriteValue.Value);
        EnsureOpusValue.Value = ensureOpus;
        FfmpegLocatorValue.Value = ffmpegLocator;
        AfterWriteValue.Value = afterWrite;
        return new Restore(previous);
    }

    private sealed class Restore((Action?, Func<string?>?, Action<Stream>?) previous) : IDisposable
    {
        public void Dispose()
        {
            EnsureOpusValue.Value = previous.Item1;
            FfmpegLocatorValue.Value = previous.Item2;
            AfterWriteValue.Value = previous.Item3;
        }
    }
}
