using Pixelbadger.Toolkit.Opus.Native;

namespace Pixelbadger.Toolkit.Opus;

public interface IOpusDecoder : IDisposable
{
    int SampleRate { get; }
    int Channels { get; }
    int Decode(ReadOnlySpan<byte> packet, Span<short> pcm, int frameSize, bool decodeFec = false);
    int DecodeFloat(ReadOnlySpan<byte> packet, Span<float> pcm, int frameSize, bool decodeFec = false);
    int GetSampleCount(ReadOnlySpan<byte> packet);
    int Gain { get; set; }
    int LastPacketDuration { get; }
    uint FinalRange { get; }
    OpusBandwidth Bandwidth { get; }
    void ResetState();
}

public sealed class OpusDecoder : IOpusDecoder
{
    private readonly OpusDecoderHandle _handle;

    public OpusDecoder(int sampleRate, int channels)
    {
        OpusLibrary.EnsureAvailable();
        var ptr = OpusNative.opus_decoder_create(sampleRate, channels, out var error);
        if (error < 0 || ptr == IntPtr.Zero)
            throw OpusNative.Error(error < 0 ? error : OpusNative.OpusBadArg, "opus_decoder_create");
        _handle = new OpusDecoderHandle();
        _handle.Set(ptr);
        Channels = channels;
    }

    public int Channels { get; }

    public int SampleRate => GetInt(OpusNative.GetSampleRate, "OPUS_GET_SAMPLE_RATE");

    public int Decode(ReadOnlySpan<byte> packet, Span<short> pcm, int frameSize, bool decodeFec = false)
    {
        var h = Handle();
        if (frameSize <= 0 || pcm.Length < (long)frameSize * Channels)
            throw OpusNative.Error(OpusNative.OpusBadArg, "opus_decode");
        return OpusNative.Check(
            OpusNative.opus_decode(h, ref OpusInterop.Ref(packet), packet.Length, ref OpusInterop.Ref(pcm), frameSize, OpusInterop.Bool(decodeFec)),
            "opus_decode");
    }

    public int DecodeFloat(ReadOnlySpan<byte> packet, Span<float> pcm, int frameSize, bool decodeFec = false)
    {
        var h = Handle();
        if (frameSize <= 0 || pcm.Length < (long)frameSize * Channels)
            throw OpusNative.Error(OpusNative.OpusBadArg, "opus_decode_float");
        return OpusNative.Check(
            OpusNative.opus_decode_float(h, ref OpusInterop.Ref(packet), packet.Length, ref OpusInterop.Ref(pcm), frameSize, OpusInterop.Bool(decodeFec)),
            "opus_decode_float");
    }

    public int GetSampleCount(ReadOnlySpan<byte> packet)
    {
        var h = Handle();
        if (packet.IsEmpty)
            throw OpusNative.Error(OpusNative.OpusBadArg, "opus_decoder_get_nb_samples");
        return OpusNative.Check(
            OpusNative.opus_decoder_get_nb_samples(h, ref OpusInterop.Ref(packet), packet.Length),
            "opus_decoder_get_nb_samples");
    }

    public int Gain
    {
        get => GetInt(OpusNative.GetGain, "OPUS_GET_GAIN");
        set => OpusNative.Check(OpusNative.opus_decoder_ctl_set(Handle(), OpusNative.SetGain, value), "OPUS_SET_GAIN");
    }

    public int LastPacketDuration => GetInt(OpusNative.GetLastPacketDuration, "OPUS_GET_LAST_PACKET_DURATION");

    public uint FinalRange
    {
        get
        {
            var h = Handle();
            OpusNative.Check(OpusNative.opus_decoder_ctl_get_uint(h, OpusNative.GetFinalRange, out var value), "OPUS_GET_FINAL_RANGE");
            return value;
        }
    }

    public OpusBandwidth Bandwidth => (OpusBandwidth)GetInt(OpusNative.GetBandwidth, "OPUS_GET_BANDWIDTH");

    public void ResetState()
        => OpusNative.Check(OpusNative.opus_decoder_ctl_void(Handle(), OpusNative.ResetStateRequest), "OPUS_RESET_STATE");

    public void Dispose() => _handle.Dispose();

    private IntPtr Handle()
    {
        ObjectDisposedException.ThrowIf(_handle.IsClosed, this);
        return _handle.DangerousGetHandle();
    }

    private int GetInt(int request, string name)
    {
        OpusNative.Check(OpusNative.opus_decoder_ctl_get(Handle(), request, out var value), name);
        return value;
    }
}
