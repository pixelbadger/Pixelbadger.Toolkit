using Pixelbadger.Toolkit.Opus.Native;

namespace Pixelbadger.Toolkit.Opus;

public interface IOpusEncoder : IDisposable
{
    int SampleRate { get; }
    int Channels { get; }
    int Encode(ReadOnlySpan<short> pcm, int frameSize, Span<byte> output);
    int EncodeFloat(ReadOnlySpan<float> pcm, int frameSize, Span<byte> output);
    int Bitrate { get; set; }
    int Complexity { get; set; }
    bool Vbr { get; set; }
    bool VbrConstraint { get; set; }
    OpusSignal Signal { get; set; }
    OpusBandwidth Bandwidth { get; set; }
    OpusBandwidth MaxBandwidth { get; set; }
    bool Dtx { get; set; }
    bool InbandFec { get; set; }
    int PacketLossPercentage { get; set; }
    int LsbDepth { get; set; }
    OpusApplication Application { get; set; }
    int Lookahead { get; }
    uint FinalRange { get; }
    void ResetState();
}

public sealed class OpusEncoder : IOpusEncoder
{
    private readonly OpusEncoderHandle _handle;

    public OpusEncoder(int sampleRate, int channels, OpusApplication application)
    {
        OpusLibrary.EnsureAvailable();
        var ptr = OpusNative.opus_encoder_create(sampleRate, channels, (int)application, out var error);
        if (error < 0 || ptr == IntPtr.Zero)
            throw OpusNative.Error(error < 0 ? error : OpusNative.OpusBadArg, "opus_encoder_create");
        _handle = new OpusEncoderHandle();
        _handle.Set(ptr);
        Channels = channels;
    }

    public int Channels { get; }

    public int SampleRate => GetInt(OpusNative.GetSampleRate, "OPUS_GET_SAMPLE_RATE");

    public int Encode(ReadOnlySpan<short> pcm, int frameSize, Span<byte> output)
    {
        var h = Handle();
        if (frameSize <= 0 || pcm.Length < (long)frameSize * Channels)
            throw OpusNative.Error(OpusNative.OpusBadArg, "opus_encode");
        return OpusNative.Check(
            OpusNative.opus_encode(h, ref OpusInterop.Ref(pcm), frameSize, ref OpusInterop.Ref(output), output.Length),
            "opus_encode");
    }

    public int EncodeFloat(ReadOnlySpan<float> pcm, int frameSize, Span<byte> output)
    {
        var h = Handle();
        if (frameSize <= 0 || pcm.Length < (long)frameSize * Channels)
            throw OpusNative.Error(OpusNative.OpusBadArg, "opus_encode_float");
        return OpusNative.Check(
            OpusNative.opus_encode_float(h, ref OpusInterop.Ref(pcm), frameSize, ref OpusInterop.Ref(output), output.Length),
            "opus_encode_float");
    }

    public int Bitrate
    {
        get => GetInt(OpusNative.GetBitrate, "OPUS_GET_BITRATE");
        set => SetInt(OpusNative.SetBitrate, value, "OPUS_SET_BITRATE");
    }

    public int Complexity
    {
        get => GetInt(OpusNative.GetComplexity, "OPUS_GET_COMPLEXITY");
        set => SetInt(OpusNative.SetComplexity, value, "OPUS_SET_COMPLEXITY");
    }

    public bool Vbr
    {
        get => GetInt(OpusNative.GetVbr, "OPUS_GET_VBR") != 0;
        set => SetInt(OpusNative.SetVbr, OpusInterop.Bool(value), "OPUS_SET_VBR");
    }

    public bool VbrConstraint
    {
        get => GetInt(OpusNative.GetVbrConstraint, "OPUS_GET_VBR_CONSTRAINT") != 0;
        set => SetInt(OpusNative.SetVbrConstraint, OpusInterop.Bool(value), "OPUS_SET_VBR_CONSTRAINT");
    }

    public OpusSignal Signal
    {
        get => (OpusSignal)GetInt(OpusNative.GetSignal, "OPUS_GET_SIGNAL");
        set => SetInt(OpusNative.SetSignal, (int)value, "OPUS_SET_SIGNAL");
    }

    public OpusBandwidth Bandwidth
    {
        get => (OpusBandwidth)GetInt(OpusNative.GetBandwidth, "OPUS_GET_BANDWIDTH");
        set => SetInt(OpusNative.SetBandwidth, (int)value, "OPUS_SET_BANDWIDTH");
    }

    public OpusBandwidth MaxBandwidth
    {
        get => (OpusBandwidth)GetInt(OpusNative.GetMaxBandwidth, "OPUS_GET_MAX_BANDWIDTH");
        set => SetInt(OpusNative.SetMaxBandwidth, (int)value, "OPUS_SET_MAX_BANDWIDTH");
    }

    public bool Dtx
    {
        get => GetInt(OpusNative.GetDtx, "OPUS_GET_DTX") != 0;
        set => SetInt(OpusNative.SetDtx, OpusInterop.Bool(value), "OPUS_SET_DTX");
    }

    public bool InbandFec
    {
        get => GetInt(OpusNative.GetInbandFec, "OPUS_GET_INBAND_FEC") != 0;
        set => SetInt(OpusNative.SetInbandFec, OpusInterop.Bool(value), "OPUS_SET_INBAND_FEC");
    }

    public int PacketLossPercentage
    {
        get => GetInt(OpusNative.GetPacketLossPerc, "OPUS_GET_PACKET_LOSS_PERC");
        set => SetInt(OpusNative.SetPacketLossPerc, value, "OPUS_SET_PACKET_LOSS_PERC");
    }

    public int LsbDepth
    {
        get => GetInt(OpusNative.GetLsbDepth, "OPUS_GET_LSB_DEPTH");
        set => SetInt(OpusNative.SetLsbDepth, value, "OPUS_SET_LSB_DEPTH");
    }

    public OpusApplication Application
    {
        get => (OpusApplication)GetInt(OpusNative.GetApplication, "OPUS_GET_APPLICATION");
        set => SetInt(OpusNative.SetApplication, (int)value, "OPUS_SET_APPLICATION");
    }

    public int Lookahead => GetInt(OpusNative.GetLookahead, "OPUS_GET_LOOKAHEAD");

    public uint FinalRange
    {
        get
        {
            var h = Handle();
            OpusNative.Check(OpusNative.EncoderCtlGetUInt(h, OpusNative.GetFinalRange, out var value), "OPUS_GET_FINAL_RANGE");
            return value;
        }
    }

    public void ResetState()
        => OpusNative.Check(OpusNative.opus_encoder_ctl_void(Handle(), OpusNative.ResetStateRequest), "OPUS_RESET_STATE");

    public void Dispose() => _handle.Dispose();

    private IntPtr Handle()
    {
        ObjectDisposedException.ThrowIf(_handle.IsClosed, this);
        return _handle.DangerousGetHandle();
    }

    private int GetInt(int request, string name)
    {
        OpusNative.Check(OpusNative.EncoderCtlGet(Handle(), request, out var value), name);
        return value;
    }

    private void SetInt(int request, int value, string name)
        => OpusNative.Check(OpusNative.EncoderCtlSet(Handle(), request, value), name);
}
