using FluentAssertions;
using Pixelbadger.Toolkit.Opus.Ogg;

namespace Pixelbadger.Toolkit.Tests;

public class OggCrcTests : IDisposable
{
    private readonly string _dir;

    public OggCrcTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void Compute_ShouldReturnZero_WhenDataIsEmpty()
    {
        OggCrc.Compute(ReadOnlySpan<byte>.Empty).Should().Be(0u);
    }

    [Fact]
    public void Compute_ShouldMatchKnownVector_WhenGivenCheckString()
    {
        // CRC-32/MPEG-2-like (poly 0x04C11DB7, init 0, no reflection, no xor-out) of "123456789" is 0x89A1897F.
        OggCrc.Compute("123456789"u8).Should().Be(0x89A1897Fu);
    }

    [Fact]
    public void Compute_ShouldMatchStoredCrc_WhenPageProducedByFfmpeg()
    {
        var path = Path.Combine(_dir, "a.opus");
        OggTestSupport.GenerateOpus(path);
        var bytes = File.ReadAllBytes(path);

        // First page of a real ffmpeg file: verify the stored CRC independently of OggPageReader.
        var segs = bytes[26];
        var pageLen = 27 + segs + bytes.Skip(27).Take(segs).Sum(b => (int)b);
        var page = bytes.AsSpan(0, pageLen).ToArray();
        var stored = BitConverter.ToUInt32(page, 22);
        page.AsSpan(22, 4).Clear();

        OggCrc.Compute(page).Should().Be(stored);

        using var ms = new MemoryStream(bytes);
        new OggPageReader(ms).ReadPage().Should().NotBeNull();
    }

    [Fact]
    public void Compute_ShouldDiffer_WhenOneBitFlips()
    {
        var a = new byte[] { 1, 2, 3, 4 };
        var b = new byte[] { 1, 2, 3, 5 };
        OggCrc.Compute(a).Should().NotBe(OggCrc.Compute(b));
    }
}
