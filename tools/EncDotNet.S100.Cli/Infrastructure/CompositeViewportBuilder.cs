using System.Globalization;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Cli.Infrastructure;

/// <summary>
/// Builds an explicit shared <see cref="Viewport"/> for a composite render from
/// the CLI's viewport flags — either a geographic bounding box
/// (<c>--bbox minLon,minLat,maxLon,maxLat</c>) or a centre + scale
/// (<c>--center lon,lat --scale N</c>). When neither is supplied the compositor
/// falls back to its union auto-fit (see
/// <c>HeadlessCompositor.BuildUnionViewport</c>), whose EPSG:3857 aspect-fit and
/// scale-denominator maths this helper mirrors so an explicit box behaves
/// identically to the auto-fit for the same extent.
/// </summary>
internal static class CompositeViewportBuilder
{
    // EPSG:3857 sphere radius (WGS-84 semi-major axis), in metres. Matches
    // EncDotNet.S100.Rendering.Scene.WebMercator.EarthRadius.
    private const double EarthRadius = 6378137.0;

    // S-100 Part 9 §11.1: 1 px = 0.28 mm = 0.00028 m on the nominal display
    // surface at 96 DPI. Matches ScaleVisibility.DenomToResolutionMetres.
    private const double DenomToResolutionMetres = 0.00028;

    private const double DegToRad = Math.PI / 180.0;
    private const double RadToDeg = 180.0 / Math.PI;

    /// <summary>Practical EPSG:3857 latitude limit (±85.05112878°).</summary>
    private const double MaxLatitude = 85.05112878;

    /// <summary>
    /// Builds a viewport that frames the WGS-84 bounding box
    /// [<paramref name="minLon"/>, <paramref name="minLat"/>] –
    /// [<paramref name="maxLon"/>, <paramref name="maxLat"/>] in a
    /// <paramref name="width"/> × <paramref name="height"/> pixel image,
    /// expanding the smaller axis so the box's aspect matches the output (no
    /// distortion), mirroring the compositor's union auto-fit.
    /// </summary>
    public static Viewport FromBoundingBox(
        double minLon, double minLat, double maxLon, double maxLat, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        var (minX, minY) = FromLonLat(minLon, minLat);
        var (maxX, maxY) = FromLonLat(maxLon, maxLat);

        double spanX = maxX - minX;
        double spanY = maxY - minY;

        // Expand the smaller dimension so the extent's aspect matches the output.
        double viewAspect = (double)width / height;
        double dataAspect = spanY > 0 ? spanX / spanY : viewAspect;
        if (dataAspect > viewAspect)
        {
            double targetSpanY = spanX / viewAspect;
            double grow = (targetSpanY - spanY) / 2.0;
            minY -= grow; maxY += grow;
        }
        else
        {
            double targetSpanX = spanY * viewAspect;
            double grow = (targetSpanX - spanX) / 2.0;
            minX -= grow; maxX += grow;
        }

        return BuildViewport(minX, minY, maxX, maxY, width, height);
    }

    /// <summary>
    /// Builds a viewport centred on (<paramref name="centerLon"/>,
    /// <paramref name="centerLat"/>) at the given scale denominator
    /// (e.g. 25000 for 1:25 000) for a <paramref name="width"/> ×
    /// <paramref name="height"/> pixel image.
    /// </summary>
    public static Viewport FromCenterScale(
        double centerLon, double centerLat, double scaleDenominator, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scaleDenominator);

        var (centerX, centerY) = FromLonLat(centerLon, centerLat);

        // Invert the compositor's denom maths: it derives
        //   denom = (spanX / width) * cos(midLat) / DenomToResolutionMetres,
        // so the EPSG:3857 span for a target denom is
        //   spanX = denom * DenomToResolutionMetres * width / cos(midLat).
        double clampedLat = Math.Clamp(centerLat, -MaxLatitude, MaxLatitude);
        double cosLat = Math.Cos(clampedLat * DegToRad);
        if (cosLat <= 0)
            cosLat = double.Epsilon;

        double metresPerPixel = scaleDenominator * DenomToResolutionMetres / cosLat;
        double halfSpanX = metresPerPixel * width / 2.0;
        double halfSpanY = metresPerPixel * height / 2.0;

        return BuildViewport(
            centerX - halfSpanX, centerY - halfSpanY,
            centerX + halfSpanX, centerY + halfSpanY,
            width, height);
    }

    /// <summary>
    /// Parses a comma-separated list of invariant-culture doubles (e.g.
    /// <c>"-1.5,50.0,-1.0,50.5"</c>) with an exact expected arity. Returns
    /// <see langword="false"/> when the arity or any token is invalid.
    /// </summary>
    public static bool TryParseDoubles(string value, int expected, out double[] values)
    {
        values = Array.Empty<double>();
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var tokens = value.Split(',', StringSplitOptions.TrimEntries);
        if (tokens.Length != expected)
            return false;

        var parsed = new double[expected];
        for (int i = 0; i < expected; i++)
        {
            if (!double.TryParse(tokens[i], NumberStyles.Float, CultureInfo.InvariantCulture, out parsed[i])
                || !double.IsFinite(parsed[i]))
                return false;
        }

        values = parsed;
        return true;
    }

    private static Viewport BuildViewport(
        double minX, double minY, double maxX, double maxY, int width, int height)
    {
        var (minLon, minLat) = ToLonLat(minX, minY);
        var (maxLon, maxLat) = ToLonLat(maxX, maxY);

        double midLatRad = (minLat + maxLat) * 0.5 * DegToRad;
        double groundMetresPerPixel = (maxX - minX) / width * Math.Cos(midLatRad);
        double denom = groundMetresPerPixel / DenomToResolutionMetres;

        return new Viewport
        {
            MinLongitude = minLon,
            MaxLongitude = maxLon,
            MinLatitude = minLat,
            MaxLatitude = maxLat,
            WidthPixels = width,
            HeightPixels = height,
            ScaleDenominator = denom > 0 ? denom : 1.0,
        };
    }

    private static (double X, double Y) FromLonLat(double longitude, double latitude)
    {
        double lat = Math.Clamp(latitude, -MaxLatitude, MaxLatitude);
        double x = EarthRadius * longitude * DegToRad;
        double y = EarthRadius * Math.Log(Math.Tan(Math.PI / 4.0 + lat * DegToRad / 2.0));
        return (x, y);
    }

    private static (double Longitude, double Latitude) ToLonLat(double x, double y)
    {
        double longitude = x / EarthRadius * RadToDeg;
        double latitude = (2.0 * Math.Atan(Math.Exp(y / EarthRadius)) - Math.PI / 2.0) * RadToDeg;
        if (double.IsNaN(latitude) || latitude < -MaxLatitude) latitude = -MaxLatitude;
        else if (latitude > MaxLatitude) latitude = MaxLatitude;
        return (longitude, latitude);
    }
}
