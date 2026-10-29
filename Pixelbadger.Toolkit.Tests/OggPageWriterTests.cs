using FluentAssertions;
using Pixelbadger.Toolkit.Opus.Ogg;

namespace Pixelbadger.Toolkit.Tests;

public class OggPageWriterTests
{
    [Fact]
    public void WritePage_ShouldProduceHeaderLayout_WhenGivenSimplePage()
    {
        using var ms = new MemoryStream();
        new OggPageWriter(ms).WritePage(new OggPage(OggPageFlags.BeginOfStream, 1234, 0xAABBCCDD, 7, new byte[] { 3 }, new byte[] { 1, 2, 3 }));
        var b = ms.ToArray();

        b.Take(4).Should().Equal("OggS"u8.ToArray());
        b[4].Should().Be(0);
        b[5].Should().Be(2);
        BitConverter.ToInt64(b, 6).Should().Be(1234);
        BitConverter.ToUInt32(b, 14).Should().Be(0xAABBCCDDu);
        BitConverter.ToUInt32(b, 18).Should().Be(7u);
        b[26].Should().Be(1);
        b[27].Should().Be(3);
        b.Skip(28).Should().Equal(new byte[] { 1, 2, 3 });

        var copy = b.ToArray();
        var crc = BitConverter.ToUInt32(copy, 22);
        copy.AsSpan(22, 4).Clear();
        OggCrc.Compute(copy).Should().Be(crc);
    }

    [Fact]
    public void WritePage_ShouldThrow_WhenBodyDoesNotMatchSegmentTable()
    {
        using var ms = new MemoryStream();
        var act = () => new OggPageWriter(ms).WritePage(new OggPage(OggPageFlags.None, 0, 1, 0, new byte[] { 5 }, new byte[3]));
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void WritePage_ShouldThrow_WhenMoreThan255Segments()
    {
        using var ms = new MemoryStream();
        var act = () => new OggPageWriter(ms).WritePage(new OggPage(OggPageFlags.None, 0, 1, 0, new byte[256], Array.Empty<byte>()));
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void WritePage_ShouldRoundTripBytes_WhenReadBackAndRewritten()
    {
        var page = new OggPage(OggPageFlags.Continued | OggPageFlags.EndOfStream, -1, 42, 9, new byte[] { 255, 255, 10 }, new byte[520]);
        using var first = new MemoryStream();
        new OggPageWriter(first).WritePage(page);

        using var read = new MemoryStream(first.ToArray());
        var back = new OggPageReader(read).ReadPage()!;
        using var second = new MemoryStream();
        new OggPageWriter(second).WritePage(back);

        second.ToArray().Should().Equal(first.ToArray());
        back.GranulePosition.Should().Be(-1);
        back.Flags.Should().Be(OggPageFlags.Continued | OggPageFlags.EndOfStream);
    }
}
