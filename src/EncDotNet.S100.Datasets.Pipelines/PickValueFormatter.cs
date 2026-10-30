using System.Globalization;
using EncDotNet.S100.Features;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Formats a pick-report value as S-98 15.1 asks: numbers without padding
/// and followed by their unit of measure, and dates as <c>DD-MMM-YYYY</c>
/// with English month abbreviations. The value type and unit come from the
/// feature catalogue's simple attribute (S-100 Part 5).
/// </summary>
public static class PickValueFormatter
{
    private static readonly string[] Months =
        ["JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC"];

    /// <summary>
    /// The display form of <paramref name="value"/> for an attribute of
    /// <paramref name="attribute"/>'s value type, or <c>null</c> when the
    /// value reads as encoded (text, listed values, values that don't parse).
    /// </summary>
    /// <param name="attribute">The feature catalogue's simple attribute.</param>
    /// <param name="value">The encoded value.</param>
    /// <returns>The display value, or <c>null</c>.</returns>
    public static string? Format(SimpleAttribute attribute, string value)
    {
        ArgumentNullException.ThrowIfNull(attribute);
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var number = attribute.ValueType switch
        {
            "real" => FormatReal(value),
            "integer" => FormatInteger(value),
            _ => null,
        };
        if (number is not null)
            return WithUnit(number, attribute.Uom?.Symbol);

        return attribute.ValueType switch
        {
            "S100_TruncatedDate" or "date" => FormatDate(value),
            _ => null,
        };
    }

    /// <summary>
    /// An S-100 date (<c>CCYYMMDD</c>, or <c>CCYY-MM-DD</c>) as
    /// <c>DD-MMM-YYYY</c> (<c>16-AUG-2012</c>). A truncated date
    /// (S-100 <c>S100_TruncatedDate</c>, dashes for the parts left out) keeps
    /// the parts it has: <c>----03--</c> reads <c>MAR</c>, <c>1996----</c>
    /// reads <c>1996</c>, <c>----0316</c> reads
    /// <c>16-MAR</c>. Returns <c>null</c> when <paramref name="value"/> isn't a date.
    /// </summary>
    /// <param name="value">The encoded date.</param>
    /// <returns>The display date, or <c>null</c>.</returns>
    public static string? FormatDate(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var text = value.Trim();
        if (text.Length == 10 && text[4] == '-' && text[7] == '-')
            text = string.Concat(text.AsSpan(0, 4), text.AsSpan(5, 2), text.AsSpan(8, 2));
        if (text.Length != 8)
            return null;

        var year = Part(text, 0, 4);
        var month = Part(text, 4, 2);
        var day = Part(text, 6, 2);
        if (year is -1 || month is -1 || day is -1)
            return null;
        if (month is not (null or >= 1 and <= 12) || day is not (null or >= 1 and <= 31))
            return null;
        if (year is null && month is null && day is null)
            return null;

        var parts = new List<string>(3);
        if (day is { } d)
            parts.Add(d.ToString("00", CultureInfo.InvariantCulture));
        if (month is { } m)
            parts.Add(Months[m - 1]);
        if (year is { } y)
            parts.Add(y.ToString("0000", CultureInfo.InvariantCulture));
        return string.Join('-', parts);
    }

    // The digits at [start, start+length): their value, null when all dashes, -1 otherwise.
    private static int? Part(string text, int start, int length)
    {
        var span = text.AsSpan(start, length);
        if (span.Trim('-').IsEmpty)
            return null;
        return int.TryParse(span, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : -1;
    }

    private static string? FormatReal(string value) =>
        double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d)
            ? d.ToString("0.##########", CultureInfo.InvariantCulture)
            : null;

    private static string? FormatInteger(string value) =>
        long.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? n.ToString(CultureInfo.InvariantCulture)
            : null;

    // Angles read 10°, 5'; everything else takes a space: 5 s, 12.5 m.
    private static string WithUnit(string number, string? unit) =>
        string.IsNullOrWhiteSpace(unit) ? number
        : unit is "°" or "'" or "′" or "\"" or "″" ? number + unit
        : $"{number} {unit}";
}
