using System.Buffers.Binary;

namespace Pixelbadger.Toolkit.Opus.Ogg;

public sealed class OggPageWriter
{
    private readonly Stream _stream;

    public OggPageWriter(Stream s)
    {
        _stream = s ?? throw new ArgumentNullException(nameof(s));
    }

    public void WritePage(OggPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (page.SegmentTable.Length > 255)
            throw new ArgumentException("An Ogg page holds at most 255 segments", nameof(page));
        var expectedBody = 0;
        foreach (var s in page.SegmentTable) expectedBody += s;
        if (expectedBody != page.Body.Length)
            throw new ArgumentException("Ogg page body length does not match its segment table", nameof(page));

        var bytes = Serialize(page);
        _stream.Write(bytes, 0, bytes.Length);
    }

    internal static byte[] Serialize(OggPage page)
    {
        var headerLength = 27 + page.SegmentTable.Length;
        var bytes = new byte[headerLength + page.Body.Length];
        "OggS"u8.CopyTo(bytes);
        bytes[4] = 0;
        bytes[5] = (byte)page.Flags;
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(6), page.GranulePosition);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(14), page.SerialNumber);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(18), page.SequenceNumber);
        bytes[26] = (byte)page.SegmentTable.Length;
        page.SegmentTable.CopyTo(bytes, 27);
        page.Body.CopyTo(bytes, headerLength);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(22), OggCrc.Compute(bytes));
        return bytes;
    }
}
