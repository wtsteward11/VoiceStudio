using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace VoiceStudio.Core.Panels;

/// <summary>
/// Reads optional key/value metadata from search results (e.g. marker project_id, time).
/// Values may deserialize as <see cref="JsonElement"/>.
/// </summary>
public static class SearchNavigationMetadata
{
    public static string? GetString(IReadOnlyDictionary<string, object>? metadata, string key)
    {
        if (metadata == null || !metadata.TryGetValue(key, out var raw) || raw is null)
            return null;

        if (raw is JsonElement je)
        {
            return je.ValueKind switch
            {
                JsonValueKind.String => je.GetString(),
                JsonValueKind.Number => je.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => je.ToString(),
            };
        }

        return raw.ToString();
    }

    public static double? GetDouble(IReadOnlyDictionary<string, object>? metadata, string key)
    {
        if (metadata == null || !metadata.TryGetValue(key, out var raw) || raw is null)
            return null;

        if (raw is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Number && je.TryGetDouble(out var d))
                return d;
            if (je.ValueKind == JsonValueKind.String &&
                double.TryParse(je.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                return parsed;
            return null;
        }

        if (raw is double dbl)
            return dbl;
        if (raw is float f)
            return f;
        if (raw is int i)
            return i;
        if (double.TryParse(raw.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var p))
            return p;
        return null;
    }
}
