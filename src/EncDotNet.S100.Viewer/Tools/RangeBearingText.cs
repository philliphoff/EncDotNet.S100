using System.Globalization;
using System.Text;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Viewer.Geodesy;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.Tools;

/// <summary>Status-bar and clipboard text for the Range &amp; bearing tool.</summary>
internal static class RangeBearingText
{
    /// <summary>
    /// The status bar's summary: <c>17.90 NM · 295.7°T</c> once there's a
    /// range and bearing, or the hint before the origin is placed.
    /// </summary>
    public static string? Summary(RangeBearingState state, DistanceUnit unit)
    {
        if (state.Origin is null)
            return Strings.RangeBearing_StatusHint;
        if (state.RangeMetres is not { } range || state.BearingDegrees is not { } bearing)
            return null;
        return string.Format(CultureInfo.CurrentCulture, Strings.RangeBearing_Status,
            MeasureFormat.Distance(range, unit), MeasureFormat.Bearing(bearing));
    }

    /// <summary>
    /// The earth model the tool uses, for the card's footer:
    /// <c>WGS-84 ellipsoid</c> (D17).
    /// </summary>
    public static string Model() => Strings.RangeBearing_Model;

    /// <summary>
    /// The result as text for the clipboard (D8, D20): origin, geometry, range
    /// in both unit systems, bearing, end, reverse bearing, rings and the earth
    /// model. <c>null</c> before there's an origin.
    /// </summary>
    public static string? Copy(RangeBearingState state, DistanceUnit unit)
    {
        if (state.Origin is not { } origin)
            return null;

        var text = new StringBuilder();
        void Line(string label, string value) => text.Append(label).Append(": ").AppendLine(value);

        Line(Strings.RangeBearing_Origin, LatLonFormatter.Format(origin.Latitude, origin.Longitude));
        Line(Strings.RangeBearing_GeometryLabel, Geometry(state.Geometry));
        if (state.RangeMetres is { } range)
            Line(Strings.RangeBearing_Range, $"{MeasureFormat.Distance(range, unit)} ({MeasureFormat.OtherDistance(range, unit)})");
        if (state.BearingDegrees is { } bearing)
            Line(Strings.RangeBearing_Bearing, MeasureFormat.Bearing(bearing) + "T");
        if (state.End is { } end)
            Line(Strings.RangeBearing_End, LatLonFormatter.Format(end.Latitude, end.Longitude));
        if (state.ReverseBearingDegrees is { } reverse)
            Line(Strings.RangeBearing_Reverse, MeasureFormat.Bearing(reverse) + "T");
        if (state.RingsEnabled)
        {
            Line(Strings.RangeBearing_Rings, string.Format(CultureInfo.CurrentCulture, Strings.RangeBearing_RingsCopy,
                MeasureFormat.Distance(state.RingIntervalMetres, unit), state.RingCount));
        }

        Line(Strings.RangeBearing_ModelLabel, Model());
        return text.ToString();
    }

    /// <summary>The geometry's name: Rhumb or Great circle.</summary>
    public static string Geometry(LegGeometry geometry) =>
        geometry == LegGeometry.GreatCircle ? Strings.RangeBearing_GreatCircle : Strings.RangeBearing_Rhumb;
}
