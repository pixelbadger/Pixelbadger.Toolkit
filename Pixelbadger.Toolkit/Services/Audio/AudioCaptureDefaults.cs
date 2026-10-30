namespace Pixelbadger.Toolkit.Services.Audio;

public static class AudioCaptureDefaults
{
    public static (string Format, string? Device) ForCurrentPlatform()
    {
        if (OperatingSystem.IsWindows())
            return ("dshow", null);
        if (OperatingSystem.IsMacOS())
            return ("avfoundation", ":0");
        return ("pulse", "default");
    }
}
