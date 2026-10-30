using System.Runtime.InteropServices;
using Pixelbadger.Toolkit.Opus.Native;

namespace Pixelbadger.Toolkit.Opus;

public sealed record OpusPacketInfo(byte Toc, int PayloadOffset, IReadOnlyList<byte[]> Frames);

public static class OpusPacket
{
    private const int MaxFrames = 48;

    public static int GetFrameCount(ReadOnlySpan<byte> packet)
    {
        OpusLibrary.EnsureAvailable();
        RequireNonEmpty(packet, "opus_packet_get_nb_frames");
        return OpusNative.Check(OpusNative.opus_packet_get_nb_frames(ref OpusInterop.Ref(packet), packet.Length), "opus_packet_get_nb_frames");
    }

    public static int GetSampleCount(ReadOnlySpan<byte> packet, int sampleRate)
    {
        OpusLibrary.EnsureAvailable();
        RequireNonEmpty(packet, "opus_packet_get_nb_samples");
        return OpusNative.Check(OpusNative.opus_packet_get_nb_samples(ref OpusInterop.Ref(packet), packet.Length, sampleRate), "opus_packet_get_nb_samples");
    }

    public static int GetSamplesPerFrame(ReadOnlySpan<byte> packet, int sampleRate)
    {
        OpusLibrary.EnsureAvailable();
        RequireNonEmpty(packet, "opus_packet_get_samples_per_frame");
        return OpusNative.opus_packet_get_samples_per_frame(ref OpusInterop.Ref(packet), sampleRate);
    }

    public static OpusBandwidth GetBandwidth(ReadOnlySpan<byte> packet)
    {
        OpusLibrary.EnsureAvailable();
        RequireNonEmpty(packet, "opus_packet_get_bandwidth");
        return (OpusBandwidth)OpusNative.Check(OpusNative.opus_packet_get_bandwidth(ref OpusInterop.Ref(packet)), "opus_packet_get_bandwidth");
    }

    public static int GetChannelCount(ReadOnlySpan<byte> packet)
    {
        OpusLibrary.EnsureAvailable();
        RequireNonEmpty(packet, "opus_packet_get_nb_channels");
        return OpusNative.Check(OpusNative.opus_packet_get_nb_channels(ref OpusInterop.Ref(packet)), "opus_packet_get_nb_channels");
    }

    public static OpusPacketInfo Parse(ReadOnlySpan<byte> packet)
    {
        OpusLibrary.EnsureAvailable();
        RequireNonEmpty(packet, "opus_packet_parse");

        var native = Marshal.AllocHGlobal(packet.Length);
        try
        {
            Marshal.Copy(packet.ToArray(), 0, native, packet.Length);
            var pointers = new IntPtr[MaxFrames];
            var sizes = new short[MaxFrames];
            var count = OpusNative.Check(
                OpusNative.opus_packet_parse(native, packet.Length, out var toc, pointers, sizes, out var payloadOffset),
                "opus_packet_parse");

            var frames = new List<byte[]>(count);
            for (var i = 0; i < count; i++)
            {
                var frame = new byte[sizes[i]];
                if (frame.Length > 0)
                    Marshal.Copy(pointers[i], frame, 0, frame.Length);
                frames.Add(frame);
            }

            return new OpusPacketInfo(toc, payloadOffset, frames);
        }
        finally
        {
            Marshal.FreeHGlobal(native);
        }
    }

    public static byte[] Pad(ReadOnlySpan<byte> packet, int newLength)
    {
        OpusLibrary.EnsureAvailable();
        RequireNonEmpty(packet, "opus_packet_pad");
        if (newLength < packet.Length)
            throw OpusNative.Error(OpusNative.OpusBadArg, "opus_packet_pad");

        var buffer = new byte[newLength];
        packet.CopyTo(buffer);
        OpusNative.Check(OpusNative.opus_packet_pad(ref buffer[0], packet.Length, newLength), "opus_packet_pad");
        return buffer;
    }

    public static byte[] Unpad(ReadOnlySpan<byte> packet)
    {
        OpusLibrary.EnsureAvailable();
        RequireNonEmpty(packet, "opus_packet_unpad");

        var buffer = packet.ToArray();
        var length = OpusNative.Check(OpusNative.opus_packet_unpad(ref buffer[0], buffer.Length), "opus_packet_unpad");
        return buffer.AsSpan(0, length).ToArray();
    }

    private static void RequireNonEmpty(ReadOnlySpan<byte> packet, string operation)
    {
        if (packet.IsEmpty)
            throw OpusNative.Error(OpusNative.OpusBadArg, operation);
    }
}
