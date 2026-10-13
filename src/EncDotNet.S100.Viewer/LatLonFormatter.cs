using System.Globalization;
using EncDotNet.S100.Collections.Library;

namespace EncDotNet.S100.Viewer;

/// <summary>
/// Mariner-friendly formatting for geographic coordinates. The format is
/// degrees-decimal-minutes (DDM), e.g. <c>"12°34.567'N  056°12.345'W"</c>,
/// matching the convention typically used on nautical charts.
/// </summary>
internal static class LatLonFormatter
{
    /// <summary>
    /// Placeholder used when no coordinate is available (e.g. the cursor is
    /// not over the map). Empty so the status bar simply shows nothing in
    /// that state.
    /// </summary>
    public const string Placeholder = "";

    /// <summary>
    /// Formats a (latitude, longitude) pair in degrees-decimal-minutes form.
    /// Degrees are zero-padded (latitude to two digits, longitude to three)
    /// so the readout keeps a stable width when rendered with tabular figures.
    /// </summary>
    public static string Format(double latitude, double longitude) =>
        LibraryTextFormat.LatLon(latitude, longitude);

    /// <summary>Formats just the latitude component, e.g. <c>"50°46.024'N"</c>.</summary>
    public static string FormatLatitude(double latitude) => FormatDegMin(latitude, 'N', 'S', 2);

    /// <summary>Formats just the longitude component, e.g. <c>"001°15.558'W"</c>.</summary>
    public static string FormatLongitude(double longitude) => FormatDegMin(longitude, 'E', 'W', 3);

    /// <summary>
    /// Formats a (latitude, longitude) pair as signed decimal degrees,
    /// e.g. <c>"47.601234, -122.334567"</c>. This machine-friendly form is
    /// what the pick panel places on the clipboard so the coordinate can be
    /// pasted straight into tools and scripts during debugging.
    /// </summary>
    public static string FormatDecimal(double latitude, double longitude) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{latitude:0.000000}, {longitude:0.000000}");

    private static string FormatDegMin(double value, char positive, char negative, int degWidth) =>
        LibraryTextFormat.DegreesMinutes(value, positive, negative, degWidth);

    /// <summary>
    /// Parses a typed or pasted position. Accepts what <see cref="Format"/>
    /// writes (<c>50°46.024'N  001°15.558'W</c>) and the common variants: degrees
    /// and minutes or degrees, minutes and seconds with or without the °, ' and "
    /// marks; hemisphere letters before or after each part; a decimal point or
    /// comma; and signed decimal degrees (<c>50.767, -1.259</c>, the form
    /// <see cref="FormatDecimal"/> writes).
    /// </summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="latitude">The latitude in decimal degrees, when parsed.</param>
    /// <param name="longitude">The longitude in decimal degrees, when parsed.</param>
    /// <returns><c>true</c> when <paramref name="text"/> holds exactly one valid
    /// latitude and one valid longitude.</returns>
    /// <remarks>
    /// Without hemisphere letters the first part is the latitude. With them,
    /// N/S marks the latitude and E/W the longitude, in either order.
    /// </remarks>
    public static bool TryParse(string? text, out double latitude, out double longitude)
    {
        latitude = 0;
        longitude = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var parts = SplitParts(text);
        if (parts is not { Count: 2 })
            return false;

        var (first, second) = (parts[0], parts[1]);
        var firstIsLon = first.Hemisphere is 'E' or 'W';
        var secondIsLat = second.Hemisphere is 'N' or 'S';
        var swap = firstIsLon || secondIsLat;
        var (lat, lon) = swap ? (second, first) : (first, second);
        if (lat.Hemisphere is 'E' or 'W' || lon.Hemisphere is 'N' or 'S')
            return false;

        if (lat.ToDegrees('S') is not { } latValue || Math.Abs(latValue) > 90.0)
            return false;
        if (lon.ToDegrees('W') is not { } lonValue || Math.Abs(lonValue) > 180.0)
            return false;

        latitude = latValue;
        longitude = lonValue;
        return true;
    }

    /// <summary>One latitude or longitude as typed: up to three numbers and a hemisphere.</summary>
    private sealed class CoordinatePart
    {
        public List<double> Numbers { get; } = new(3);
        public bool Negative { get; set; }
        public bool HasFraction { get; set; }
        public char? Hemisphere { get; set; }
        public bool HemisphereLeads { get; set; }

        public double? ToDegrees(char negativeHemisphere)
        {
            if (Numbers.Count == 0)
                return null;
            var degrees = Numbers[0];
            var minutes = Numbers.Count > 1 ? Numbers[1] : 0.0;
            var seconds = Numbers.Count > 2 ? Numbers[2] : 0.0;
            if (minutes >= 60.0 || seconds >= 60.0)
                return null;
            if (Negative && Hemisphere is not null)
                return null;
            var value = degrees + minutes / 60.0 + seconds / 3600.0;
            return Negative || Hemisphere == negativeHemisphere ? -value : value;
        }
    }

    private static List<CoordinatePart>? SplitParts(string text)
    {
        var parts = new List<CoordinatePart>();
        var current = new CoordinatePart();
        var i = 0;

        void Finish()
        {
            if (current.Numbers.Count > 0 || current.Hemisphere is not null)
                parts.Add(current);
            current = new CoordinatePart();
        }

        while (i < text.Length)
        {
            var c = char.ToUpperInvariant(text[i]);
            if (char.IsDigit(c) || ((c is '-' or '+') && i + 1 < text.Length && char.IsDigit(text[i + 1])))
            {
                // A sign, a fraction, a full set of numbers or a trailing
                // hemisphere already seen all start the next part.
                var signed = c is '-' or '+';
                if (current.Numbers.Count > 0 && (signed || current.HasFraction || current.Numbers.Count == 3))
                    Finish();
                else if (current.Hemisphere is not null && !current.HemisphereLeads)
                    Finish();

                var start = i;
                if (signed)
                    i++;
                while (i < text.Length && char.IsDigit(text[i]))
                    i++;
                // A point or comma between digits is a decimal separator.
                if (i + 1 < text.Length && text[i] is '.' or ',' && char.IsDigit(text[i + 1]))
                {
                    i++;
                    while (i < text.Length && char.IsDigit(text[i]))
                        i++;
                    current.HasFraction = true;
                }

                var token = text[start..i].Replace(',', '.');
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                    return null;
                if (current.Numbers.Count > 0 && number < 0)
                    return null;
                if (number < 0)
                    current.Negative = true;
                current.Numbers.Add(Math.Abs(number));
                continue;
            }

            if (c is 'N' or 'S' or 'E' or 'W')
            {
                if (current.Numbers.Count > 0 && current.Hemisphere is null && !current.HemisphereLeads
                    && (parts.Count == 0 || !parts[0].HemisphereLeads))
                {
                    current.Hemisphere = c;
                    Finish();
                }
                else
                {
                    Finish();
                    current.Hemisphere = c;
                    current.HemisphereLeads = true;
                }
                i++;
                continue;
            }

            if (c is '°' or 'º' or '\'' or '′' or '’' or '"' or '″' or '”')
            {
                if (current.Numbers.Count == 0)
                    return null;
                i++;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (c is ',' or ';' or '/')
            {
                Finish();
                i++;
                continue;
            }

            return null;
        }

        Finish();
        // Minutes and seconds follow whole degrees only.
        foreach (var part in parts)
        {
            if (part.Numbers.Count == 0)
                return null;
            if (part.Numbers.Count > 1 && part.Numbers[0] != Math.Floor(part.Numbers[0]))
                return null;
        }
        return parts;
    }
}
