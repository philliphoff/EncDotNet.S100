using System.Globalization;
using EncDotNet.S100.Gml;

namespace EncDotNet.S100.Mcp.Tools.Time;

/// <summary>
/// Inspects an <see cref="IGmlFeature"/> for S-100 validity metadata
/// — the <c>fixedDateRange</c> and <c>periodicDateRange</c> complex
/// attributes carried by specs such as S-122 (seasonal MPA
/// restrictions), S-124 (warning in-force periods), S-201 (AtoN
/// status reports), and S-411 (per-feature ice product valid time).
/// </summary>
/// <remarks>
/// <para>
/// The S-100 General Feature Model declares both ranges as complex
/// attributes with <c>dateStart</c> and <c>dateEnd</c> sub-attributes
/// in ISO-8601 form. Either bound may be absent, which means
/// "open-ended on that side".
/// </para>
/// <para>
/// Per the Phase 2 plan question 4 recommendation, a feature with
/// no validity metadata at all is treated as "always valid" — its
/// validity is <see cref="Validity.Unknown"/> and callers should
/// include it under any <see cref="TimeQuery"/>.
/// </para>
/// </remarks>
internal static class FeatureValidity
{
    /// <summary>Validity verdict for a feature against a <see cref="TimeQuery"/>.</summary>
    internal enum Verdict
    {
        /// <summary>The feature carries no validity metadata; include unconditionally.</summary>
        Unknown,

        /// <summary>The feature's validity range overlaps the query window.</summary>
        Overlaps,

        /// <summary>The feature's validity range is known and is disjoint from the query window.</summary>
        Disjoint,
    }

    /// <summary>
    /// Checks whether <paramref name="feature"/> overlaps
    /// <paramref name="query"/>. Returns <see cref="Verdict.Unknown"/>
    /// when no validity metadata is present.
    /// </summary>
    public static Verdict Check(IGmlFeature feature, TimeQuery query)
    {
        ArgumentNullException.ThrowIfNull(feature);
        ArgumentNullException.ThrowIfNull(query);

        var (windowStart, windowEnd) = query.GetWindow();

        var ranges = ExtractRanges(feature);
        if (ranges.Count == 0)
        {
            return Verdict.Unknown;
        }

        foreach (var (start, end) in ranges)
        {
            if (Overlaps(start, end, windowStart, windowEnd))
            {
                return Verdict.Overlaps;
            }
        }

        return Verdict.Disjoint;
    }

    private static bool Overlaps(
        DateTimeOffset? rangeStart,
        DateTimeOffset? rangeEnd,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd)
    {
        if (rangeStart.HasValue && rangeStart.Value > windowEnd)
        {
            return false;
        }

        if (rangeEnd.HasValue && rangeEnd.Value < windowStart)
        {
            return false;
        }

        return true;
    }

    private static List<(DateTimeOffset? Start, DateTimeOffset? End)> ExtractRanges(IGmlFeature feature)
    {
        var ranges = new List<(DateTimeOffset?, DateTimeOffset?)>();

        foreach (var complex in feature.GmlComplexAttributes)
        {
            if (!IsValidityRange(complex.Code))
            {
                continue;
            }

            var start = TryParseDate(complex.SubAttributes.GetValueOrDefault("dateStart"));
            var end = TryParseDate(complex.SubAttributes.GetValueOrDefault("dateEnd"));
            if (start is null && end is null)
            {
                continue;
            }

            ranges.Add((start, end));
        }

        return ranges;
    }

    private static bool IsValidityRange(string code)
    {
        return string.Equals(code, "fixedDateRange", StringComparison.OrdinalIgnoreCase)
            || string.Equals(code, "periodicDateRange", StringComparison.OrdinalIgnoreCase);
    }

    private static DateTimeOffset? TryParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed))
        {
            return parsed;
        }

        return null;
    }
}
