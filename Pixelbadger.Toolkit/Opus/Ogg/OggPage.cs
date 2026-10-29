namespace Pixelbadger.Toolkit.Opus.Ogg;

[Flags]
public enum OggPageFlags : byte
{
    None = 0,
    Continued = 1,
    BeginOfStream = 2,
    EndOfStream = 4
}

/// <summary>A single Ogg page. The CRC is not stored; it is verified on read and computed on write.</summary>
public sealed record OggPage(
    OggPageFlags Flags,
    long GranulePosition,
    uint SerialNumber,
    uint SequenceNumber,
    byte[] SegmentTable,
    byte[] Body);
