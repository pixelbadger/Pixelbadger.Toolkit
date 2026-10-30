using System.Runtime.InteropServices;
using Pixelbadger.Toolkit.Opus.Native;

namespace Pixelbadger.Toolkit.Opus;

/// <summary>
/// Wraps opus_repacketizer. libopus keeps pointers to every added packet, so each packet is copied into
/// unmanaged memory that stays allocated until <see cref="Reset"/> or <see cref="Dispose"/>.
/// </summary>
public sealed class OpusRepacketizer : IDisposable
{
    private readonly OpusRepacketizerHandle _handle;
    private readonly List<IntPtr> _buffers = [];
    private long _totalBytes;

    public OpusRepacketizer()
    {
        OpusLibrary.EnsureAvailable();
        var ptr = OpusNative.opus_repacketizer_create();
        if (ptr == IntPtr.Zero)
            throw new OpusException(-7, "opus_repacketizer_create failed: " + OpusNative.ErrorText(-7));
        _handle = new OpusRepacketizerHandle();
        _handle.Set(ptr);
    }

    public int FrameCount
    {
        get
        {
            using var h = Handle();
            return OpusNative.opus_repacketizer_get_nb_frames(h);
        }
    }

    public void Reset()
    {
        using var h = Handle();
        OpusNative.opus_repacketizer_init(h);
        FreeBuffers();
    }

    public void Add(ReadOnlySpan<byte> packet)
    {
        using var h = Handle();
        if (packet.IsEmpty)
            throw OpusNative.Error(OpusNative.OpusBadArg, "opus_repacketizer_cat");

        var native = Marshal.AllocHGlobal(packet.Length);
        Marshal.Copy(packet.ToArray(), 0, native, packet.Length);
        var result = OpusNative.opus_repacketizer_cat(h, native, packet.Length);
        if (result < 0)
        {
            Marshal.FreeHGlobal(native);
            throw OpusNative.Error(result, "opus_repacketizer_cat");
        }

        _buffers.Add(native);
        _totalBytes += packet.Length;
    }

    public byte[] Output()
    {
        using var h = Handle();
        var buffer = new byte[MaxOutput()];
        var length = OpusNative.Check(OpusNative.opus_repacketizer_out(h, ref buffer[0], buffer.Length), "opus_repacketizer_out");
        return buffer.AsSpan(0, length).ToArray();
    }

    public byte[] Output(int beginFrame, int endFrame)
    {
        using var h = Handle();
        var buffer = new byte[MaxOutput()];
        var length = OpusNative.Check(
            OpusNative.opus_repacketizer_out_range(h, beginFrame, endFrame, ref buffer[0], buffer.Length),
            "opus_repacketizer_out_range");
        return buffer.AsSpan(0, length).ToArray();
    }

    public void Dispose()
    {
        _handle.Dispose();
        FreeBuffers();
    }

    private int MaxOutput() => (int)Math.Min(int.MaxValue / 2, _totalBytes + 8L * Math.Max(1, FrameCount) + 16);

    private NativeLease Handle() => new(_handle, this);

    private void FreeBuffers()
    {
        foreach (var ptr in _buffers)
            Marshal.FreeHGlobal(ptr);
        _buffers.Clear();
        _totalBytes = 0;
    }
}
