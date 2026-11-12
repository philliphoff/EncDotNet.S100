using System.Globalization;
using System.Text.RegularExpressions;

namespace EncDotNet.S100.Datasets.Aml;

/// <summary>
/// The text of AML labels: a feature's name, and an airspace's vertical
/// limits, drawn as a stacked fraction, upper over lower ("FL245 / SFC",
/// AML design handoff decision 18).
/// </summary>
public static partial class AmlLabels
{
    /// <summary>The longest <c>identy</c> used as a name: longer values are prose, such as "Marked by buoys".</summary>
    public const int MaximumIdentifierLength = 12;

    /// <summary>The lower limit at the surface: <c>SFC</c>.</summary>
    public const string Surface = "SFC";

    /// <summary>A limit the feature doesn't give: an en dash.</summary>
    public const string Unknown = "–";

    /// <summary>
    /// A feature's name: its <c>OBJNAM</c>, or else its <c>identy</c> when that
    /// is short enough to be a designator; <see langword="null"/> for neither.
    /// </summary>
    /// <param name="feature">The feature.</param>
    public static string? Name(AmlFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        if (feature.Attribute("OBJNAM") is { } name && name != ".")
            return name;
        return feature.Attribute("identy") is { Length: <= MaximumIdentifierLength } identifier ? identifier : null;
    }

    /// <summary>
    /// An airspace's vertical limits, upper and lower, or <see langword="null"/>
    /// when it gives neither. Each limit is the first the feature has of:
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description>the AML 3.0 coded limit, <c>limitu</c> / <c>limitl</c> (<c>FL245</c>, <c>07800</c>);</description></item>
    /// <item><description>the AML 3.0 effective altitude, <c>efaltu</c> / <c>efaltl</c> (<c>003500AMSL</c>, <c>FL75</c>);</description></item>
    /// <item><description>the upper limit's flight level, <c>maxftl</c>, then its altitude, <c>maxalt</c>;</description></item>
    /// <item><description>the lower limit's altitude, <c>minalt</c>, then its flight level, <c>minftl</c>.</description></item>
    /// </list>
    /// <para>
    /// Coded limits are written <c>FL245</c>, <c>7800 ft</c>, <c>1500 ft AMSL</c>
    /// or <c>1000 ft AGL</c>: a bare number is in feet, as aeronautical limits
    /// are. Altitudes are in the feature's <c>HUNITS</c> (1 metres, 2 feet; feet
    /// when unset). A lower limit of 0 is <see cref="Surface"/>, and a flight
    /// level above 999 is ignored as malformed.
    /// </para>
    /// </remarks>
    /// <param name="feature">The feature.</param>
    public static (string Upper, string Lower)? Limits(AmlFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        string unit = feature.Attribute("HUNITS") == "1" ? "m" : "ft";
        var upper = Coded(feature.Attribute("limitu"))
            ?? Coded(feature.Attribute("efaltu"))
            ?? FlightLevel(feature.Attribute("maxftl"))
            ?? Altitude(feature.Attribute("maxalt"), unit, lower: false);
        var lower = Coded(feature.Attribute("limitl"))
            ?? Coded(feature.Attribute("efaltl"))
            ?? Altitude(feature.Attribute("minalt"), unit, lower: true)
            ?? FlightLevel(feature.Attribute("minftl"));
        if (upper is null && lower is null)
            return null;
        return (upper ?? Unknown, lower ?? Unknown);
    }

    /// <summary>The limits as one line, upper over lower: <c>FL245 / SFC</c>.</summary>
    /// <param name="limits">The limits.</param>
    public static string Fraction((string Upper, string Lower) limits) => $"{limits.Upper} / {limits.Lower}";

    private static string? FlightLevel(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var level) && level is >= 0 and <= 999
            ? string.Create(CultureInfo.InvariantCulture, $"FL{level}")
            : null;

    private static string? Altitude(string? value, string unit, bool lower)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var altitude) || altitude < 0)
            return null;
        if (lower && altitude == 0)
            return Surface;
        return string.Create(CultureInfo.InvariantCulture, $"{altitude:0} {unit}");
    }

    private static string? Coded(string? value)
    {
        if (value is null)
            return null;
        var text = value.Trim().ToUpperInvariant();
        if (text is "SFC" or "GND" or "UNL")
            return text;
        var match = CodedLimit().Match(text);
        if (!match.Success)
            return text;
        if (match.Groups["fl"].Success)
            return FlightLevel(match.Groups["fl"].Value) ?? text;
        var feet = int.Parse(match.Groups["ft"].Value, CultureInfo.InvariantCulture);
        var datum = match.Groups["datum"].Success ? " " + match.Groups["datum"].Value : string.Empty;
        return feet == 0 && datum is "" or " AGL"
            ? Surface
            : string.Create(CultureInfo.InvariantCulture, $"{feet} ft{datum}");
    }

    [GeneratedRegex(@"^(?:FL(?<fl>\d{1,3})|(?<ft>\d{1,6})\s*(?<datum>AMSL|MSL|AGL|SFC)?)$", RegexOptions.CultureInvariant)]
    private static partial Regex CodedLimit();
}
