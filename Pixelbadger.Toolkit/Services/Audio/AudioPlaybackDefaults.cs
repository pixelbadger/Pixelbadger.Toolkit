namespace Pixelbadger.Toolkit.Services.Audio;

public sealed record AudioPlaybackOptions(string OutputFormat, string Device);

public static class AudioPlaybackDefaults
{
    public static (string Format, string Device) ForCurrentPlatform()
    {
        if (OperatingSystem.IsWindows())
            return ("sdl2", "default");
        if (OperatingSystem.IsMacOS())
            return ("audiotoolbox", "0");
        return ("pulse", "default");
    }
}
