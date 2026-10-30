using FluentAssertions;
using Pixelbadger.Toolkit.Opus.Ogg;

namespace Pixelbadger.Toolkit.Tests;

public class OggPageReaderTests : IDisposable
{
    private readonly string _dir;

    public OggPageReaderTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private static byte[] Serialize(params OggPage[] pages)
    {
        using var ms = new MemoryStream();
        var w = new OggPageWriter(ms);
        foreach (var p in pages) w.WritePage(p);
        return ms.ToArray();
    }

    private static OggPage SimplePage(byte[]? body = null, uint seq = 0)
    {
        body ??= new byte[] { 1, 2, 3, 4, 5 };
        return new OggPage(OggPageFlags.None, 0, 1, seq, new[] { (byte)body.Length }, body);
    }

    [Fact]
    public void ReadPage_ShouldReturnNull_WhenStreamEmpty()
    {
        new OggPageReader(new MemoryStream()).ReadPage().Should().BeNull();
    }

    [Fact]
    public void ReadPage_ShouldReadAllFields_WhenPageValid()
    {
        var page = new OggPage(OggPageFlags.BeginOfStream, 99, 77, 3, new byte[] { 2, 1 }, new byte[] { 9, 8, 7 });
        var read = new OggPageReader(new MemoryStream(Serialize(page))).ReadPage()!;

        read.Flags.Should().Be(OggPageFlags.BeginOfStream);
        read.GranulePosition.Should().Be(99);
        read.SerialNumber.Should().Be(77u);
        read.SequenceNumber.Should().Be(3u);
        read.SegmentTable.Should().Equal(page.SegmentTable);
        read.Body.Should().Equal(page.Body);
    }

    [Fact]
    public void ReadPage_ShouldReportCrcMismatchWithOffset_WhenBodyByteCorrupted()
    {
        var first = Serialize(SimplePage());
        var bytes = Serialize(SimplePage(), SimplePage(seq: 1));
        bytes[first.Length + bytes.Length - first.Length - 1] ^= 0xFF; // last body byte of page 2
        var reader = new OggPageReader(new MemoryStream(bytes));

        reader.ReadPage().Should().NotBeNull();
        var act = () => reader.ReadPage();
        act.Should().Throw<InvalidDataException>().WithMessage($"Ogg page CRC mismatch at byte offset {first.Length}");
    }

    [Fact]
    public void ReadPage_ShouldReportTruncated_WhenBodyCutShort()
    {
        var bytes = Serialize(SimplePage());
        var reader = new OggPageReader(new MemoryStream(bytes[..^2]));
        var act = () => reader.ReadPage();
        act.Should().Throw<InvalidDataException>().WithMessage("Truncated Ogg page at byte offset 0");
    }

    [Fact]
    public void ReadPage_ShouldReportTruncated_WhenHeaderCutShort()
    {
        var bytes = Serialize(SimplePage());
        var act = () => new OggPageReader(new MemoryStream(bytes[..10])).ReadPage();
        act.Should().Throw<InvalidDataException>().WithMessage("Truncated Ogg page at byte offset 0");
    }

    [Fact]
    public void ReadPage_ShouldReportTruncated_WhenSegmentTableCutShort()
    {
        var bytes = Serialize(SimplePage());
        var act = () => new OggPageReader(new MemoryStream(bytes[..27])).ReadPage();
        act.Should().Throw<InvalidDataException>().WithMessage("Truncated Ogg page at byte offset 0");
    }

    [Fact]
    public void ReadPage_ShouldReportInvalid_WhenCapturePatternWrong()
    {
        var bytes = Serialize(SimplePage());
        bytes[0] = (byte)'X';
        var act = () => new OggPageReader(new MemoryStream(bytes)).ReadPage();
        act.Should().Throw<InvalidDataException>().WithMessage("Invalid Ogg page at byte offset 0");
    }

    [Fact]
    public void ReadPage_ShouldReportInvalid_WhenVersionNonZero()
    {
        var bytes = Serialize(SimplePage());
        bytes[4] = 1;
        var act = () => new OggPageReader(new MemoryStream(bytes)).ReadPage();
        act.Should().Throw<InvalidDataException>().WithMessage("Invalid Ogg page at byte offset 0");
    }

    [Fact]
    public void ReadPage_ShouldReportInvalidAtLaterOffset_WhenGarbageFollowsPage()
    {
        var first = Serialize(SimplePage());
        var bytes = first.Concat(new byte[40]).ToArray();
        var reader = new OggPageReader(new MemoryStream(bytes));
        reader.ReadPage();
        var act = () => reader.ReadPage();
        act.Should().Throw<InvalidDataException>().WithMessage($"Invalid Ogg page at byte offset {first.Length}");
    }

    [Theory]
    [InlineData(255)]
    [InlineData(510)]
    [InlineData(254)]
    [InlineData(65025)]
    [InlineData(70000)]
    public void Reader_ShouldRoundTripPacketsAcrossPages_WhenPacketSizeIsALacingEdge(int length)
    {
        var packet = Enumerable.Range(0, length).Select(i => (byte)(i * 7 + 1)).ToArray();
        var file = new OggOpusFile(
            new OpusHead(1, 1, 312, 48000, 0, 0).ToBytes(),
            new byte[] { (byte)'O', (byte)'p', (byte)'u', (byte)'s', (byte)'T', (byte)'a', (byte)'g', (byte)'s', 0, 0, 0, 0, 0, 0, 0, 0 },
            new List<byte[]> { MakePacket(packet), MakePacket(new byte[] { 0xFC, 1, 2 }) },
            0);
        using var ms = new MemoryStream();
        file.Write(ms, 5);

        var back = OggOpusFile.Read(new MemoryStream(ms.ToArray()));
        back.AudioPackets[0].Should().Equal(file.AudioPackets[0]);
        back.AudioPackets[1].Should().Equal(file.AudioPackets[1]);
    }

    private static byte[] MakePacket(byte[] payload)
    {
        var p = payload.ToArray();
        p[0] = 0xFC; // CELT fullband 20 ms, code 0
        return p;
    }

    [Fact]
    public void ReadPage_ShouldAcceptEveryPage_WhenFileProducedByFfmpeg()
    {
        var path = Path.Combine(_dir, "f.opus");
        OggTestSupport.GenerateOpus(path, seconds: 3);
        using var fs = File.OpenRead(path);
        var reader = new OggPageReader(fs);
        var count = 0;
        OggPage? page;
        uint expectedSeq = 0;
        while ((page = reader.ReadPage()) != null)
        {
            page.SequenceNumber.Should().Be(expectedSeq++);
            count++;
        }
        count.Should().BeGreaterThan(2);
    }

    [Fact]
    public void ReadPage_ShouldRoundTripBytesExactly_WhenRewritingFfmpegPages()
    {
        var path = Path.Combine(_dir, "f.opus");
        OggTestSupport.GenerateOpus(path, seconds: 2);
        var original = File.ReadAllBytes(path);

        using var rewritten = new MemoryStream();
        var writer = new OggPageWriter(rewritten);
        var reader = new OggPageReader(new MemoryStream(original));
        OggPage? page;
        while ((page = reader.ReadPage()) != null) writer.WritePage(page);

        rewritten.ToArray().Should().Equal(original);
    }
}
