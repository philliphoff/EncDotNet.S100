using System.Text.Json;

namespace EncDotNet.S100.Mcp.Tools.Time;

/// <summary>
/// Parses the wire-format JSON envelope for <see cref="TimeQuery"/>.
/// </summary>
/// <remarks>
/// <para>
/// The envelope is shared by every tool that accepts a temporal
/// parameter. Three shapes are accepted:
/// </para>
/// <code>
/// { "kind": "instant", "t": "2024-01-01T14:00:00Z" }
/// { "kind": "range",   "from": "2024-01-01T00:00:00Z", "to": "2024-01-01T06:00:00Z" }
/// { "kind": "series",  "from": "...", "to": "...", "stepSeconds": 1800 }
/// </code>
/// <para>
/// All timestamps must be ISO-8601 with an explicit offset or trailing
/// <c>Z</c>; values without offsets are rejected. <c>stepSeconds</c>
/// must be a positive number.
/// </para>
/// </remarks>
public static class TimeQueryJsonReader
{
    /// <summary>
    /// Parses the JSON envelope. Throws <see cref="ArgumentException"/>
    /// on malformed input.
    /// </summary>
    public static TimeQuery Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var doc = JsonDocument.Parse(json);
        return Parse(doc.RootElement);
    }

    /// <summary>Parses the JSON envelope from an already-loaded <see cref="JsonElement"/>.</summary>
    public static TimeQuery Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("TimeQuery JSON must be an object.", nameof(root));
        }

        if (!root.TryGetProperty("kind", out var kindElement) || kindElement.ValueKind != JsonValueKind.String)
        {
            throw new ArgumentException("TimeQuery JSON must include a string 'kind' property.", nameof(root));
        }

        var kind = kindElement.GetString();
        return kind switch
        {
            "instant" => ParseInstant(root),
            "range" => ParseRange(root),
            "series" => ParseSeries(root),
            _ => throw new ArgumentException(
                $"Unknown TimeQuery kind '{kind}'. Expected one of 'instant', 'range', 'series'.",
                nameof(root)),
        };
    }

    private static TimeQuery ParseInstant(JsonElement root)
    {
        if (!root.TryGetProperty("t", out var t) || t.ValueKind != JsonValueKind.String)
        {
            throw new ArgumentException("instant TimeQuery requires a string 't' property.", nameof(root));
        }
        return TimeQuery.At(ReadOffsetDateTime(t, "t"));
    }

    private static TimeQuery ParseRange(JsonElement root)
    {
        var from = ReadRequiredOffsetDateTime(root, "from");
        var to = ReadRequiredOffsetDateTime(root, "to");
        try
        {
            return TimeQuery.Between(from, to);
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException($"range TimeQuery is invalid: {ex.Message}", nameof(root), ex);
        }
    }

    private static TimeQuery ParseSeries(JsonElement root)
    {
        var from = ReadRequiredOffsetDateTime(root, "from");
        var to = ReadRequiredOffsetDateTime(root, "to");
        if (!root.TryGetProperty("stepSeconds", out var stepElement)
            || stepElement.ValueKind != JsonValueKind.Number
            || !stepElement.TryGetDouble(out var stepSeconds)
            || stepSeconds <= 0
            || double.IsNaN(stepSeconds)
            || double.IsInfinity(stepSeconds))
        {
            throw new ArgumentException("series TimeQuery requires a positive numeric 'stepSeconds'.", nameof(root));
        }
        try
        {
            return TimeQuery.Every(from, to, TimeSpan.FromSeconds(stepSeconds));
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException($"series TimeQuery is invalid: {ex.Message}", nameof(root), ex);
        }
    }

    private static DateTimeOffset ReadRequiredOffsetDateTime(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String)
        {
            throw new ArgumentException($"TimeQuery requires a string '{name}' property.", nameof(root));
        }
        return ReadOffsetDateTime(element, name);
    }

    // Require explicit offset / Z. JsonElement.GetDateTimeOffset() will
    // happily accept naive timestamps and assume local; we reject those
    // because cross-zone agent prompts would silently shift.
    private static DateTimeOffset ReadOffsetDateTime(JsonElement element, string name)
    {
        var raw = element.GetString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new ArgumentException($"TimeQuery '{name}' must be a non-empty ISO-8601 timestamp.", nameof(element));
        }
        if (!DateTimeOffset.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var dto))
        {
            throw new ArgumentException($"TimeQuery '{name}' is not a valid ISO-8601 timestamp: '{raw}'.", nameof(element));
        }
        if (!raw.EndsWith('Z') && !ContainsOffset(raw))
        {
            throw new ArgumentException(
                $"TimeQuery '{name}' must include an explicit UTC offset (trailing 'Z' or '+hh:mm'/'-hh:mm'). Got '{raw}'.",
                nameof(element));
        }
        return dto;
    }

    // Heuristic: look for an offset suffix like "+05:00" or "-08:00"
    // after the time portion (positions ≥ 11 in YYYY-MM-DDThh:mm:ss…).
    // The date itself contains '-' separators but only in positions
    // 4 and 7, so anything later is guaranteed to be an offset sign.
    private static bool ContainsOffset(string raw)
    {
        for (int i = raw.Length - 1; i >= 11; i--)
        {
            var c = raw[i];
            if (c == '+' || c == '-')
            {
                if (char.IsDigit(raw[i - 1])) return true;
            }
        }
        return false;
    }
}
