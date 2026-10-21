using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pixelbadger.Toolkit.Benchmarks.Results;

public static class ResultsJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
        };
        options.Converters.Add(new UtcDateTimeConverter());
        return options;
    }

    /// <summary>Returns the run with its results sorted by Class, Method, Parameters (ordinal).</summary>
    public static RunRecord Normalize(RunRecord run) =>
        run with
        {
            Results = run.Results
                .OrderBy(r => r.Class, StringComparer.Ordinal)
                .ThenBy(r => r.Method, StringComparer.Ordinal)
                .ThenBy(r => r.Parameters, StringComparer.Ordinal)
                .ToList(),
        };

    public static string Serialize(RunRecord run) => JsonSerializer.Serialize(Normalize(run), Options);

    public static string Serialize(DeviceRecord device) => JsonSerializer.Serialize(device, Options);

    private sealed class UtcDateTimeConverter : JsonConverter<DateTime>
    {
        private const string Format = "yyyy-MM-dd'T'HH:mm:ss'Z'";

        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var text = reader.GetString();
            if (text is null || !text.EndsWith('Z') ||
                !DateTime.TryParseExact(text, Format, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value))
                throw new JsonException($"'{text}' is not a UTC timestamp of the form yyyy-MM-ddTHH:mm:ssZ.");
            return DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToUniversalTime().ToString(Format, CultureInfo.InvariantCulture));
    }
}
