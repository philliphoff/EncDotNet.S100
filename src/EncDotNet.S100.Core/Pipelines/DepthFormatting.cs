using System;
using System.Globalization;

namespace EncDotNet.S100.Pipelines;

/// <summary>
/// Locale-invariant conversion, formatting and parsing for depth values
/// expressed in <see cref="DepthUnit"/>. Canonical storage is always metres;
/// these helpers exist purely for user-facing presentation in the viewer
/// (settings inputs, Pick panel, S-102 legend, status bar).
/// </summary>
/// <remarks>
/// Conversion factors used:
/// <list type="bullet">
///   <item><description>1 m = 3.28084 ft</description></item>
///   <item><description>1 fathom = 6 ft</description></item>
///   <item><description>1 m = 0.546806649168854 fm (= 3.28084 / 6)</description></item>
/// </list>
/// </remarks>
public static class DepthFormatting
{
    /// <summary>Feet per metre (international foot).</summary>
    public const double FeetPerMetre = 3.28084;

    /// <summary>Feet per fathom.</summary>
    public const double FeetPerFathom = 6.0;

    /// <summary>Fathoms per metre.</summary>
    public const double FathomsPerMetre = FeetPerMetre / FeetPerFathom;

    /// <summary>
    /// Converts a depth value from canonical metres to the supplied display unit.
    /// </summary>
    public static double ToDisplay(double metres, DepthUnit unit) => unit switch
    {
        DepthUnit.Metres => metres,
        DepthUnit.Feet => metres * FeetPerMetre,
        DepthUnit.FathomsFeet => metres * FeetPerMetre, // exposed as total feet; Format splits into fm/ft
        DepthUnit.Fathoms => metres * FathomsPerMetre,
        _ => metres,
    };

    /// <summary>
    /// Converts a display value back to canonical metres.
    /// </summary>
    /// <remarks>
    /// For <see cref="DepthUnit.FathomsFeet"/> the input is interpreted as a
    /// total in feet (the same convention as <see cref="ToDisplay"/>).
    /// </remarks>
    public static double ToMetres(double display, DepthUnit unit) => unit switch
    {
        DepthUnit.Metres => display,
        DepthUnit.Feet => display / FeetPerMetre,
        DepthUnit.FathomsFeet => display / FeetPerMetre,
        DepthUnit.Fathoms => display / FathomsPerMetre,
        _ => display,
    };

    /// <summary>
    /// Returns the short abbreviation used for <paramref name="unit"/> in
    /// formatted strings ("m", "ft", "fm", "fm/ft").
    /// </summary>
    public static string UnitAbbreviation(DepthUnit unit) => unit switch
    {
        DepthUnit.Metres => "m",
        DepthUnit.Feet => "ft",
        DepthUnit.FathomsFeet => "fm/ft",
        DepthUnit.Fathoms => "fm",
        _ => "m",
    };

    /// <summary>
    /// Formats <paramref name="metres"/> in <paramref name="unit"/> using
    /// invariant culture. <see cref="DepthUnit.FathomsFeet"/> renders as
    /// "&lt;fathoms&gt;fm &lt;feet&gt;ft" (e.g. <c>"5fm 2ft"</c>).
    /// </summary>
    /// <param name="metres">Depth in metres.</param>
    /// <param name="unit">Display unit.</param>
    /// <param name="decimals">
    /// Number of decimal places. Defaults: 1 for metres/fathoms/feet, 0 for
    /// fathoms-and-feet (always rendered as whole feet remainder).
    /// </param>
    public static string Format(double metres, DepthUnit unit, int? decimals = null)
    {
        var inv = CultureInfo.InvariantCulture;

        if (unit == DepthUnit.FathomsFeet)
        {
            // Convert to total feet, then split into fathoms + feet remainder.
            // Sign-aware so values like -3.5m round-trip sensibly.
            double totalFeet = metres * FeetPerMetre;
            int sign = totalFeet < 0 ? -1 : 1;
            double absFeet = Math.Abs(totalFeet);
            int wholeFeet = (int)Math.Round(absFeet, MidpointRounding.AwayFromZero);
            int fathoms = wholeFeet / 6;
            int feetRem = wholeFeet % 6;
            string prefix = sign < 0 ? "-" : "";
            return $"{prefix}{fathoms.ToString(inv)}fm {feetRem.ToString(inv)}ft";
        }

        int d = decimals ?? 1;
        double display = ToDisplay(metres, unit);
        string num = display.ToString("F" + d.ToString(inv), inv);
        return $"{num} {UnitAbbreviation(unit)}";
    }

    /// <summary>
    /// Parses <paramref name="text"/> in <paramref name="unit"/> back to metres
    /// using invariant culture. Returns <c>true</c> on success.
    /// </summary>
    /// <remarks>
    /// Accepted forms:
    /// <list type="bullet">
    ///   <item><description>Bare number ("30", "30.5") — interpreted in <paramref name="unit"/>.</description></item>
    ///   <item><description>Number with matching unit suffix ("30 m", "100ft", "5 fm").</description></item>
    ///   <item><description>For <see cref="DepthUnit.FathomsFeet"/>: "5fm 2ft", "5 fm", "12 ft", or a bare number (treated as feet).</description></item>
    /// </list>
    /// </remarks>
    public static bool TryParse(string text, DepthUnit unit, out double metres)
    {
        metres = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var inv = CultureInfo.InvariantCulture;
        var trimmed = text.Trim();

        if (unit == DepthUnit.FathomsFeet)
        {
            return TryParseFathomsFeet(trimmed, inv, out metres);
        }

        // Strip a trailing unit token if the user typed one. We accept any
        // recognised unit and convert appropriately (so "100 ft" parses
        // correctly even when the active unit is metres — useful for paste).
        var (value, parsedUnit) = SplitValueAndUnit(trimmed, unit);
        if (!double.TryParse(value, NumberStyles.Float, inv, out var num))
            return false;

        metres = ToMetres(num, parsedUnit);
        return true;
    }

    private static bool TryParseFathomsFeet(string text, IFormatProvider inv, out double metres)
    {
        metres = 0;
        // Allow "5fm 2ft", "5 fm 2 ft", "5fm", "12ft", or bare number (= feet).
        bool sawFm = false, sawFt = false;
        double fathoms = 0, feet = 0;
        int sign = 1;
        var s = text;
        if (s.StartsWith('-')) { sign = -1; s = s[1..].TrimStart(); }

        // Find "fm" and "ft" occurrences (case-insensitive).
        int fmIdx = s.IndexOf("fm", StringComparison.OrdinalIgnoreCase);
        int ftIdx = s.IndexOf("ft", StringComparison.OrdinalIgnoreCase);

        if (fmIdx >= 0)
        {
            var head = s[..fmIdx].Trim();
            if (!double.TryParse(head, NumberStyles.Float, inv, out fathoms))
                return false;
            sawFm = true;
            // Whatever comes after "fm" may be the feet portion.
            var tail = s[(fmIdx + 2)..].Trim();
            if (tail.Length > 0)
            {
                int tFt = tail.IndexOf("ft", StringComparison.OrdinalIgnoreCase);
                var ftStr = tFt >= 0 ? tail[..tFt].Trim() : tail;
                if (!double.TryParse(ftStr, NumberStyles.Float, inv, out feet))
                    return false;
                sawFt = true;
            }
        }
        else if (ftIdx >= 0)
        {
            var head = s[..ftIdx].Trim();
            if (!double.TryParse(head, NumberStyles.Float, inv, out feet))
                return false;
            sawFt = true;
        }
        else
        {
            // Bare number — treat as total feet (matches Format/ToDisplay convention).
            if (!double.TryParse(s, NumberStyles.Float, inv, out feet))
                return false;
            sawFt = true;
        }

        if (!sawFm && !sawFt) return false;

        double totalFeet = fathoms * FeetPerFathom + feet;
        metres = sign * totalFeet / FeetPerMetre;
        return true;
    }

    private static (string Value, DepthUnit Unit) SplitValueAndUnit(string text, DepthUnit fallback)
    {
        // Try to peel off a recognised trailing unit token.
        ReadOnlySpan<(string Suffix, DepthUnit Unit)> suffixes =
        [
            ("metres", DepthUnit.Metres),
            ("meter",  DepthUnit.Metres),
            ("meters", DepthUnit.Metres),
            ("metre",  DepthUnit.Metres),
            ("feet",   DepthUnit.Feet),
            ("ft",     DepthUnit.Feet),
            ("fm",     DepthUnit.Fathoms),
            ("m",      DepthUnit.Metres),
        ];

        foreach (var (suffix, unit) in suffixes)
        {
            if (text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                var head = text[..^suffix.Length].TrimEnd();
                return (head, unit);
            }
        }

        return (text, fallback);
    }
}
