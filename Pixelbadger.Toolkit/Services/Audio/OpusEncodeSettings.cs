namespace Pixelbadger.Toolkit.Services.Audio;

public sealed record OpusEncodeSettings(int Channels, int Bitrate = 64000, int Complexity = 10);
