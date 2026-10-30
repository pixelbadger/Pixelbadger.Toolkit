using System.Buffers.Binary;

namespace Pixelbadger.Toolkit.Services.Audio;

public static class WavReader
{
    private const ushort FormatPcm = 1;
    private const ushort FormatFloat = 3;
    private const ushort FormatExtensible = 0xFFFE;

    public static PcmAudio Read(Stream s)
    {
        ArgumentNullException.ThrowIfNull(s);
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        var data = ms.GetBuffer().AsSpan(0, (int)ms.Length);

        if (data.Length < 12 || !data[..4].SequenceEqual("RIFF"u8) || !data.Slice(8, 4).SequenceEqual("WAVE"u8))
            throw new InvalidDataException("Not a WAV file");

        ushort tag = 0;
        int channels = 0, sampleRate = 0, bits = 0;
        var haveFmt = false;
        ReadOnlySpan<byte> pcmData = default;
        var haveData = false;

        var pos = 12;
        while (pos + 8 <= data.Length)
        {
            var id = data.Slice(pos, 4);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(pos + 4, 4));
            var bodyStart = pos + 8;
            var available = data.Length - bodyStart;
            var length = (int)Math.Min(size, (uint)available);

            if (id.SequenceEqual("fmt "u8))
            {
                if (length < 16)
                    throw Unsupported("truncated fmt chunk");
                var body = data.Slice(bodyStart, length);
                tag = BinaryPrimitives.ReadUInt16LittleEndian(body);
                channels = BinaryPrimitives.ReadUInt16LittleEndian(body[2..]);
                sampleRate = (int)BinaryPrimitives.ReadUInt32LittleEndian(body[4..]);
                bits = BinaryPrimitives.ReadUInt16LittleEndian(body[14..]);
                if (tag == FormatExtensible)
                {
                    if (length < 40)
                        throw Unsupported("truncated WAVE_FORMAT_EXTENSIBLE header");
                    tag = BinaryPrimitives.ReadUInt16LittleEndian(body[24..]);
                }

                haveFmt = true;
            }
            else if (id.SequenceEqual("data"u8))
            {
                pcmData = data.Slice(bodyStart, length);
                haveData = true;
                if (haveFmt)
                    break;
            }

            // Chunks are word aligned: odd sizes carry one pad byte.
            var next = (long)bodyStart + size + (size & 1);
            if (next > data.Length)
                break;
            pos = (int)next;
        }

        if (!haveFmt)
            throw Unsupported("missing fmt chunk");
        if (!haveData)
            throw Unsupported("missing data chunk");
        if (channels is not (1 or 2))
            throw Unsupported($"{channels} channels");
        if (sampleRate <= 0)
            throw Unsupported($"sample rate {sampleRate}");

        short[] samples;
        if (tag == FormatPcm && bits == 16)
        {
            var count = pcmData.Length / 2;
            samples = new short[count - count % channels];
            for (var i = 0; i < samples.Length; i++)
                samples[i] = BinaryPrimitives.ReadInt16LittleEndian(pcmData[(i * 2)..]);
        }
        else if (tag == FormatFloat && bits == 32)
        {
            var count = pcmData.Length / 4;
            samples = new short[count - count % channels];
            for (var i = 0; i < samples.Length; i++)
                samples[i] = FloatToShort(BinaryPrimitives.ReadSingleLittleEndian(pcmData[(i * 4)..]));
        }
        else
        {
            var kind = tag switch
            {
                FormatPcm => "PCM",
                FormatFloat => "float",
                _ => $"format tag {tag}"
            };
            throw Unsupported($"{bits}-bit {kind}");
        }

        return new PcmAudio(sampleRate, channels, samples);
    }

    private static short FloatToShort(float v)
    {
        if (float.IsNaN(v))
            return 0;
        var scaled = Math.Round(v * 32768.0, MidpointRounding.ToEven);
        return (short)Math.Clamp(scaled, short.MinValue, short.MaxValue);
    }

    private static InvalidDataException Unsupported(string details) =>
        new($"Unsupported WAV format: {details}. Use 16-bit PCM or 32-bit float, mono or stereo.");
}
