using System.Runtime.InteropServices;

namespace Pixelbadger.Toolkit.Opus.Native;

/// <summary>Raw P/Invoke declarations. Resolved by the resolver registered in <see cref="OpusLibrary"/>.</summary>
internal static partial class OpusNative
{
    internal const string LibraryName = "opus";

    internal const int OpusOk = 0;
    internal const int OpusBadArg = -1;

    // Encoder / decoder ctl request codes (opus_defines.h)
    internal const int SetApplication = 4000, GetApplication = 4001;
    internal const int SetBitrate = 4002, GetBitrate = 4003;
    internal const int SetMaxBandwidth = 4004, GetMaxBandwidth = 4005;
    internal const int SetVbr = 4006, GetVbr = 4007;
    internal const int SetBandwidth = 4008, GetBandwidth = 4009;
    internal const int SetComplexity = 4010, GetComplexity = 4011;
    internal const int SetInbandFec = 4012, GetInbandFec = 4013;
    internal const int SetPacketLossPerc = 4014, GetPacketLossPerc = 4015;
    internal const int SetDtx = 4016, GetDtx = 4017;
    internal const int SetVbrConstraint = 4020, GetVbrConstraint = 4021;
    internal const int SetSignal = 4024, GetSignal = 4025;
    internal const int GetLookahead = 4027;
    internal const int ResetStateRequest = 4028;
    internal const int GetSampleRate = 4029;
    internal const int GetFinalRange = 4031;
    internal const int SetGain = 4034;
    internal const int SetLsbDepth = 4036, GetLsbDepth = 4037;
    internal const int GetLastPacketDuration = 4039;
    internal const int GetGain = 4045;

    [LibraryImport(LibraryName)]
    internal static partial IntPtr opus_get_version_string();

    [LibraryImport(LibraryName)]
    internal static partial IntPtr opus_strerror(int error);

    // Encoder
    [LibraryImport(LibraryName)]
    internal static partial IntPtr opus_encoder_create(int fs, int channels, int application, out int error);

    [LibraryImport(LibraryName)]
    internal static partial void opus_encoder_destroy(IntPtr encoder);

    [LibraryImport(LibraryName)]
    internal static partial int opus_encode(IntPtr encoder, ref short pcm, int frameSize, ref byte data, int maxDataBytes);

    [LibraryImport(LibraryName)]
    internal static partial int opus_encode_float(IntPtr encoder, ref float pcm, int frameSize, ref byte data, int maxDataBytes);

    [LibraryImport(LibraryName, EntryPoint = "opus_encoder_ctl")]
    internal static partial int opus_encoder_ctl_set(IntPtr encoder, int request, int value);

    [LibraryImport(LibraryName, EntryPoint = "opus_encoder_ctl")]
    internal static partial int opus_encoder_ctl_get(IntPtr encoder, int request, out int value);

    [LibraryImport(LibraryName, EntryPoint = "opus_encoder_ctl")]
    internal static partial int opus_encoder_ctl_get_uint(IntPtr encoder, int request, out uint value);

    [LibraryImport(LibraryName, EntryPoint = "opus_encoder_ctl")]
    internal static partial int opus_encoder_ctl_void(IntPtr encoder, int request);

    // Decoder
    [LibraryImport(LibraryName)]
    internal static partial IntPtr opus_decoder_create(int fs, int channels, out int error);

    [LibraryImport(LibraryName)]
    internal static partial void opus_decoder_destroy(IntPtr decoder);

    [LibraryImport(LibraryName)]
    internal static partial int opus_decode(IntPtr decoder, ref byte data, int len, ref short pcm, int frameSize, int decodeFec);

    [LibraryImport(LibraryName)]
    internal static partial int opus_decode_float(IntPtr decoder, ref byte data, int len, ref float pcm, int frameSize, int decodeFec);

    [LibraryImport(LibraryName, EntryPoint = "opus_decoder_ctl")]
    internal static partial int opus_decoder_ctl_set(IntPtr decoder, int request, int value);

    [LibraryImport(LibraryName, EntryPoint = "opus_decoder_ctl")]
    internal static partial int opus_decoder_ctl_get(IntPtr decoder, int request, out int value);

    [LibraryImport(LibraryName, EntryPoint = "opus_decoder_ctl")]
    internal static partial int opus_decoder_ctl_get_uint(IntPtr decoder, int request, out uint value);

    [LibraryImport(LibraryName, EntryPoint = "opus_decoder_ctl")]
    internal static partial int opus_decoder_ctl_void(IntPtr decoder, int request);

    [LibraryImport(LibraryName)]
    internal static partial int opus_decoder_get_nb_samples(IntPtr decoder, ref byte packet, int len);

    // Packet inspection
    [LibraryImport(LibraryName)]
    internal static partial int opus_packet_get_nb_frames(ref byte packet, int len);

    [LibraryImport(LibraryName)]
    internal static partial int opus_packet_get_nb_samples(ref byte packet, int len, int fs);

    [LibraryImport(LibraryName)]
    internal static partial int opus_packet_get_samples_per_frame(ref byte packet, int fs);

    [LibraryImport(LibraryName)]
    internal static partial int opus_packet_get_bandwidth(ref byte packet);

    [LibraryImport(LibraryName)]
    internal static partial int opus_packet_get_nb_channels(ref byte packet);

    [LibraryImport(LibraryName)]
    internal static partial int opus_packet_parse(IntPtr data, int len, out byte outToc, [Out] IntPtr[] frames, [Out] short[] sizes, out int payloadOffset);

    [LibraryImport(LibraryName)]
    internal static partial int opus_packet_pad(ref byte data, int len, int newLen);

    [LibraryImport(LibraryName)]
    internal static partial int opus_packet_unpad(ref byte data, int len);

    // Repacketizer
    [LibraryImport(LibraryName)]
    internal static partial IntPtr opus_repacketizer_create();

    [LibraryImport(LibraryName)]
    internal static partial void opus_repacketizer_destroy(IntPtr rp);

    [LibraryImport(LibraryName)]
    internal static partial IntPtr opus_repacketizer_init(IntPtr rp);

    [LibraryImport(LibraryName)]
    internal static partial int opus_repacketizer_cat(IntPtr rp, IntPtr data, int len);

    [LibraryImport(LibraryName)]
    internal static partial int opus_repacketizer_out(IntPtr rp, ref byte data, int maxLen);

    [LibraryImport(LibraryName)]
    internal static partial int opus_repacketizer_out_range(IntPtr rp, int begin, int end, ref byte data, int maxLen);

    [LibraryImport(LibraryName)]
    internal static partial int opus_repacketizer_get_nb_frames(IntPtr rp);

    internal static string ErrorText(int code)
        => Marshal.PtrToStringUTF8(opus_strerror(code)) ?? $"error {code}";

    /// <summary>Throws <see cref="OpusException"/> when <paramref name="code"/> is negative; otherwise returns it.</summary>
    internal static int Check(int code, string operation)
    {
        if (code < 0)
            throw Error(code, operation);
        return code;
    }

    internal static OpusException Error(int code, string operation)
        => new(code, $"{operation} failed: {ErrorText(code)}");
}

internal sealed class OpusEncoderHandle : SafeHandle
{
    public OpusEncoderHandle() : base(IntPtr.Zero, true) { }
    public override bool IsInvalid => handle == IntPtr.Zero;

    internal void Set(IntPtr value) => SetHandle(value);

    protected override bool ReleaseHandle()
    {
        OpusNative.opus_encoder_destroy(handle);
        return true;
    }
}

internal sealed class OpusDecoderHandle : SafeHandle
{
    public OpusDecoderHandle() : base(IntPtr.Zero, true) { }
    public override bool IsInvalid => handle == IntPtr.Zero;

    internal void Set(IntPtr value) => SetHandle(value);

    protected override bool ReleaseHandle()
    {
        OpusNative.opus_decoder_destroy(handle);
        return true;
    }
}

internal sealed class OpusRepacketizerHandle : SafeHandle
{
    public OpusRepacketizerHandle() : base(IntPtr.Zero, true) { }
    public override bool IsInvalid => handle == IntPtr.Zero;

    internal void Set(IntPtr value) => SetHandle(value);

    protected override bool ReleaseHandle()
    {
        OpusNative.opus_repacketizer_destroy(handle);
        return true;
    }
}
