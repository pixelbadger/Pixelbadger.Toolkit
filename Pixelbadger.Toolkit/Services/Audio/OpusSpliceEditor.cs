using Pixelbadger.Toolkit.Opus.Ogg;

namespace Pixelbadger.Toolkit.Services.Audio;

/// <summary>Edits Ogg Opus files by packet splicing: original packets are never re-encoded.</summary>
public static class OpusSpliceEditor
{
    public static OggOpusFile CreateRecording(EncodedAudio audio, int channels)
    {
        ArgumentNullException.ThrowIfNull(audio);
        if (channels is not (1 or 2))
            throw new ArgumentException("Channels must be 1 or 2", nameof(channels));
        var head = new OpusHead(1, (byte)channels, (ushort)audio.Lookahead, 48000, 0, 0);
        return OggOpusFile.Create(head, audio.Packets, audio.EndTrim);
    }

    /// <summary>
    /// Inserts new audio at the nearest packet boundary. <paramref name="encode"/> is first called with no front pad (which also
    /// reveals the encoder's lookahead L); when the boundary's packet-stream start s is below PreSkip - L it is called again with
    /// FrontPadSamples = PreSkip - s - L so the decoder's pre-skip never discards new audio.
    /// </summary>
    public static SpliceResult Insert(OggOpusFile file, long atSample, Func<EncodeRequest, EncodedAudio> encode)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(encode);
        var layout = Layout.Of(file);
        var index = layout.Snap(atSample, file.DurationSamples);
        var audio = EncodeWithPad(file, layout, index, null, encode);

        var packets = new List<byte[]>(file.AudioPackets.Count + audio.Packets.Count);
        packets.AddRange(file.AudioPackets.Take(index));
        packets.AddRange(audio.Packets);
        packets.AddRange(file.AudioPackets.Skip(index));
        var endTrim = index == file.AudioPackets.Count ? audio.EndTrim : file.EndTrim;
        return new SpliceResult(file with { AudioPackets = packets, EndTrim = endTrim }, atSample, layout.SnappedTime(index, file.DurationSamples), 0);
    }

    /// <summary>
    /// Overwrites audio from the nearest packet boundary. <paramref name="newSampleCount"/> is N, the unpadded take length.
    /// Inside the file (snapped + N below the duration) the longest run of whole original packets of total length R &lt;= N (at
    /// least one packet) is replaced by exactly R samples of the take, preserving the length. Otherwise (the take reaches or passes
    /// the end, including appends) every original packet from the boundary on is replaced by the full encode.
    /// </summary>
    public static SpliceResult Overwrite(OggOpusFile file, long atSample, Func<EncodeRequest, EncodedAudio> encode, long newSampleCount)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(encode);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(newSampleCount);
        var duration = file.DurationSamples;
        var layout = Layout.Of(file);
        var index = layout.Snap(atSample, duration);
        var snapped = layout.SnappedTime(index, duration);
        var count = file.AudioPackets.Count;

        if (index < count && snapped + newSampleCount < duration)
        {
            // Inside the decoder's pre-skip (spec v8 #32) the replaced run is the SHORTEST run of whole original packets whose total
            // is at least (PreSkip - s) + N, so the whole take is audible; if no such run exists before the end, fall through to the
            // reaches-the-end case. Elsewhere (spec v7 #30) it is the longest run of at most N + pad samples (at least one packet).
            // The first encode (no front pad) also reveals the encoder's lookahead L; if a front pad is needed the encode is repeated.
            var lead = Math.Max(0, layout.PreSkip - layout.Starts[index]);
            (int End, long Run)? plan = lead > 0
                ? ShortestRun(layout, index, lead + newSampleCount)
                : LongestRun(layout, index, newSampleCount);
            if (plan is { } p)
            {
                var (end, run) = p;
                var exact = encode(new EncodeRequest(0, run));
                CheckChannels(file, exact);
                var pad = layout.FrontPad(index, exact.Lookahead);
                if (pad > 0)
                {
                    if (lead == 0)
                        (end, run) = LongestRun(layout, index, newSampleCount + pad);
                    exact = encode(new EncodeRequest(pad, run));
                    CheckChannels(file, exact);
                }

                var encodedSamples = exact.Packets.Sum(x => (long)OpusToc.GetSampleCount(x));
                if (encodedSamples != run)
                    throw new InvalidOperationException($"Exact encode returned {encodedSamples} samples, expected {run}");
                var packets = new List<byte[]>(count - (end - index) + exact.Packets.Count);
                packets.AddRange(file.AudioPackets.Take(index));
                packets.AddRange(exact.Packets);
                packets.AddRange(file.AudioPackets.Skip(end));
                return new SpliceResult(file with { AudioPackets = packets }, atSample, snapped, run);
            }
        }

        var full = EncodeWithPad(file, layout, index, null, encode);
        var tail = new List<byte[]>(index + full.Packets.Count);
        tail.AddRange(file.AudioPackets.Take(index));
        tail.AddRange(full.Packets);
        return new SpliceResult(file with { AudioPackets = tail, EndTrim = full.EndTrim }, atSample, snapped, layout.Starts[count] - layout.Starts[index]);
    }

    /// <summary>The longest run of whole original packets from <paramref name="index"/> totalling at most <paramref name="allowance"/> samples (at least one packet).</summary>
    private static (int End, long Run) LongestRun(Layout layout, int index, long allowance)
    {
        var count = layout.Starts.Length - 1;
        var end = index;
        long run = 0;
        while (end < count && run + (layout.Starts[end + 1] - layout.Starts[end]) <= allowance)
        {
            run += layout.Starts[end + 1] - layout.Starts[end];
            end++;
        }

        if (end == index)
        {
            end = index + 1;
            run = layout.Starts[end] - layout.Starts[index];
        }

        return (end, run);
    }

    /// <summary>The shortest run of whole original packets from <paramref name="index"/> totalling at least <paramref name="required"/> samples, or null when the file ends first.</summary>
    private static (int End, long Run)? ShortestRun(Layout layout, int index, long required)
    {
        var count = layout.Starts.Length - 1;
        for (var end = index + 1; end <= count; end++)
        {
            var run = layout.Starts[end] - layout.Starts[index];
            if (run >= required)
                return (end, run);
        }

        return null;
    }

    private static EncodedAudio EncodeWithPad(OggOpusFile file, Layout layout, int index, long? exactSamples, Func<EncodeRequest, EncodedAudio> encode)
    {
        var audio = encode(new EncodeRequest(0, exactSamples));
        CheckChannels(file, audio);
        var pad = layout.FrontPad(index, audio.Lookahead);
        if (pad > 0)
        {
            audio = encode(new EncodeRequest(pad, exactSamples));
            CheckChannels(file, audio);
        }

        return audio;
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
        public int PreSkip => _preSkip;

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

        /// <summary>Zero samples to prepend so the decoder's pre-skip discards only priming, never new audio (spec v6 #23).</summary>
        public int FrontPad(int boundary, int lookahead) => (int)Math.Max(0, _preSkip - Starts[boundary] - lookahead);

        /// <summary>The reported snapped time: the audible duration for the end boundary, else the boundary's output time.</summary>
        public long SnappedTime(int boundary, long duration)
            => boundary == Starts.Length - 1 ? duration : OutputTime(boundary);

        public int Snap(long atSample, long duration)
        {
            if (atSample < 0 || atSample > duration)
                throw new ArgumentOutOfRangeException(nameof(atSample), atSample, $"Sample must be between 0 and {duration}");
            if (atSample == duration)
                return Starts.Length - 1;
            var best = 0;
            var bestDistance = long.MaxValue;
            for (var i = 0; i < Starts.Length; i++)
            {
                var time = i == Starts.Length - 1 ? duration : OutputTime(i);
                var distance = Math.Abs(time - atSample);
                if (distance < bestDistance) { best = i; bestDistance = distance; }
            }

            return best;
        }
    }
}
