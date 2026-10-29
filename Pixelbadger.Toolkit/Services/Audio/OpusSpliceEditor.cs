using Pixelbadger.Toolkit.Opus.Ogg;

namespace Pixelbadger.Toolkit.Services.Audio;

/// <summary>Edits Ogg Opus files by packet splicing: original packets are never re-encoded.</summary>
public static class OpusSpliceEditor
{
    private const int FrameSamples = 960;

    public static OggOpusFile CreateRecording(EncodedAudio audio, int channels)
    {
        ArgumentNullException.ThrowIfNull(audio);
        if (channels is not (1 or 2))
            throw new ArgumentException("Channels must be 1 or 2", nameof(channels));
        var head = new OpusHead(1, (byte)channels, (ushort)audio.Lookahead, 48000, 0, 0);
        return OggOpusFile.Create(head, audio.Packets, audio.EndTrim);
    }

    public static SpliceResult Insert(OggOpusFile file, long atSample, EncodedAudio audio)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(audio);
        CheckChannels(file, audio);
        var layout = Layout.Of(file);
        var index = layout.Snap(atSample, file.DurationSamples);

        var packets = new List<byte[]>(file.AudioPackets.Count + audio.Packets.Count);
        packets.AddRange(file.AudioPackets.Take(index));
        packets.AddRange(audio.Packets);
        packets.AddRange(file.AudioPackets.Skip(index));
        var endTrim = index == file.AudioPackets.Count ? audio.EndTrim : file.EndTrim;
        return new SpliceResult(file with { AudioPackets = packets, EndTrim = endTrim }, atSample, layout.OutputTime(index), 0);
    }

    /// <summary>
    /// <paramref name="encode"/>(null) returns the full encode, <paramref name="encode"/>(K) a truncated encode of exactly K packets.
    /// </summary>
    public static SpliceResult Overwrite(OggOpusFile file, long atSample, Func<int?, EncodedAudio> encode)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(encode);
        var duration = file.DurationSamples;
        var layout = Layout.Of(file);
        var index = layout.Snap(atSample, duration);
        var snapped = layout.OutputTime(index);
        var count = file.AudioPackets.Count;

        var full = encode(null);
        CheckChannels(file, full);

        if (snapped + full.SampleCount < duration)
        {
            var k = (int)Math.Max(1, full.SampleCount / FrameSamples);
            var end = -1;
            for (var j = index + 1; j < count; j++)
            {
                if (layout.Starts[j] - layout.Starts[index] >= (long)k * FrameSamples) { end = j; break; }
            }

            if (end >= 0)
            {
                var truncated = encode(k);
                CheckChannels(file, truncated);
                if (truncated.Packets.Count != k)
                    throw new InvalidOperationException($"Truncated encode returned {truncated.Packets.Count} packets, expected {k}");
                var packets = new List<byte[]>(count - (end - index) + k);
                packets.AddRange(file.AudioPackets.Take(index));
                packets.AddRange(truncated.Packets);
                packets.AddRange(file.AudioPackets.Skip(end));
                return new SpliceResult(file with { AudioPackets = packets }, atSample, snapped, layout.Starts[end] - layout.Starts[index]);
            }
        }

        var tail = new List<byte[]>(index + full.Packets.Count);
        tail.AddRange(file.AudioPackets.Take(index));
        tail.AddRange(full.Packets);
        return new SpliceResult(file with { AudioPackets = tail, EndTrim = full.EndTrim }, atSample, snapped, layout.Starts[count] - layout.Starts[index]);
    }

    private static void CheckChannels(OggOpusFile file, EncodedAudio audio)
    {
        if (audio.Channels != 0 && audio.Channels != file.Head.ChannelCount)
            throw new ArgumentException(
                $"Audio has {audio.Channels} channel(s) but the file has {file.Head.ChannelCount}", nameof(audio));
    }

    private sealed class Layout
    {
        public long[] Starts = [];
        private int _preSkip;

        public static Layout Of(OggOpusFile file)
        {
            var starts = new long[file.AudioPackets.Count + 1];
            long total = 0;
            for (var i = 0; i < file.AudioPackets.Count; i++)
            {
                starts[i] = total;
                total += OpusToc.GetSampleCount(file.AudioPackets[i]);
            }

            starts[^1] = total;
            return new Layout { Starts = starts, _preSkip = file.Head.PreSkip };
        }

        public long OutputTime(int boundary) => Math.Max(0, Starts[boundary] - _preSkip);

        public int Snap(long atSample, long duration)
        {
            if (atSample < 0 || atSample > duration)
                throw new ArgumentOutOfRangeException(nameof(atSample), atSample, $"Sample must be between 0 and {duration}");
            var best = 0;
            var bestDistance = long.MaxValue;
            for (var i = 0; i < Starts.Length; i++)
            {
                var distance = Math.Abs(OutputTime(i) - atSample);
                if (distance < bestDistance) { best = i; bestDistance = distance; }
            }

            return best;
        }
    }
}
