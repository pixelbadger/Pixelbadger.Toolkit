using System.Buffers.Binary;

namespace Pixelbadger.Toolkit.Opus.Ogg;

public sealed class OggPageReader
{
    private readonly Stream _stream;
    private long _offset;

    public OggPageReader(Stream s)
    {
        _stream = s ?? throw new ArgumentNullException(nameof(s));
    }

    /// <summary>Reads the next page, or returns null at a clean end of stream.</summary>
    public OggPage? ReadPage()
    {
        var pageOffset = _offset;
        var header = new byte[27];
        var got = ReadFully(header, 0, 27);
        if (got == 0) return null;
        if (got < 27)
        {
            // Not even a full header: report garbage as invalid, a plausible page prefix as truncated.
            if (!"OggS"u8.StartsWith(header.AsSpan(0, Math.Min(got, 4))))
                throw new InvalidDataException($"Invalid Ogg page at byte offset {pageOffset}");
            throw new InvalidDataException($"Truncated Ogg page at byte offset {pageOffset}");
        }

        if (!header.AsSpan(0, 4).SequenceEqual("OggS"u8) || header[4] != 0)
            throw new InvalidDataException($"Invalid Ogg page at byte offset {pageOffset}");

        var segmentCount = header[26];
        var table = new byte[segmentCount];
        if (ReadFully(table, 0, segmentCount) < segmentCount)
            throw new InvalidDataException($"Truncated Ogg page at byte offset {pageOffset}");

        var bodyLength = 0;
        foreach (var s in table) bodyLength += s;
        var body = new byte[bodyLength];
        if (ReadFully(body, 0, bodyLength) < bodyLength)
            throw new InvalidDataException($"Truncated Ogg page at byte offset {pageOffset}");

        var storedCrc = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(22));
        header.AsSpan(22, 4).Clear();
        var crc = OggCrc.Update(0, header);
        crc = OggCrc.Update(crc, table);
        crc = OggCrc.Update(crc, body);
        if (crc != storedCrc)
            throw new InvalidDataException($"Ogg page CRC mismatch at byte offset {pageOffset}");

        return new OggPage(
            (OggPageFlags)header[5],
            BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(6)),
            BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(14)),
            BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(18)),
            table,
            body);
    }

    private int ReadFully(byte[] buffer, int start, int count)
    {
        var total = 0;
        while (total < count)
        {
            var n = _stream.Read(buffer, start + total, count - total);
            if (n <= 0) break;
            total += n;
        }
        _offset += total;
        return total;
    }
}
