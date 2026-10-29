using System.Buffers.Binary;

namespace Pixelbadger.Toolkit.Opus.Ogg;

public sealed record OpusHead(byte Version, byte ChannelCount, ushort PreSkip, uint InputSampleRate, short OutputGain, byte MappingFamily)
{
    private const int HeadLength = 19;

    public static OpusHead Parse(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < HeadLength || !packet[..8].SequenceEqual("OpusHead"u8))
            throw new InvalidDataException("Not an Ogg Opus file");
        if (packet[8] >> 4 != 0)
            throw new InvalidDataException($"Unsupported OpusHead version {packet[8]}");
        return new OpusHead(
            packet[8],
            packet[9],
            BinaryPrimitives.ReadUInt16LittleEndian(packet[10..]),
            BinaryPrimitives.ReadUInt32LittleEndian(packet[12..]),
            BinaryPrimitives.ReadInt16LittleEndian(packet[16..]),
            packet[18]);
    }

    /// <summary>Serialises the 19-byte identification header. Only mapping family 0 is supported.</summary>
    public byte[] ToBytes()
    {
        if (MappingFamily != 0)
            throw new NotSupportedException("Only channel mapping family 0 can be serialised");
        var bytes = new byte[HeadLength];
        "OpusHead"u8.CopyTo(bytes);
        bytes[8] = Version;
        bytes[9] = ChannelCount;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(10), PreSkip);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), InputSampleRate);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(16), OutputGain);
        bytes[18] = MappingFamily;
        return bytes;
    }
}
