namespace Pixelbadger.Toolkit.Opus.Ogg;

/// <summary>Ogg page checksum: CRC-32, polynomial 0x04C11DB7, initial value 0, no reflection, no final xor.</summary>
public static class OggCrc
{
    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var r = i << 24;
            for (var k = 0; k < 8; k++)
                r = (r & 0x80000000) != 0 ? (r << 1) ^ 0x04C11DB7 : r << 1;
            table[i] = r;
        }
        return table;
    }

    public static uint Compute(ReadOnlySpan<byte> data) => Update(0, data);

    internal static uint Update(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
            crc = (crc << 8) ^ Table[(byte)(crc >> 24) ^ b];
        return crc;
    }
}
