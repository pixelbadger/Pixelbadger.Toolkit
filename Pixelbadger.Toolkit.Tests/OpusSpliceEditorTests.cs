using System.Diagnostics;
using FluentAssertions;
using Pixelbadger.Toolkit.Opus;
using Pixelbadger.Toolkit.Opus.Ogg;
using Pixelbadger.Toolkit.Services.Audio;

namespace Pixelbadger.Toolkit.Tests;

public class OpusSpliceEditorTests : IDisposable
{
    private const int Rate = 48000;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    private readonly OpusPcmEncoder _encoder = new(new OpusCodecFactory());

    public OpusSpliceEditorTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private OggOpusFile Record(double seconds, double frequency = 440, int channels = 1)
        => OpusSpliceEditor.CreateRecording(
            _encoder.Encode(OpusTestHelpers.Sine(channels, (int)(seconds * Rate), frequency), new OpusEncodeSettings(channels)), channels);

    private Func<EncodeRequest, EncodedAudio> Src(double seconds, double frequency, int channels = 1)
    {
        var pcm = OpusTestHelpers.Sine(channels, (int)(seconds * Rate), frequency);
        return r => _encoder.Encode(OpusPcmEncoder.PrependSilence(pcm, r.FrontPadSamples, channels).ToArray(), new OpusEncodeSettings(channels), r.ExactSamples);
    }

    private static Func<EncodeRequest, EncodedAudio> Const(EncodedAudio audio) => _ => audio;

    private SpliceResult Over(OggOpusFile file, long at, double seconds, double frequency, int channels = 1)
        => OpusSpliceEditor.Overwrite(file, at, Src(seconds, frequency, channels), (long)(seconds * Rate));

    private EncodedAudio Audio(double seconds, double frequency, int channels = 1)
        => _encoder.Encode(OpusTestHelpers.Sine(channels, (int)(seconds * Rate), frequency), new OpusEncodeSettings(channels));

    private static OggOpusFile RoundTrip(OggOpusFile file)
    {
        using var ms = new MemoryStream();
        file.Write(ms, 1234);
        ms.Position = 0;
        return OggOpusFile.Read(ms);
    }

    private (short[] Samples, string Stderr) Decode(OggOpusFile file, int channels = 1)
    {
        var path = Path.Combine(_dir, Guid.NewGuid() + ".opus");
        using (var fs = File.Create(path)) file.Write(fs, 77);
        var psi = new ProcessStartInfo("ffmpeg") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { "-v", "error", "-i", path, "-f", "s16le", "-ac", channels.ToString(), "-ar", "48000", "-" })
            psi.ArgumentList.Add(a);
        using var proc = Process.Start(psi)!;
        var errTask = proc.StandardError.ReadToEndAsync();
        using var ms = new MemoryStream();
        proc.StandardOutput.BaseStream.CopyTo(ms);
        proc.WaitForExit();
        var bytes = ms.ToArray();
        var samples = new short[bytes.Length / 2];
        Buffer.BlockCopy(bytes, 0, samples, 0, samples.Length * 2);
        return (samples, errTask.Result);
    }

    private static double Goertzel(short[] samples, double from, double to, double frequency)
    {
        var start = (int)(from * Rate);
        var end = Math.Min(samples.Length, (int)(to * Rate));
        var w = 2 * Math.PI * frequency / Rate;
        double s1 = 0, s2 = 0;
        for (var i = start; i < end; i++)
        {
            var s0 = samples[i] + 2 * Math.Cos(w) * s1 - s2;
            s2 = s1; s1 = s0;
        }

        return Math.Sqrt(s1 * s1 + s2 * s2 - 2 * Math.Cos(w) * s1 * s2) / (end - start);
    }

    private static void AssertDominant(short[] samples, double from, double to, double dominant, double other)
        => Goertzel(samples, from, to, dominant).Should().BeGreaterThan(Goertzel(samples, from, to, other) * 10, $"{dominant} Hz should dominate {from}-{to}s");

    [Fact]
    public void CreateRecording_ShouldBuildHeadAndExactDuration_WhenEncoded()
    {
        var audio = Audio(1.0, 440);
        var file = OpusSpliceEditor.CreateRecording(audio, 1);
        file.Head.Version.Should().Be(1);
        file.Head.ChannelCount.Should().Be(1);
        file.Head.PreSkip.Should().Be(312);
        file.Head.InputSampleRate.Should().Be(48000u);
        file.Head.OutputGain.Should().Be(0);
        file.Head.MappingFamily.Should().Be(0);
        file.DurationSamples.Should().Be(48000);
        file.EndTrim.Should().Be(audio.EndTrim);
        RoundTrip(file).DurationSamples.Should().Be(48000);
    }

    [Fact]
    public void CreateRecording_ShouldProduceDecodableStereoFile_WhenTwoChannels()
    {
        var file = Record(0.5, 440, 2);
        file.Head.ChannelCount.Should().Be(2);
        file.DurationSamples.Should().Be(24000);
        var (samples, err) = Decode(file, 2);
        err.Should().BeEmpty();
        samples.Length.Should().Be(24000 * 2);
    }

    [Fact]
    public void Insert_ShouldAddExactDurationAndSwitchTone_WhenInsertingIntoMiddle()
    {
        var file = Record(2.0);
        var originals = file.AudioPackets.ToList();
        var audio = Audio(1.0, 880);

        var result = OpusSpliceEditor.Insert(file, 48000, Const(audio));

        result.File.DurationSamples.Should().Be(96000 + 48960);
        result.ReplacedSamples.Should().Be(0);
        result.RequestedSample.Should().Be(48000);
        result.SnappedSample.Should().Be(47688);
        result.File.EndTrim.Should().Be(file.EndTrim);
        result.File.HeadPacket.Should().BeSameAs(file.HeadPacket);
        result.File.TagsPacket.Should().BeSameAs(file.TagsPacket);
        var packets = result.File.AudioPackets;
        for (var i = 0; i < 50; i++) packets[i].Should().BeSameAs(originals[i]);
        for (var i = 0; i < audio.Packets.Count; i++) packets[50 + i].Should().BeSameAs(audio.Packets[i]);
        for (var i = 50; i < originals.Count; i++) packets[i + audio.Packets.Count].Should().BeSameAs(originals[i]);

        var (samples, err) = Decode(RoundTrip(result.File));
        err.Should().BeEmpty();
        samples.Length.Should().Be(96000 + 48960);
        AssertDominant(samples, 0.1, 0.9, 440, 880);
        AssertDominant(samples, 1.1, 1.9, 880, 440);
        AssertDominant(samples, 2.2, 2.9, 440, 880);
    }

    [Fact]
    public void Insert_ShouldInsertAtStart_WhenAtZero()
    {
        var file = Record(1.0);
        var result = OpusSpliceEditor.Insert(file, 0, Src(0.5, 880));
        result.SnappedSample.Should().Be(0);
        result.File.AudioPackets.Take(file.AudioPackets.Count).Should().NotBeEquivalentTo(file.AudioPackets);
        result.File.AudioPackets.Skip(result.File.AudioPackets.Count - file.AudioPackets.Count).Should().Equal(file.AudioPackets);
        result.File.EndTrim.Should().Be(file.EndTrim);
        result.File.DurationSamples.Should().Be(48000 + 26 * 960);
        Decode(result.File).Stderr.Should().BeEmpty();
    }

    [Fact]
    public void Insert_ShouldAppendWithNewEndTrim_WhenNearestBoundaryIsEnd()
    {
        var file = Record(1.0) with { EndTrim = 0 };
        var audio = Audio(0.5, 880);
        var result = OpusSpliceEditor.Insert(file, file.DurationSamples, Const(audio));
        result.File.EndTrim.Should().Be(audio.EndTrim);
        result.File.AudioPackets.Take(file.AudioPackets.Count).Should().Equal(file.AudioPackets);
        result.SnappedSample.Should().Be(file.DurationSamples);
        result.File.DurationSamples.Should().Be(file.TotalPacketSamples - 312 + audio.Packets.Count * 960L - audio.EndTrim);
        Decode(result.File).Stderr.Should().BeEmpty();
    }

    [Fact]
    public void Overwrite_ShouldReplaceExactly25Packets_WhenInside()
    {
        var file = Record(2.0);
        var originals = file.AudioPackets.ToList();

        var result = Over(file, 48000, 0.5, 880);

        result.File.DurationSamples.Should().Be(96000);
        result.File.AudioPackets.Should().HaveCount(originals.Count);
        result.ReplacedSamples.Should().Be(25 * 960);
        result.SnappedSample.Should().Be(47688);
        result.File.EndTrim.Should().Be(file.EndTrim);
        var packets = result.File.AudioPackets;
        for (var i = 0; i < packets.Count; i++)
        {
            if (i is >= 50 and < 75) packets[i].Should().NotBeSameAs(originals[i]);
            else packets[i].Should().BeSameAs(originals[i]);
        }

        var (samples, err) = Decode(RoundTrip(result.File));
        err.Should().BeEmpty();
        samples.Length.Should().Be(96000);
        AssertDominant(samples, 0.1, 0.9, 440, 880);
        AssertDominant(samples, 1.1, 1.4, 880, 440);
        AssertDominant(samples, 1.6, 1.9, 440, 880);
    }

    [Fact]
    public void Overwrite_ShouldReplaceToEndWithFullEncode_WhenReachingTheEnd()
    {
        var file = Record(2.0);
        var originals = file.AudioPackets.ToList();
        var full = Audio(1.0, 880);

        var result = Over(file, 72000, 1.0, 880);

        result.SnappedSample.Should().Be(71688);
        result.File.DurationSamples.Should().Be(result.SnappedSample + 312 + 48000);
        result.File.EndTrim.Should().Be(full.EndTrim);
        result.ReplacedSamples.Should().Be(originals.Count * 960L - 75 * 960);
        result.File.AudioPackets.Take(75).Should().Equal(originals.Take(75));
        result.File.AudioPackets.Skip(75).Should().BeEquivalentTo(full.Packets, o => o.WithStrictOrdering());
        var (samples, err) = Decode(RoundTrip(result.File));
        err.Should().BeEmpty();
        samples.Length.Should().Be((int)result.File.DurationSamples);
        AssertDominant(samples, 0.1, 0.9, 440, 880);
        AssertDominant(samples, 1.7, 2.3, 880, 440);
    }

    [Fact]
    public void Overwrite_ShouldAppend_WhenNearestBoundaryIsEnd()
    {
        var file = Record(1.0) with { EndTrim = 0 };
        var result = Over(file, file.DurationSamples, 0.25, 880);
        result.ReplacedSamples.Should().Be(0);
        result.File.AudioPackets.Take(file.AudioPackets.Count).Should().Equal(file.AudioPackets);
        Decode(result.File).Stderr.Should().BeEmpty();
    }

    [Fact]
    public void Insert_ShouldAppend_WhenAtDurationOfRealRecording()
    {
        var file = Record(2.0);
        file.EndTrim.Should().BeGreaterThan(0);
        var audio = Audio(1.0, 880);

        var result = OpusSpliceEditor.Insert(file, file.DurationSamples, Const(audio));

        result.SnappedSample.Should().Be(96000);
        result.ReplacedSamples.Should().Be(0);
        result.File.EndTrim.Should().Be(audio.EndTrim);
        result.File.AudioPackets.Take(file.AudioPackets.Count).Should().Equal(file.AudioPackets);
        result.File.AudioPackets.Skip(file.AudioPackets.Count).Should().Equal(audio.Packets);
        result.File.DurationSamples.Should().Be(file.TotalPacketSamples - 312 + 312 + 48000);
        result.File.DurationSamples.Should().Be(144960);
        var (samples, err) = Decode(RoundTrip(result.File));
        err.Should().BeEmpty();
        AssertDominant(samples, 2.1, 2.9, 880, 440);
        AssertDominant(samples, 0.1, 0.9, 440, 880);
    }

    [Fact]
    public void Overwrite_ShouldAppendFullEncode_WhenAtDurationOfRealRecording()
    {
        var file = Record(2.0);
        var full = Audio(1.0, 880);
        var result = Over(file, file.DurationSamples, 1.0, 880);
        result.SnappedSample.Should().Be(96000);
        result.ReplacedSamples.Should().Be(0);
        result.File.EndTrim.Should().Be(full.EndTrim);
        result.File.AudioPackets.Take(file.AudioPackets.Count).Should().Equal(file.AudioPackets);
        result.File.AudioPackets.Skip(file.AudioPackets.Count).Should().BeEquivalentTo(full.Packets, o => o.WithStrictOrdering());
        result.File.DurationSamples.Should().Be(144960);
        var (samples, err) = Decode(RoundTrip(result.File));
        err.Should().BeEmpty();
        AssertDominant(samples, 2.1, 2.9, 880, 440);
    }

    [Fact]
    public void Insert_ShouldAppendAtDuration_WhenOneSampleBeforeDuration()
    {
        var file = Record(2.0);
        var audio = Audio(0.1, 880);
        var result = OpusSpliceEditor.Insert(file, file.DurationSamples - 1, Const(audio));
        // the end boundary (audible duration 96000) is 1 sample away; the last interior boundary (95688) is 311 away
        result.SnappedSample.Should().Be(file.DurationSamples);
        result.File.AudioPackets.Take(file.AudioPackets.Count).Should().Equal(file.AudioPackets);
        result.File.EndTrim.Should().Be(audio.EndTrim);
    }

    [Fact]
    public void Insert_ShouldUseLastInteriorBoundary_WhenNearerThanEnd()
    {
        var file = Record(2.0);
        var audio = Audio(0.1, 880);
        var result = OpusSpliceEditor.Insert(file, file.DurationSamples - 600, Const(audio));
        // 95400 is 288 from 95688 (packet 100) and 600 from the end
        result.SnappedSample.Should().Be(95688);
        result.File.AudioPackets.Take(100).Should().Equal(file.AudioPackets.Take(100));
        result.File.AudioPackets.Skip(100 + audio.Packets.Count).Should().Equal(file.AudioPackets.Skip(100));
        result.File.EndTrim.Should().Be(file.EndTrim);
    }

    [Fact]
    public void Insert_ShouldStartNewAudioAtBoundaryPlusLookahead_WhenInsertingAtOneSecond()
    {
        var file = Record(2.0);
        var result = OpusSpliceEditor.Insert(file, 48000, Src(1.0, 880));
        result.SnappedSample.Should().Be(47688);
        var (samples, err) = Decode(RoundTrip(result.File));
        err.Should().BeEmpty();
        Goertzel(samples, 1.01, 1.05, 880).Should().BeGreaterThan(Goertzel(samples, 1.01, 1.05, 440));
    }

    [Fact]
    public void Overwrite_ShouldReplaceExactRunInside_WhenTakeEndsBeforeFileEnd()
    {
        var file = Record(1.0);
        var originals = file.AudioPackets.ToList();
        var requests = new List<EncodeRequest>();
        var src = Src(0.0205, 880);

        // boundary 24 is at 22728; N = 984 samples, so S + N < D and the run is exactly one 960-sample packet (inside branch)
        var result = OpusSpliceEditor.Overwrite(file, 22728, r => { requests.Add(r); return src(r); }, 984);

        result.SnappedSample.Should().Be(22728);
        result.ReplacedSamples.Should().Be(960);
        requests.Should().Equal(new EncodeRequest(0, 960));
        result.File.AudioPackets.Should().HaveCount(originals.Count);
        result.File.EndTrim.Should().Be(file.EndTrim);
        result.File.DurationSamples.Should().Be(file.DurationSamples);
        for (var i = 0; i < originals.Count; i++)
        {
            if (i == 24) result.File.AudioPackets[i].Should().NotBeSameAs(originals[i]);
            else result.File.AudioPackets[i].Should().BeSameAs(originals[i]);
        }

        Decode(result.File).Stderr.Should().BeEmpty();
    }

    [Fact]
    public void Overwrite_ShouldReplaceAllRemainingPackets_WhenTakeReachesTheEnd()
    {
        var file = Record(1.0);
        var originals = file.AudioPackets.ToList();
        var requests = new List<EncodeRequest>();
        var src = Src(0.5, 880);

        // boundary 40 is at 38088; S + N = 62088 > D = 48000, so the full-encode branch replaces packets 40..end
        var result = OpusSpliceEditor.Overwrite(file, 38088, r => { requests.Add(r); return src(r); }, 24000);

        result.SnappedSample.Should().Be(38088);
        requests.Should().Equal(new EncodeRequest(0, null));
        result.ReplacedSamples.Should().Be((originals.Count - 40) * 960L);
        result.File.AudioPackets.Take(40).Should().Equal(originals.Take(40));
        result.File.AudioPackets.Skip(40).Should().BeEquivalentTo(Audio(0.5, 880).Packets, o => o.WithStrictOrdering());
        result.File.DurationSamples.Should().Be(38088 + 312 + 24000);
    }

    [Fact]
    public void Overwrite_ShouldAppendFullEncode_WhenAtFileDuration()
    {
        var file = Record(1.0);
        var requests = new List<EncodeRequest>();
        var src = Src(0.25, 880);

        var result = OpusSpliceEditor.Overwrite(file, file.DurationSamples, r => { requests.Add(r); return src(r); }, 12000);

        requests.Should().Equal(new EncodeRequest(0, null));
        result.SnappedSample.Should().Be(file.DurationSamples);
        result.ReplacedSamples.Should().Be(0);
        result.File.AudioPackets.Take(file.AudioPackets.Count).Should().Equal(file.AudioPackets);
        result.File.AudioPackets.Should().HaveCount(file.AudioPackets.Count + 13);
        Decode(result.File).Stderr.Should().BeEmpty();
    }

    [Fact]
    public void Overwrite_ShouldReplaceOnePacketMinimum_WhenShorterThan20Ms()
    {
        var file = Record(1.0);
        var result = Over(file, 24000, 0.005, 880);
        result.ReplacedSamples.Should().Be(960);
        result.SnappedSample.Should().Be(23688);
        var replaced = result.File.AudioPackets.Select((p, i) => (p, i)).Where(t => !ReferenceEquals(t.p, file.AudioPackets[t.i])).Select(t => t.i);
        replaced.Should().Equal(25);
        result.File.AudioPackets.Should().HaveCount(file.AudioPackets.Count);
        result.File.DurationSamples.Should().Be(file.DurationSamples);
    }

    [Fact]
    public void Snap_ShouldSnapToNearestBoundary_WhenPreSkipIsZero()
    {
        var file = Record(2.0);
        file = file with { HeadPacket = new OpusHead(1, 1, 0, 48000, 0, 0).ToBytes() };
        var result = OpusSpliceEditor.Insert(file, 48624L, Src(0.1, 880));
        result.SnappedSample.Should().Be(48960);
        result.RequestedSample.Should().Be(48624);
    }

    [Fact]
    public void Snap_ShouldSnapOnPreSkipShiftedBoundaries_WhenRecordingHasPreSkip()
    {
        var file = Record(2.0);
        var result = OpusSpliceEditor.Insert(file, 48624, Src(0.1, 880));
        result.SnappedSample.Should().Be(48648);
    }

    [Fact]
    public void Snap_ShouldPreferEarlierBoundary_WhenTied()
    {
        var file = Record(2.0);
        // boundaries at 47688 and 48648; midpoint 48168
        OpusSpliceEditor.Insert(file, 48168, Src(0.1, 880)).SnappedSample.Should().Be(47688);
        OpusSpliceEditor.Insert(file, 48169, Src(0.1, 880)).SnappedSample.Should().Be(48648);
    }

    [Fact]
    public void Snap_ShouldMapToZero_WhenAtZeroOrWithinFirstBoundaryPair()
    {
        var file = Record(1.0);
        OpusSpliceEditor.Insert(file, 0, Src(0.1, 880)).SnappedSample.Should().Be(0);
        // boundaries 0 (packet 0, clamped) and 648 (packet 1) tie at 324 -> earlier
        OpusSpliceEditor.Insert(file, 324, Src(0.1, 880)).SnappedSample.Should().Be(0);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(48001)]
    public void Edits_ShouldThrowOutOfRange_WhenSampleOutsideFile(long at)
    {
        var file = Record(1.0);
        var insert = () => OpusSpliceEditor.Insert(file, at, Src(0.1, 880));
        var overwrite = () => Over(file, at, 0.1, 880);
        insert.Should().Throw<ArgumentOutOfRangeException>();
        overwrite.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Edits_ShouldThrowArgumentException_WhenChannelsMismatch()
    {
        var file = Record(1.0);
        var stereo = Audio(0.1, 880, 2);
        var insert = () => OpusSpliceEditor.Insert(file, 0, Const(stereo));
        var overwrite = () => Over(file, 0, 0.1, 880, 2);
        insert.Should().Throw<ArgumentException>();
        overwrite.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Insert_ShouldWorkOnStereoFile_WhenChannelsMatch()
    {
        var file = Record(1.0, 440, 2);
        var result = OpusSpliceEditor.Insert(file, 24000, Src(0.5, 880, 2));
        var (samples, err) = Decode(RoundTrip(result.File), 2);
        err.Should().BeEmpty();
        samples.Length.Should().Be((int)result.File.DurationSamples * 2);
    }

    [Fact]
    public void Insert_ShouldSpliceOnFortyMillisecondBoundaries_WhenFileHasLongPackets()
    {
        var file = Record(2.0);
        var merged = new List<byte[]>();
        using (var rp = new OpusRepacketizer())
        {
            for (var i = 0; i + 1 < file.AudioPackets.Count; i += 2)
            {
                rp.Reset();
                rp.Add(file.AudioPackets[i]);
                rp.Add(file.AudioPackets[i + 1]);
                merged.Add(rp.Output());
            }
        }

        merged.Should().OnlyContain(p => OpusToc.GetSampleCount(p) == 1920);
        // 101 packets -> 50 merged; the odd final packet is dropped, so the trim is reset to keep the duration valid
        var longFile = file with { AudioPackets = merged, EndTrim = 0 };

        var result = OpusSpliceEditor.Insert(longFile, 48000, Src(0.1, 880));

        result.SnappedSample.Should().Be(1920L * 25 - 312);
        result.File.AudioPackets.Take(25).Should().Equal(merged.Take(25));
        result.File.AudioPackets.Skip(25 + 6).Should().Equal(merged.Skip(25));
        Decode(RoundTrip(result.File)).Stderr.Should().BeEmpty();

        var over = Over(longFile, 48000, 0.1, 880);
        over.ReplacedSamples.Should().Be(1920 * 2);
        over.File.AudioPackets.Skip(25 + 4).Should().Equal(merged.Skip(25 + 2));
        over.File.DurationSamples.Should().Be(longFile.DurationSamples);
        Decode(RoundTrip(over.File)).Stderr.Should().BeEmpty();
    }

    [Fact]
    public void Edits_ShouldNotMutateInputFile_WhenSplicing()
    {
        var file = Record(1.0);
        var before = file.AudioPackets.ToList();
        OpusSpliceEditor.Insert(file, 24000, Src(0.1, 880));
        Over(file, 24000, 0.1, 880);
        file.AudioPackets.Should().Equal(before);
    }

    private OggOpusFile FfmpegRecording(int frameDurationMs, double seconds = 2.0, double frequency = 440)
    {
        var path = Path.Combine(_dir, Guid.NewGuid() + ".opus");
        var psi = new ProcessStartInfo("ffmpeg") { RedirectStandardError = true };
        foreach (var a in new[]
                 {
                     "-v", "error", "-f", "lavfi", "-i", FormattableString.Invariant($"sine=frequency={frequency}:duration={seconds}:sample_rate=48000"),
                     "-c:a", "libopus", "-frame_duration", frameDurationMs.ToString(), "-y", path
                 })
            psi.ArgumentList.Add(a);
        using var proc = Process.Start(psi)!;
        var err = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        proc.ExitCode.Should().Be(0, err);
        using var fs = File.OpenRead(path);
        return OggOpusFile.Read(fs);
    }

    [Fact]
    public void Overwrite_ShouldKeepExactLengthAndResumeOriginal_WhenFileHas60MsPackets()
    {
        var file = FfmpegRecording(60);
        var originals = file.AudioPackets.ToList();
        originals.Should().OnlyContain(p => OpusToc.GetSampleCount(p) == 2880);

        var result = Over(file, 48000, 0.5, 880);

        result.File.DurationSamples.Should().Be(file.DurationSamples);
        result.ReplacedSamples.Should().BeLessThanOrEqualTo(24000).And.BeGreaterThan(0);
        (result.ReplacedSamples % 2880).Should().Be(0);
        var firstReplaced = (int)((result.SnappedSample + file.Head.PreSkip) / 2880);
        var removed = (int)(result.ReplacedSamples / 2880);
        var added = result.File.AudioPackets.Count - originals.Count + removed;
        result.File.AudioPackets.Take(firstReplaced).Should().Equal(originals.Take(firstReplaced));
        result.File.AudioPackets.Skip(firstReplaced + added).Should().Equal(originals.Skip(firstReplaced + removed));
        added.Should().Be((int)(result.ReplacedSamples / 960));

        var (samples, err) = Decode(RoundTrip(result.File));
        err.Should().BeEmpty();
        samples.Length.Should().Be((int)file.DurationSamples);
        AssertDominant(samples, 1.1, 1.4, 880, 440);
        AssertDominant(samples, 1.6, 1.9, 440, 880);
        AssertDominant(samples, 0.1, 0.9, 440, 880);
    }

    [Fact]
    public void Insert_ShouldKeepWholeTake_WhenPreSkipExceedsBoundaryStart()
    {
        var file = Record(2.0);
        var patched = file with { HeadPacket = new OpusHead(1, 1, 3840, 48000, 0, 0).ToBytes() };
        patched.DurationSamples.Should().Be(file.DurationSamples - (3840 - 312));
        var requests = new List<EncodeRequest>();
        var src = Src(1.0, 880);

        var result = OpusSpliceEditor.Insert(patched, 0, r => { requests.Add(r); return src(r); });

        requests.Select(r => r.FrontPadSamples).Should().Equal(0, 3840 - 312);
        result.SnappedSample.Should().Be(0);
        result.File.DurationSamples.Should().Be(patched.DurationSamples + (3840 - 312) + 48000 + 312);
        var (samples, err) = Decode(RoundTrip(result.File));
        err.Should().BeEmpty();
        samples.Length.Should().Be((int)result.File.DurationSamples);
        AssertDominant(samples, 0.005, 0.1, 880, 440);
        AssertDominant(samples, 0.5, 0.9, 880, 440);
        AssertDominant(samples, 1.1, 1.5, 440, 880);
    }

    [Fact]
    public void Overwrite_ShouldKeepTakeAndLength_WhenPreSkipExceedsBoundaryStartAtZero()
    {
        var file = Record(2.0) with { HeadPacket = new OpusHead(1, 1, 3840, 48000, 0, 0).ToBytes() };

        var result = Over(file, 0, 0.5, 880);

        result.SnappedSample.Should().Be(0);
        result.File.DurationSamples.Should().Be(file.DurationSamples);
        var (samples, err) = Decode(RoundTrip(result.File));
        err.Should().BeEmpty();
        AssertDominant(samples, 0.005, 0.1, 880, 440);
        AssertDominant(samples, 0.2, 0.45, 880, 440);
        AssertDominant(samples, 0.7, 1.0, 440, 880);
    }

    [Fact]
    public void Overwrite_ShouldKeepShortTake_WhenPreSkipExceedsBoundaryStartAtZero()
    {
        // Regression (D27): the front pad used to count against the run allowance, so a 50 ms take at 0 with PreSkip 3840 was dropped.
        var file = Record(2.0) with { HeadPacket = new OpusHead(1, 1, 3840, 48000, 0, 0).ToBytes() };
        var requests = new List<EncodeRequest>();
        var src = Src(0.05, 1000);

        var result = OpusSpliceEditor.Overwrite(file, 0, r => { requests.Add(r); return src(r); }, 2400);

        // spec v8 #32: shortest run >= (PreSkip - s) + N = 3840 + 2400 = 6240 -> seven 960-sample packets (6720)
        requests.Should().Equal(new EncodeRequest(0, 6720), new EncodeRequest(3528, 6720));
        result.ReplacedSamples.Should().Be(6720);
        result.File.DurationSamples.Should().Be(file.DurationSamples);
        var (samples, err) = Decode(RoundTrip(result.File));
        err.Should().BeEmpty();
        samples.Length.Should().Be((int)file.DurationSamples);
        AssertDominant(samples, 0.005, 0.045, 1000, 440);
    }

    [Fact]
    public void Overwrite_ShouldKeepWholeTake_WhenHalfSecondTakeAtZeroWithLargePreSkip()
    {
        var file = Record(2.0) with { HeadPacket = new OpusHead(1, 1, 3840, 48000, 0, 0).ToBytes() };

        var result = Over(file, 0, 0.5, 880);

        // spec v8 #32: shortest run >= 3840 + 24000 = 27840 -> 29 packets = 27840; audible new audio = 24000 = the whole take
        result.ReplacedSamples.Should().Be(27840);
        result.File.DurationSamples.Should().Be(file.DurationSamples);
        var (samples, err) = Decode(RoundTrip(result.File));
        err.Should().BeEmpty();
        AssertDominant(samples, 0.005, 0.49, 880, 440);
        AssertDominant(samples, 0.53, 0.9, 440, 880);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(312)]
    [InlineData(960)]
    [InlineData(1271)]
    [InlineData(1272)]
    [InlineData(2400)]
    [InlineData(24000)]
    public void Overwrite_ShouldKeepTakeAudible_WhenPreSkipIs3840AndTakeIsAtZero(int takeSamples)
    {
        // Regression (D27, spec v8 #32): the run used to be rounded down to whole packets, so takes of 312..1271 samples fell entirely inside the pre-skip.
        const int preSkip = 3840;
        var file = Record(2.0) with { HeadPacket = new OpusHead(1, 1, preSkip, 48000, 0, 0).ToBytes() };
        var pcm = OpusTestHelpers.Sine(1, takeSamples, 880);
        var packetSamples = 960;

        var result = OpusSpliceEditor.Overwrite(file, 0, r => _encoder.Encode(OpusPcmEncoder.PrependSilence(pcm, r.FrontPadSamples, 1).ToArray(), new OpusEncodeSettings(1), r.ExactSamples), takeSamples);

        result.SnappedSample.Should().Be(0);
        result.ReplacedSamples.Should().BeGreaterThanOrEqualTo(preSkip + takeSamples);
        (result.ReplacedSamples - packetSamples).Should().BeLessThan(preSkip + takeSamples);
        result.File.DurationSamples.Should().Be(file.DurationSamples);
        var (samples, err) = Decode(RoundTrip(result.File));
        err.Should().BeEmpty();
        samples.Length.Should().Be((int)file.DurationSamples);
        AssertDominant(samples, 0, Math.Min(takeSamples, 960) / (double)Rate, 880, 440);
    }

    [Fact]
    public void Insert_ShouldAppendAndReportDuration_WhenSnapsToEndBoundaryWithLargeEndTrim()
    {
        var file = Record(1.0) with { EndTrim = 900 };
        var audio = Audio(0.25, 880);
        var duration = file.DurationSamples;

        var result = OpusSpliceEditor.Insert(file, duration - 10, Const(audio));

        result.SnappedSample.Should().Be(duration);
        result.ReplacedSamples.Should().Be(0);
        result.File.AudioPackets.Take(file.AudioPackets.Count).Should().Equal(file.AudioPackets);
        result.File.AudioPackets.Skip(file.AudioPackets.Count).Should().Equal(audio.Packets);
        result.File.EndTrim.Should().Be(audio.EndTrim);
    }

    [Fact]
    public void Overwrite_ShouldAppendAndReportDuration_WhenSnapsToEndBoundaryWithLargeEndTrim()
    {
        var file = Record(1.0) with { EndTrim = 900 };
        var duration = file.DurationSamples;

        var result = Over(file, duration - 10, 0.25, 880);

        result.SnappedSample.Should().Be(duration);
        result.ReplacedSamples.Should().Be(0);
        result.File.AudioPackets.Take(file.AudioPackets.Count).Should().Equal(file.AudioPackets);
        result.File.AudioPackets.Should().HaveCount(file.AudioPackets.Count + 13);
    }
}
