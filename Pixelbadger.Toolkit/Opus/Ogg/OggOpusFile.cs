using System.Buffers.Binary;
using System.Text;

namespace Pixelbadger.Toolkit.Opus.Ogg;

public sealed record OggOpusFile(byte[] HeadPacket, byte[] TagsPacket, IReadOnlyList<byte[]> AudioPackets, int EndTrim)
{
    private const string VendorString = "Pixelbadger.Toolkit";
    private const int PageSampleLimit = 48000;

    public OpusHead Head => OpusHead.Parse(HeadPacket);

    public long TotalPacketSamples
    {
        get
        {
            long total = 0;
            foreach (var p in AudioPackets) total += OpusToc.GetSampleCount(p);
            return total;
        }
    }

    public long DurationSamples => Math.Max(0, TotalPacketSamples - Head.PreSkip - EndTrim);

    public static OggOpusFile Create(OpusHead head, IReadOnlyList<byte[]> audioPackets, int endTrim)
    {
        ArgumentNullException.ThrowIfNull(head);
        ArgumentNullException.ThrowIfNull(audioPackets);
        var vendor = Encoding.UTF8.GetBytes(VendorString);
        var tags = new byte[8 + 4 + vendor.Length + 4];
        "OpusTags"u8.CopyTo(tags);
        BinaryPrimitives.WriteUInt32LittleEndian(tags.AsSpan(8), (uint)vendor.Length);
        vendor.CopyTo(tags, 12);
        // trailing 4 bytes: user comment count = 0
        return new OggOpusFile(head.ToBytes(), tags, audioPackets, endTrim);
    }

    public static OggOpusFile Read(Stream s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var reader = new OggPageReader(s);
        var packets = new List<byte[]>();
        var pending = new MemoryStream();
        var hasPending = false;
        uint serial = 0;
        var first = true;
        var sawEos = false;
        var lastGranule = -1L;

        while (true)
        {
            OggPage? page;
            try
            {
                page = reader.ReadPage();
            }
            catch (InvalidDataException ex) when (first && ex.Message == "Invalid Ogg page at byte offset 0")
            {
                throw new InvalidDataException("Not an Ogg Opus file");
            }

            if (page is null) break;

            if (first)
            {
                if ((page.Flags & OggPageFlags.BeginOfStream) == 0)
                    throw new InvalidDataException("Not an Ogg Opus file");
                serial = page.SerialNumber;
                first = false;
            }
            else if ((page.Flags & OggPageFlags.BeginOfStream) != 0 || page.SerialNumber != serial || sawEos)
            {
                throw new InvalidDataException("Chained or multiplexed Ogg streams are not supported");
            }

            if ((page.Flags & OggPageFlags.EndOfStream) != 0) sawEos = true;

            var continued = (page.Flags & OggPageFlags.Continued) != 0;
            if (continued != hasPending)
                throw new InvalidDataException($"Corrupt Ogg stream: unexpected packet continuation in page {page.SequenceNumber}");

            var pos = 0;
            foreach (var seg in page.SegmentTable)
            {
                pending.Write(page.Body, pos, seg);
                pos += seg;
                hasPending = true;
                if (seg < 255)
                {
                    packets.Add(pending.ToArray());
                    pending.SetLength(0);
                    hasPending = false;
                    ValidateEarlyPacket(packets);
                }
            }

            if (page.GranulePosition >= 0) lastGranule = page.GranulePosition;
        }

        if (first) throw new InvalidDataException("Not an Ogg Opus file");
        if (hasPending) throw new InvalidDataException("Truncated Ogg stream: the final packet is incomplete");
        if (packets.Count < 2) throw new InvalidDataException("Ogg Opus file is missing its OpusTags header");

        var audio = packets.GetRange(2, packets.Count - 2);
        long total = 0;
        foreach (var p in audio) total += OpusToc.GetSampleCount(p);
        var endTrim = 0;
        if (lastGranule >= 0 && total - lastGranule > 0)
            endTrim = (int)Math.Min(total - lastGranule, int.MaxValue);

        return new OggOpusFile(packets[0], packets[1], audio, endTrim);
    }

    private static void ValidateEarlyPacket(List<byte[]> packets)
    {
        if (packets.Count == 1)
        {
            var head = OpusHead.Parse(packets[0]); // throws "Not an Ogg Opus file"
            if (head.MappingFamily != 0)
                throw new InvalidDataException(
                    $"Unsupported Ogg Opus channel mapping family {head.MappingFamily}; only mono and stereo files are supported");
        }
        else if (packets.Count == 2)
        {
            if (!packets[1].AsSpan().StartsWith("OpusTags"u8))
                throw new InvalidDataException("Ogg Opus file is missing its OpusTags header");
        }
    }

    public void Write(Stream s, uint serialNumber)
    {
        ArgumentNullException.ThrowIfNull(s);
        var builder = new PageBuilder(new OggPageWriter(s), serialNumber);

        builder.AddPacket(HeadPacket, 0);
        builder.Flush(0, false);
        builder.AddPacket(TagsPacket, 0);

        if (AudioPackets.Count == 0)
        {
            builder.Flush(0, true);
            return;
        }

        builder.Flush(0, false);
        long total = 0;
        foreach (var p in AudioPackets)
        {
            var samples = OpusToc.GetSampleCount(p);
            if (builder.PageSamples >= PageSampleLimit) builder.Flush(null, false);
            builder.AddPacket(p, samples);
            total += samples;
        }
        builder.Flush(Math.Max(0, total - EndTrim), true);
    }

    private sealed class PageBuilder
    {
        private readonly OggPageWriter _writer;
        private readonly uint _serial;
        private readonly List<byte> _segments = new();
        private readonly MemoryStream _body = new();
        private uint _sequence;
        private long _cumulative;
        private bool _completedOnPage;
        private bool _nextContinued;
        private bool _bos = true;

        public int PageSamples { get; private set; }

        public PageBuilder(OggPageWriter writer, uint serial)
        {
            _writer = writer;
            _serial = serial;
        }

        public void AddPacket(byte[] packet, int samples)
        {
            var pos = 0;
            while (true)
            {
                if (_segments.Count == 255) Flush(null, false, midPacket: pos > 0);
                var len = Math.Min(255, packet.Length - pos);
                _segments.Add((byte)len);
                _body.Write(packet, pos, len);
                pos += len;
                if (len < 255) break; // a final segment of 255 bytes needs a trailing zero segment
            }

            _cumulative += samples;
            PageSamples += samples;
            _completedOnPage = true;
        }

        /// <param name="granuleOverride">Explicit granule, or null to derive it from completed packets.</param>
        public void Flush(long? granuleOverride, bool endOfStream, bool midPacket = false)
        {
            var flags = OggPageFlags.None;
            if (_nextContinued) flags |= OggPageFlags.Continued;
            if (_bos) flags |= OggPageFlags.BeginOfStream;
            if (endOfStream) flags |= OggPageFlags.EndOfStream;

            var granule = granuleOverride ?? (_completedOnPage ? _cumulative : -1);
            _writer.WritePage(new OggPage(flags, granule, _serial, _sequence++, _segments.ToArray(), _body.ToArray()));

            _segments.Clear();
            _body.SetLength(0);
            PageSamples = 0;
            _completedOnPage = false;
            _nextContinued = midPacket;
            _bos = false;
        }
    }
}
