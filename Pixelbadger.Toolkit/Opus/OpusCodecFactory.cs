namespace Pixelbadger.Toolkit.Opus;

public interface IOpusCodecFactory
{
    IOpusEncoder CreateEncoder(int sampleRate, int channels, OpusApplication application);
    IOpusDecoder CreateDecoder(int sampleRate, int channels);
}

public sealed class OpusCodecFactory : IOpusCodecFactory
{
    public IOpusEncoder CreateEncoder(int sampleRate, int channels, OpusApplication application)
        => new OpusEncoder(sampleRate, channels, application);

    public IOpusDecoder CreateDecoder(int sampleRate, int channels)
        => new OpusDecoder(sampleRate, channels);
}
