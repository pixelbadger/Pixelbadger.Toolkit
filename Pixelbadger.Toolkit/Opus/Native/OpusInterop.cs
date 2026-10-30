using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pixelbadger.Toolkit.Opus.Native;

internal static class OpusInterop
{
    /// <summary>Reference to the first byte, or a null reference for a default span.</summary>
    internal static ref byte Ref(ReadOnlySpan<byte> span) => ref Unsafe.AsRef(in MemoryMarshal.GetReference(span));

    internal static ref short Ref(ReadOnlySpan<short> span) => ref Unsafe.AsRef(in MemoryMarshal.GetReference(span));

    internal static ref float Ref(ReadOnlySpan<float> span) => ref Unsafe.AsRef(in MemoryMarshal.GetReference(span));

    internal static int Bool(bool value) => value ? 1 : 0;

}
