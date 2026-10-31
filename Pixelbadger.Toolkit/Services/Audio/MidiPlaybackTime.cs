using System.Globalization;

namespace Pixelbadger.Toolkit.Services.Audio;

/// <summary>Formats playback times as m:ss (h:mm:ss from one hour), truncating to whole seconds.</summary>
public static class MidiPlaybackTime
{
    public static string Format(TimeSpan t)
    {
        var total = t <= TimeSpan.Zero ? 0L : (long)Math.Floor(t.TotalSeconds);
        var hours = total / 3600;
        var minutes = total % 3600 / 60;
        var seconds = total % 60;
        return hours > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{hours}:{minutes:00}:{seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes}:{seconds:00}");
    }
}
