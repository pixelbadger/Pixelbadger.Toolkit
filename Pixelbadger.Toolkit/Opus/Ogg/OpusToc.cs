namespace Pixelbadger.Toolkit.Opus.Ogg;

/// <summary>Managed Opus packet duration calculation per RFC 6716 sections 3.1 and 3.2.</summary>
public static class OpusToc
{
    private const int MaxPacketSamples = 5760; // 120 ms at 48 kHz

    /// <summary>Returns the number of 48 kHz samples per channel in the packet.</summary>
    public static int GetSampleCount(ReadOnlySpan<byte> packet)
    {
        if (packet.Length == 0)
            throw new InvalidDataException("Invalid Opus packet: empty packet");

        var frameSamples = FrameSamples(packet[0] >> 3);
        int frames;
        switch (packet[0] & 3)
        {
            case 0:
                frames = 1;
                break;
            case 1:
            case 2:
                frames = 2;
                break;
            default:
                if (packet.Length < 2)
                    throw new InvalidDataException("Invalid Opus packet: missing frame count byte");
                frames = packet[1] & 0x3F;
                if (frames == 0)
                    throw new InvalidDataException("Invalid Opus packet: frame count is zero");
                break;
        }

        var total = frames * frameSamples;
        if (total > MaxPacketSamples)
            throw new InvalidDataException("Invalid Opus packet: duration exceeds 120 ms");
        return total;
    }

    private static int FrameSamples(int config)
    {
        if (config < 12)
            return (config & 3) switch { 0 => 480, 1 => 960, 2 => 1920, _ => 2880 }; // SILK 10/20/40/60 ms
        if (config < 16)
            return (config & 1) == 0 ? 480 : 960; // Hybrid 10/20 ms
        return 120 << (config & 3); // CELT 2.5/5/10/20 ms
    }
}
