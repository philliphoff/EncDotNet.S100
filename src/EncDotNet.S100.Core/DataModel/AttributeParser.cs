using System.Globalization;

namespace EncDotNet.S100.DataModel;

/// <summary>
/// Permissive primitive parsers used by strongly-typed projections. Each
/// helper returns the parsed value or <c>null</c>, and on parse failure
/// records a <see cref="DiagnosticSeverity.Warning"/> on the supplied
/// <see cref="ProjectionContext"/> with a stable diagnostic code.
/// </summary>
/// <remarks>
/// <para>
/// Empty or <c>null</c> input is treated as an absent value and produces no
/// diagnostic — only a non-empty value that fails to parse is reported.
/// </para>
/// <para>
/// Numeric parsing uses <see cref="CultureInfo.InvariantCulture"/>. Date/time
/// parsing follows S-100 Part 5 §10 (Simple Types) — ISO 8601 round-trip
/// representation, normalised to UTC.
/// </para>
/// </remarks>
public static class AttributeParser
{
    /// <summary>Attempts to parse an <see cref="int"/>; reports <c>"attribute.parse.int"</c> on failure.</summary>
    public static int? TryParseInt(string? value, ProjectionContext context,
        string? relatedId = null, string? attributeName = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrEmpty(value)) return null;
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var r))
            return r;

        context.Warn(
            $"Could not parse value '{value}' as an integer.",
            code: "attribute.parse.int",
            relatedId: relatedId,
            relatedAttribute: attributeName);
        return null;
    }

    /// <summary>Attempts to parse a <see cref="double"/>; reports <c>"attribute.parse.double"</c> on failure.</summary>
    public static double? TryParseDouble(string? value, ProjectionContext context,
        string? relatedId = null, string? attributeName = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrEmpty(value)) return null;
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var r))
            return r;

        context.Warn(
            $"Could not parse value '{value}' as a double.",
            code: "attribute.parse.double",
            relatedId: relatedId,
            relatedAttribute: attributeName);
        return null;
    }

    /// <summary>
    /// Attempts to parse a <see cref="bool"/>; accepts <c>"1"</c>/<c>"0"</c> and
    /// the case-insensitive forms <c>"true"</c>/<c>"false"</c>. Reports
    /// <c>"attribute.parse.bool"</c> on failure.
    /// </summary>
    public static bool? TryParseBool(string? value, ProjectionContext context,
        string? relatedId = null, string? attributeName = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrEmpty(value)) return null;
        switch (value)
        {
            case "1": return true;
            case "0": return false;
        }
        if (bool.TryParse(value, out var r)) return r;

        context.Warn(
            $"Could not parse value '{value}' as a boolean.",
            code: "attribute.parse.bool",
            relatedId: relatedId,
            relatedAttribute: attributeName);
        return null;
    }

    /// <summary>
    /// Attempts to parse a <see cref="DateTimeOffset"/> using ISO 8601
    /// round-trip semantics (S-100 Part 5 §10). Values without a timezone are
    /// assumed UTC; the result is normalised to UTC. Reports
    /// <c>"attribute.parse.datetime"</c> on failure.
    /// </summary>
    public static DateTimeOffset? TryParseDateTimeOffset(string? value, ProjectionContext context,
        string? relatedId = null, string? attributeName = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrEmpty(value)) return null;
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var r))
            return r;

        context.Warn(
            $"Could not parse value '{value}' as a date/time.",
            code: "attribute.parse.datetime",
            relatedId: relatedId,
            relatedAttribute: attributeName);
        return null;
    }
}
