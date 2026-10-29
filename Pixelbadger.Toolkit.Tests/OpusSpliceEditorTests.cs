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

    private Func<int?, EncodedAudio> Source(double seconds, double frequency, int channels = 1)
    {
        var pcm = OpusTestHelpers.Sine(channels, (int)(seconds * Rate), frequency);
        return k => _encoder.Encode(pcm, new OpusEncodeSettings(channels), k);
    }

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

        var result = OpusSpliceEditor.Insert(file, 48000, audio);

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
        var result = OpusSpliceEditor.Insert(file, 0, Audio(0.5, 880));
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
        var result = OpusSpliceEditor.Insert(file, file.DurationSamples, audio);
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

        var result = OpusSpliceEditor.Overwrite(file, 48000, Source(0.5, 880));

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

        var result = OpusSpliceEditor.Overwrite(file, 72000, k => k is null ? full : throw new InvalidOperationException("no truncated encode expected"));

        result.SnappedSample.Should().Be(71688);
        result.File.DurationSamples.Should().Be(result.SnappedSample + 312 + 48000);
        result.File.EndTrim.Should().Be(full.EndTrim);
        result.ReplacedSamples.Should().Be(originals.Count * 960L - 75 * 960);
        result.File.AudioPackets.Take(75).Should().Equal(originals.Take(75));
        result.File.AudioPackets.Skip(75).Should().Equal(full.Packets);
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
        var result = OpusSpliceEditor.Overwrite(file, file.DurationSamples, Source(0.25, 880));
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

        var result = OpusSpliceEditor.Insert(file, file.DurationSamples, audio);

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
        var result = OpusSpliceEditor.Overwrite(file, file.DurationSamples, k => k is null ? full : throw new InvalidOperationException());
        result.SnappedSample.Should().Be(96000);
        result.ReplacedSamples.Should().Be(0);
        result.File.EndTrim.Should().Be(full.EndTrim);
        result.File.AudioPackets.Take(file.AudioPackets.Count).Should().Equal(file.AudioPackets);
        result.File.AudioPackets.Skip(file.AudioPackets.Count).Should().Equal(full.Packets);
        result.File.DurationSamples.Should().Be(144960);
        var (samples, err) = Decode(RoundTrip(result.File));
        err.Should().BeEmpty();
        AssertDominant(samples, 2.1, 2.9, 880, 440);
    }

    [Fact]
    public void Insert_ShouldUseNearestBoundary_WhenOneSampleBeforeDuration()
    {
        var file = Record(2.0);
        var audio = Audio(0.1, 880);
        var result = OpusSpliceEditor.Insert(file, file.DurationSamples - 1, audio);
        // boundaries at 960i-312: 95688 (i=100) is nearest to 95999; the end boundary (96648) is not chosen
        result.SnappedSample.Should().Be(95688);
        result.File.AudioPackets.Take(100).Should().Equal(file.AudioPackets.Take(100));
        result.File.AudioPackets.Skip(100 + audio.Packets.Count).Should().Equal(file.AudioPackets.Skip(100));
        result.File.EndTrim.Should().Be(file.EndTrim);
    }

    [Fact]
    public void Insert_ShouldStartNewAudioAtBoundaryPlusLookahead_WhenInsertingAtOneSecond()
    {
        var file = Record(2.0);
        var result = OpusSpliceEditor.Insert(file, 48000, Audio(1.0, 880));
        result.SnappedSample.Should().Be(47688);
        var (samples, err) = Decode(RoundTrip(result.File));
        err.Should().BeEmpty();
        Goertzel(samples, 1.01, 1.05, 880).Should().BeGreaterThan(Goertzel(samples, 1.01, 1.05, 440));
    }

    [Fact]
    public void Overwrite_ShouldTreatAsReachingEnd_WhenNoFollowingPacketCoversRun()
    {
        var file = Record(1.0);
        // S + N < D but the run of K packets would extend past the last packet start: falls back to case (b).
        var result = OpusSpliceEditor.Overwrite(file, 47000, Source(0.0205, 880));
        result.File.AudioPackets.Count.Should().BeLessThanOrEqualTo(file.AudioPackets.Count + 1);
        Decode(result.File).Stderr.Should().BeEmpty();
    }

    [Fact]
    public void Overwrite_ShouldReplaceOnePacketMinimum_WhenShorterThan20Ms()
    {
        var file = Record(1.0);
        var result = OpusSpliceEditor.Overwrite(file, 24000, Source(0.005, 880));
        result.ReplacedSamples.Should().Be(960);
        result.File.AudioPackets.Should().HaveCount(file.AudioPackets.Count);
        result.File.DurationSamples.Should().Be(file.DurationSamples);
    }

    [Fact]
    public void Snap_ShouldSnapToNearestBoundary_WhenPreSkipIsZero()
    {
        var file = Record(2.0);
        file = file with { HeadPacket = new OpusHead(1, 1, 0, 48000, 0, 0).ToBytes() };
        var result = OpusSpliceEditor.Insert(file, 48624L, Audio(0.1, 880));
        result.SnappedSample.Should().Be(48960);
        result.RequestedSample.Should().Be(48624);
    }

    [Fact]
    public void Snap_ShouldSnapOnPreSkipShiftedBoundaries_WhenRecordingHasPreSkip()
    {
        var file = Record(2.0);
        var result = OpusSpliceEditor.Insert(file, 48624, Audio(0.1, 880));
        result.SnappedSample.Should().Be(48648);
    }

    [Fact]
    public void Snap_ShouldPreferEarlierBoundary_WhenTied()
    {
        var file = Record(2.0);
        // boundaries at 47688 and 48648; midpoint 48168
        OpusSpliceEditor.Insert(file, 48168, Audio(0.1, 880)).SnappedSample.Should().Be(47688);
        OpusSpliceEditor.Insert(file, 48169, Audio(0.1, 880)).SnappedSample.Should().Be(48648);
    }

    [Fact]
    public void Snap_ShouldMapToZero_WhenAtZeroOrWithinFirstBoundaryPair()
    {
        var file = Record(1.0);
        OpusSpliceEditor.Insert(file, 0, Audio(0.1, 880)).SnappedSample.Should().Be(0);
        // boundaries 0 (packet 0, clamped) and 648 (packet 1) tie at 324 -> earlier
        OpusSpliceEditor.Insert(file, 324, Audio(0.1, 880)).SnappedSample.Should().Be(0);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(48001)]
    public void Edits_ShouldThrowOutOfRange_WhenSampleOutsideFile(long at)
    {
        var file = Record(1.0);
        var insert = () => OpusSpliceEditor.Insert(file, at, Audio(0.1, 880));
        var overwrite = () => OpusSpliceEditor.Overwrite(file, at, Source(0.1, 880));
        insert.Should().Throw<ArgumentOutOfRangeException>();
        overwrite.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Edits_ShouldThrowArgumentException_WhenChannelsMismatch()
    {
        var file = Record(1.0);
        var stereo = Audio(0.1, 880, 2);
        var insert = () => OpusSpliceEditor.Insert(file, 0, stereo);
        var overwrite = () => OpusSpliceEditor.Overwrite(file, 0, Source(0.1, 880, 2));
        insert.Should().Throw<ArgumentException>();
        overwrite.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Insert_ShouldWorkOnStereoFile_WhenChannelsMatch()
    {
        var file = Record(1.0, 440, 2);
        var result = OpusSpliceEditor.Insert(file, 24000, Audio(0.5, 880, 2));
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

        var result = OpusSpliceEditor.Insert(longFile, 48000, Audio(0.1, 880));

        result.SnappedSample.Should().Be(1920L * 25 - 312);
        result.File.AudioPackets.Take(25).Should().Equal(merged.Take(25));
        result.File.AudioPackets.Skip(25 + 6).Should().Equal(merged.Skip(25));
        Decode(RoundTrip(result.File)).Stderr.Should().BeEmpty();

        var over = OpusSpliceEditor.Overwrite(longFile, 48000, Source(0.1, 880));
        over.ReplacedSamples.Should().Be(1920 * 3);
        over.File.AudioPackets.Skip(25 + 5).Should().Equal(merged.Skip(25 + 3));
        Decode(RoundTrip(over.File)).Stderr.Should().BeEmpty();
    }

    [Fact]
    public void Edits_ShouldNotMutateInputFile_WhenSplicing()
    {
        var file = Record(1.0);
        var before = file.AudioPackets.ToList();
        OpusSpliceEditor.Insert(file, 24000, Audio(0.1, 880));
        OpusSpliceEditor.Overwrite(file, 24000, Source(0.1, 880));
        file.AudioPackets.Should().Equal(before);
    }
}
