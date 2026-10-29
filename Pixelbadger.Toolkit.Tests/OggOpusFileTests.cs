using System.Text;
using FluentAssertions;
using Pixelbadger.Toolkit.Opus.Ogg;

namespace Pixelbadger.Toolkit.Tests;

public class OggOpusFileTests : IDisposable
{
    private readonly string _dir;

    public OggOpusFileTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private string Gen(int channels, double seconds = 1)
    {
        var path = Path.Combine(_dir, $"gen{channels}-{Guid.NewGuid():N}.opus");
        OggTestSupport.GenerateOpus(path, channels, seconds);
        return path;
    }

    private static byte[] Packet(int marker, int length = 20) // CELT FB 20 ms code 0
    {
        var p = new byte[length];
        p[0] = 0xFC;
        for (var i = 1; i < length; i++) p[i] = (byte)(marker + i);
        return p;
    }

    private static byte[] WriteToBytes(OggOpusFile f, uint serial = 11)
    {
        using var ms = new MemoryStream();
        f.Write(ms, serial);
        return ms.ToArray();
    }

    private static List<OggPage> Pages(byte[] bytes)
    {
        var r = new OggPageReader(new MemoryStream(bytes));
        var list = new List<OggPage>();
        OggPage? p;
        while ((p = r.ReadPage()) != null) list.Add(p);
        return list;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Read_ShouldReportHeadAndDuration_WhenFileProducedByFfmpeg(int channels)
    {
        var path = Gen(channels);
        using var fs = File.OpenRead(path);
        var file = OggOpusFile.Read(fs);

        file.Head.ChannelCount.Should().Be((byte)channels);
        file.Head.PreSkip.Should().Be(312);
        file.Head.MappingFamily.Should().Be(0);
        file.DurationSamples.Should().BeInRange(48000 - 48, 48000 + 48);

        var probe = OggTestSupport.Run("ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "default=nw=1:nk=1", path);
        var seconds = double.Parse(probe.StdOut.Trim(), System.Globalization.CultureInfo.InvariantCulture);
        // ffprobe's format duration includes the 312-sample pre-skip (6.5 ms).
        (file.DurationSamples / 48000.0).Should().BeApproximately(seconds, 0.01);
        Encoding.ASCII.GetString(file.TagsPacket, 0, 8).Should().Be("OpusTags");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Write_ShouldReproduceEverything_WhenReadBack(int channels)
    {
        var path = Gen(channels, 2.5);
        var original = OggOpusFile.Read(new MemoryStream(File.ReadAllBytes(path)));
        var rewritten = Path.Combine(_dir, "rw.opus");
        File.WriteAllBytes(rewritten, WriteToBytes(original, 99));

        var back = OggOpusFile.Read(new MemoryStream(File.ReadAllBytes(rewritten)));
        back.HeadPacket.Should().Equal(original.HeadPacket);
        back.TagsPacket.Should().Equal(original.TagsPacket);
        back.EndTrim.Should().Be(original.EndTrim);
        back.AudioPackets.Should().HaveCount(original.AudioPackets.Count);
        for (var i = 0; i < back.AudioPackets.Count; i++)
            back.AudioPackets[i].Should().Equal(original.AudioPackets[i]);
        back.DurationSamples.Should().Be(original.DurationSamples);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Write_ShouldProduceFileFfmpegDecodesCleanly_WhenRewritten(int channels)
    {
        var original = OggOpusFile.Read(new MemoryStream(File.ReadAllBytes(Gen(channels, 2))));
        var rewritten = Path.Combine(_dir, "rw.opus");
        File.WriteAllBytes(rewritten, WriteToBytes(original));

        var result = OggTestSupport.Run("ffmpeg", "-v", "error", "-i", rewritten, "-f", "null", "-");
        result.ExitCode.Should().Be(0);
        result.StdErr.Should().BeEmpty();

        var probe = OggTestSupport.Run("ffprobe", "-v", "error", "-show_entries", "stream=codec_name,channels", "-of", "csv=p=0", rewritten);
        probe.StdOut.Trim().Should().Be($"opus,{channels}");
    }

    [Fact]
    public void Write_ShouldFollowPageLayoutRules_WhenAudioSpansSeveralSeconds()
    {
        var packets = Enumerable.Range(0, 120).Select(i => Packet(i)).ToList();
        var file = OggOpusFile.Create(new OpusHead(1, 1, 312, 48000, 0, 0), packets, 100);
        var pages = Pages(WriteToBytes(file, 5));

        pages[0].Flags.Should().Be(OggPageFlags.BeginOfStream);
        pages[0].SegmentTable.Should().Equal(new byte[] { 19 });
        pages[0].Body.Should().Equal(file.HeadPacket);
        pages[0].GranulePosition.Should().Be(0);
        pages[1].Body.Should().Equal(file.TagsPacket);
        pages.Select(p => p.SequenceNumber).Should().Equal(Enumerable.Range(0, pages.Count).Select(i => (uint)i));
        pages.Select(p => p.SerialNumber).Distinct().Should().Equal(5u);
        pages[^1].Flags.HasFlag(OggPageFlags.EndOfStream).Should().BeTrue();
        pages.Take(pages.Count - 1).Any(p => p.Flags.HasFlag(OggPageFlags.EndOfStream)).Should().BeFalse();

        // 120 packets x 960 = 115200 samples; pages flush after reaching 48000 => 50, 50, 20 packets.
        var audio = pages.Skip(2).ToList();
        audio.Should().HaveCount(3);
        audio.Select(p => p.SegmentTable.Length).Should().Equal(50, 50, 20);
        audio[0].GranulePosition.Should().Be(48000);
        audio[1].GranulePosition.Should().Be(96000);
        audio[2].GranulePosition.Should().Be(120 * 960 - 100);
    }

    [Fact]
    public void Write_ShouldSetGranuleMinusOne_WhenNoPacketCompletesOnPage()
    {
        var big = new byte[255 * 255 * 2 + 10]; // spans 3 pages
        big[0] = 0xFC;
        var file = OggOpusFile.Create(new OpusHead(1, 1, 312, 48000, 0, 0), new List<byte[]> { big }, 0);
        var pages = Pages(WriteToBytes(file)).Skip(2).ToList();

        pages.Should().HaveCount(3);
        pages[0].GranulePosition.Should().Be(-1);
        pages[0].SegmentTable.Length.Should().Be(255);
        pages[1].Flags.HasFlag(OggPageFlags.Continued).Should().BeTrue();
        pages[1].GranulePosition.Should().Be(-1);
        pages[2].Flags.HasFlag(OggPageFlags.Continued).Should().BeTrue();
        pages[2].GranulePosition.Should().Be(960);
        OggOpusFile.Read(new MemoryStream(WriteToBytes(file))).AudioPackets[0].Should().Equal(big);
    }

    [Fact]
    public void Write_ShouldAddTrailingZeroSegment_WhenPacketIsExactlyMultipleOf255()
    {
        var p = new byte[255];
        p[0] = 0xFC;
        var file = OggOpusFile.Create(new OpusHead(1, 1, 0, 48000, 0, 0), new List<byte[]> { p }, 0);
        var audio = Pages(WriteToBytes(file)).Skip(2).Single();

        audio.SegmentTable.Should().Equal(new byte[] { 255, 0 });
        OggOpusFile.Read(new MemoryStream(WriteToBytes(file))).AudioPackets.Single().Should().Equal(p);
    }

    [Fact]
    public void Write_ShouldHandlePacketCompletingExactlyOnFullPage()
    {
        var p1 = new byte[254 * 255 + 10]; // 255 segments exactly: 254 full + 1 short
        p1[0] = 0xFC;
        var p2 = Packet(3);
        var file = OggOpusFile.Create(new OpusHead(1, 1, 0, 48000, 0, 0), new List<byte[]> { p1, p2 }, 0);
        var pages = Pages(WriteToBytes(file)).Skip(2).ToList();

        pages.Should().HaveCount(2);
        pages[0].SegmentTable.Length.Should().Be(255);
        pages[0].GranulePosition.Should().Be(960);
        pages[1].Flags.HasFlag(OggPageFlags.Continued).Should().BeFalse();
        var back = OggOpusFile.Read(new MemoryStream(WriteToBytes(file)));
        back.AudioPackets[0].Should().Equal(p1);
        back.AudioPackets[1].Should().Equal(p2);
    }

    [Fact]
    public void Write_ShouldEndStreamOnTagsPage_WhenNoAudioPackets()
    {
        var file = OggOpusFile.Create(new OpusHead(1, 1, 312, 48000, 0, 0), new List<byte[]>(), 0);
        var bytes = WriteToBytes(file);
        var pages = Pages(bytes);

        pages.Should().HaveCount(2);
        pages[1].Flags.HasFlag(OggPageFlags.EndOfStream).Should().BeTrue();
        var back = OggOpusFile.Read(new MemoryStream(bytes));
        back.AudioPackets.Should().BeEmpty();
        back.DurationSamples.Should().Be(0);
    }

    [Fact]
    public void Read_ShouldComputeEndTrimFromLastGranule()
    {
        var packets = Enumerable.Range(0, 10).Select(i => Packet(i)).ToList();
        var file = OggOpusFile.Create(new OpusHead(1, 1, 312, 48000, 0, 0), packets, 500);
        var back = OggOpusFile.Read(new MemoryStream(WriteToBytes(file)));

        back.EndTrim.Should().Be(500);
        back.TotalPacketSamples.Should().Be(9600);
        back.DurationSamples.Should().Be(9600 - 312 - 500);
    }

    [Fact]
    public void DurationSamples_ShouldNeverBeNegative()
    {
        var file = OggOpusFile.Create(new OpusHead(1, 1, 312, 48000, 0, 0), new List<byte[]> { Packet(1) }, 900);
        file.DurationSamples.Should().Be(0);
    }

    [Fact]
    public void Create_ShouldBuildTagsWithPixelbadgerVendor()
    {
        var file = OggOpusFile.Create(new OpusHead(1, 2, 312, 48000, 0, 0), new List<byte[]>(), 0);
        var tags = file.TagsPacket;

        Encoding.ASCII.GetString(tags, 0, 8).Should().Be("OpusTags");
        var vendorLen = BitConverter.ToUInt32(tags, 8);
        Encoding.UTF8.GetString(tags, 12, (int)vendorLen).Should().Be("Pixelbadger.Toolkit");
        BitConverter.ToUInt32(tags, 12 + (int)vendorLen).Should().Be(0u);
        tags.Length.Should().Be(12 + (int)vendorLen + 4);
        file.HeadPacket.Should().Equal(new OpusHead(1, 2, 312, 48000, 0, 0).ToBytes());
    }

    [Fact]
    public void Read_ShouldThrowChained_WhenTwoFilesConcatenated()
    {
        var bytes = File.ReadAllBytes(Gen(1));
        var act = () => OggOpusFile.Read(new MemoryStream(bytes.Concat(bytes).ToArray()));
        act.Should().Throw<InvalidDataException>().WithMessage("Chained or multiplexed Ogg streams are not supported");
    }

    [Fact]
    public void Read_ShouldThrowChained_WhenSecondSerialAppears()
    {
        using var ms = new MemoryStream();
        var w = new OggPageWriter(ms);
        w.WritePage(new OggPage(OggPageFlags.BeginOfStream, 0, 1, 0, new byte[] { 19 }, new OpusHead(1, 1, 0, 48000, 0, 0).ToBytes()));
        w.WritePage(new OggPage(OggPageFlags.None, 0, 2, 0, new byte[] { 1 }, new byte[] { 1 }));
        var act = () => OggOpusFile.Read(new MemoryStream(ms.ToArray()));
        act.Should().Throw<InvalidDataException>().WithMessage("Chained or multiplexed Ogg streams are not supported");
    }

    [Fact]
    public void Read_ShouldThrowFamilyMessage_WhenMappingFamilyOne()
    {
        var head = new OpusHead(1, 6, 312, 48000, 0, 0).ToBytes();
        head[18] = 1;
        using var ms = new MemoryStream();
        new OggPageWriter(ms).WritePage(new OggPage(OggPageFlags.BeginOfStream, 0, 1, 0, new byte[] { 19 }, head));
        var act = () => OggOpusFile.Read(new MemoryStream(ms.ToArray()));
        act.Should().Throw<InvalidDataException>()
            .WithMessage("Unsupported Ogg Opus channel mapping family 1; only mono and stereo files are supported");
    }

    [Fact]
    public void Read_ShouldThrowNotOggOpus_WhenFirstPacketIsVorbisLike()
    {
        var vorbis = new byte[] { 1, (byte)'v', (byte)'o', (byte)'r', (byte)'b', (byte)'i', (byte)'s', 0, 0, 0, 0, 0, 1, 0x44, 0xAC, 0, 0 };
        using var ms = new MemoryStream();
        new OggPageWriter(ms).WritePage(new OggPage(OggPageFlags.BeginOfStream, 0, 1, 0, new[] { (byte)vorbis.Length }, vorbis));
        var act = () => OggOpusFile.Read(new MemoryStream(ms.ToArray()));
        act.Should().Throw<InvalidDataException>().WithMessage("Not an Ogg Opus file");
    }

    [Fact]
    public void Read_ShouldThrowNotOggOpus_WhenInputIsGarbageOrEmpty()
    {
        var garbage = Enumerable.Range(0, 200).Select(i => (byte)i).ToArray();
        var act = () => OggOpusFile.Read(new MemoryStream(garbage));
        act.Should().Throw<InvalidDataException>().WithMessage("Not an Ogg Opus file");
        var empty = () => OggOpusFile.Read(new MemoryStream());
        empty.Should().Throw<InvalidDataException>().WithMessage("Not an Ogg Opus file");
    }

    [Fact]
    public void Read_ShouldThrowNotOggOpus_WhenFirstPageLacksBeginOfStream()
    {
        using var ms = new MemoryStream();
        new OggPageWriter(ms).WritePage(new OggPage(OggPageFlags.None, 0, 1, 0, new byte[] { 19 }, new OpusHead(1, 1, 0, 48000, 0, 0).ToBytes()));
        var act = () => OggOpusFile.Read(new MemoryStream(ms.ToArray()));
        act.Should().Throw<InvalidDataException>().WithMessage("Not an Ogg Opus file");
    }

    [Fact]
    public void Read_ShouldThrowMissingTags_WhenSecondPacketIsNotOpusTags()
    {
        using var ms = new MemoryStream();
        var w = new OggPageWriter(ms);
        w.WritePage(new OggPage(OggPageFlags.BeginOfStream, 0, 1, 0, new byte[] { 19 }, new OpusHead(1, 1, 0, 48000, 0, 0).ToBytes()));
        w.WritePage(new OggPage(OggPageFlags.None, 0, 1, 1, new byte[] { 4 }, new byte[] { 1, 2, 3, 4 }));
        var act = () => OggOpusFile.Read(new MemoryStream(ms.ToArray()));
        act.Should().Throw<InvalidDataException>().WithMessage("Ogg Opus file is missing its OpusTags header");
    }

    [Fact]
    public void Read_ShouldThrowTruncated_WhenFileCutMidPage()
    {
        var bytes = File.ReadAllBytes(Gen(1));
        var act = () => OggOpusFile.Read(new MemoryStream(bytes[..(bytes.Length - 10)]));
        act.Should().Throw<InvalidDataException>().WithMessage("Truncated Ogg page at byte offset *");
    }

    [Fact]
    public void Read_ShouldThrowCrcMismatch_WhenAudioByteCorrupted()
    {
        var bytes = File.ReadAllBytes(Gen(1));
        bytes[^5] ^= 0x55;
        var act = () => OggOpusFile.Read(new MemoryStream(bytes));
        act.Should().Throw<InvalidDataException>().WithMessage("Ogg page CRC mismatch at byte offset *");
    }

    [Fact]
    public void Read_ShouldThrow_WhenPacketLeftIncompleteAtEndOfStream()
    {
        using var ms = new MemoryStream();
        var w = new OggPageWriter(ms);
        w.WritePage(new OggPage(OggPageFlags.BeginOfStream, 0, 1, 0, new byte[] { 19 }, new OpusHead(1, 1, 0, 48000, 0, 0).ToBytes()));
        w.WritePage(new OggPage(OggPageFlags.None, 0, 1, 1, new byte[] { 8 }, "OpusTags"u8.ToArray()));
        w.WritePage(new OggPage(OggPageFlags.EndOfStream, -1, 1, 2, new byte[] { 255 }, new byte[255]));
        var act = () => OggOpusFile.Read(new MemoryStream(ms.ToArray()));
        act.Should().Throw<InvalidDataException>().WithMessage("Truncated Ogg stream*");
    }
}
