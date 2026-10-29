using System.Globalization;

namespace Pixelbadger.Toolkit.Services.Audio;

public static class AudioTimeParser
{
    public static TimeSpan Parse(string text)
    {
        var result = TryParse(text);
        return result ?? throw new FormatException(
            $"Invalid time '{text}'. Use [[hh:]mm:]ss[.fff], seconds, or a number with an ms, s or m suffix.");
    }

    private static TimeSpan? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var s = text.Trim();
        try
        {
            decimal seconds;
            if (s.Contains(':'))
            {
                var parts = s.Split(':');
                if (parts.Length is < 2 or > 3)
                    return null;

                decimal total = 0;
                for (var i = 0; i < parts.Length; i++)
                {
                    var isLast = i == parts.Length - 1;
                    var value = isLast ? ParseDecimal(parts[i]) : ParseWhole(parts[i]);
                    if (value is null)
                        return null;
                    total = total * 60 + value.Value;
                }

                seconds = total;
            }
            else if (s.EndsWith("ms", StringComparison.OrdinalIgnoreCase))
            {
                var v = ParseDecimal(s[..^2]);
                if (v is null) return null;
                seconds = v.Value / 1000m;
            }
            else if (s.EndsWith('s') || s.EndsWith('S'))
            {
                var v = ParseDecimal(s[..^1]);
                if (v is null) return null;
                seconds = v.Value;
            }
            else if (s.EndsWith('m') || s.EndsWith('M'))
            {
                var v = ParseDecimal(s[..^1]);
                if (v is null) return null;
                seconds = v.Value * 60m;
            }
            else
            {
                var v = ParseDecimal(s);
                if (v is null) return null;
                seconds = v.Value;
            }

            return TimeSpan.FromTicks(decimal.ToInt64(decimal.Round(seconds * TimeSpan.TicksPerSecond)));
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    private static decimal? ParseWhole(string part) =>
        part.Length > 0 && part.All(char.IsAsciiDigit) &&
        decimal.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var v)
            ? v
            : null;

    private static decimal? ParseDecimal(string part)
    {
        part = part.Trim();
        if (part.Length == 0 || !part.All(c => char.IsAsciiDigit(c) || c == '.'))
            return null;
        return decimal.TryParse(part, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v)
            ? v
            : null;
    }
}
