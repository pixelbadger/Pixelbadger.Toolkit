namespace Pixelbadger.Toolkit.Opus;

public enum OpusApplication { Voip = 2048, Audio = 2049, RestrictedLowDelay = 2051 }

public enum OpusSignal { Auto = -1000, Voice = 3001, Music = 3002 }

public enum OpusBandwidth
{
    Auto = -1000,
    Narrowband = 1101,
    Mediumband = 1102,
    Wideband = 1103,
    SuperWideband = 1104,
    Fullband = 1105
}
