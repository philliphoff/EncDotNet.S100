using System.Globalization;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Validation;

/// <summary>
/// How a gridded coverage's horizontal CRS shapes its georeferencing values.
/// </summary>
public enum HorizontalCrsKind
{
    /// <summary>
    /// A geographic CRS: <c>gridOriginLongitude</c> / <c>gridOriginLatitude</c>
    /// and the spacings are decimal degrees.
    /// </summary>
    Geographic,

    /// <summary>
    /// A projected CRS (e.g. WGS 84 / UTM): the same attributes are eastings
    /// and northings in metres, despite their latitude/longitude names.
    /// </summary>
    Projected,

    /// <summary>A CRS this helper cannot classify; range checks are skipped.</summary>
    Unknown,
}

/// <summary>
/// CRS-aware plausibility checks for the georeferencing of an S-100 gridded
/// coverage (S-100 Part 10c §10.2.1.2). S-100 names the grid attributes
/// <c>gridOriginLatitude</c> / <c>gridOriginLongitude</c> whatever the CRS, so
/// for a projected grid — which S-102 Edition 3.0.0 and later product editions
/// allow — the values are metres and a latitude/longitude range check is wrong.
/// </summary>
/// <remarks>
/// <para>
/// Positions are passed in the grid's native axis order: <c>x</c> is longitude
/// or easting, <c>y</c> is latitude or northing.
/// </para>
/// <para>
/// For a geographic CRS a position must lie within [-90, 90] latitude and
/// [-180, 180] longitude. For a UTM CRS (WGS 84, NAD83 or ETRS89 zones) the
/// easting must lie in [0, 1 000 000] m and the northing in [0, 10 000 000] m.
/// When an <see cref="ICrsTransformFactory"/> is available (see
/// <see cref="ValidationContext.CrsTransformFactory"/>) a projected position
/// must also transform to a finite, in-range WGS 84 position, and
/// <see cref="TryGetGeographicBounds"/> can reproject a grid extent for a
/// finding's <see cref="ValidationFinding.BoundingBox"/>. Positions in a CRS
/// classified as <see cref="HorizontalCrsKind.Unknown"/> are not checked.
/// </para>
/// </remarks>
public sealed class GridGeoreferencing
{
    private const double UtmMaxEasting = 1_000_000;
    private const double UtmMaxNorthing = 10_000_000;

    private readonly ICrsTransform? _toWgs84;

    private GridGeoreferencing(int? epsg, HorizontalCrsKind kind, ICrsTransform? toWgs84)
    {
        Epsg = epsg;
        Kind = kind;
        _toWgs84 = toWgs84;
    }

    /// <summary>The EPSG code of the grid's horizontal CRS, or <see langword="null"/> when undeclared.</summary>
    public int? Epsg { get; }

    /// <summary>How the CRS shapes the grid's georeferencing values.</summary>
    public HorizontalCrsKind Kind { get; }

    /// <summary>
    /// Resolves the checks for a grid in the CRS <paramref name="epsg"/>.
    /// </summary>
    /// <param name="epsg">
    /// The dataset's <c>horizontalCRS</c> EPSG code. <see langword="null"/> is
    /// taken as geographic, the S-100 HDF5 default (WGS 84, EPSG:4326).
    /// </param>
    /// <param name="context">
    /// The validation context; its <see cref="ValidationContext.CrsTransformFactory"/>,
    /// when set, enables reprojection of projected positions to WGS 84.
    /// </param>
    /// <returns>The checks for that CRS.</returns>
    public static GridGeoreferencing For(int? epsg, ValidationContext? context)
    {
        var kind = Classify(epsg);
        ICrsTransform? toWgs84 = null;
        if (kind == HorizontalCrsKind.Projected && context?.CrsTransformFactory is { } factory)
        {
            try
            {
                toWgs84 = factory.Create(
                    string.Create(CultureInfo.InvariantCulture, $"EPSG:{epsg}"), "EPSG:4326");
            }
            catch (Exception ex) when (ex is NotSupportedException or ArgumentException or FormatException)
            {
                // The factory cannot reproject this CRS; fall back to the native checks.
            }
        }
        return new GridGeoreferencing(epsg, kind, toWgs84);
    }

    /// <summary>
    /// Classifies an EPSG code as geographic, projected, or unknown.
    /// <see langword="null"/> is taken as geographic (the S-100 HDF5 default).
    /// </summary>
    /// <param name="epsg">The EPSG code.</param>
    /// <returns>The CRS kind.</returns>
    public static HorizontalCrsKind Classify(int? epsg) => epsg switch
    {
        null => HorizontalCrsKind.Geographic,
        // WGS 84 (2D, 3D), NAD83, NAD83(CSRS), NAD83(2011), ETRS89, GDA94, GDA2020, NZGD2000.
        4326 or 4979 or 4269 or 4617 or 6318 or 4258 or 4283 or 7844 or 4167 => HorizontalCrsKind.Geographic,
        _ when IsUtm(epsg.Value) => HorizontalCrsKind.Projected,
        // WGS 84 / Pseudo-Mercator, World Mercator, UPS North/South, Arctic/Antarctic polar stereographic.
        3857 or 3395 or 5041 or 5042 or 3995 or 3031 => HorizontalCrsKind.Projected,
        _ => HorizontalCrsKind.Unknown,
    };

    /// <summary>
    /// Checks one grid position and describes each problem found; an empty
    /// list means the position is plausible (or the CRS is unknown).
    /// </summary>
    /// <param name="x">Longitude (geographic) or easting (projected).</param>
    /// <param name="y">Latitude (geographic) or northing (projected).</param>
    /// <param name="xLabel">How messages name <paramref name="x"/> (e.g. <c>OriginLongitude</c>).</param>
    /// <param name="yLabel">How messages name <paramref name="y"/> (e.g. <c>OriginLatitude</c>).</param>
    /// <param name="longitudeNote">
    /// Optional text appended to a geographic longitude problem (e.g. that
    /// antimeridian-spanning tiles are out of scope).
    /// </param>
    /// <returns>The problems, one sentence fragment each.</returns>
    public IReadOnlyList<string> CheckPosition(double x, double y, string xLabel, string yLabel, string? longitudeNote = null)
    {
        var problems = new List<string>();
        switch (Kind)
        {
            case HorizontalCrsKind.Geographic:
                if (!(y >= -90 && y <= 90))
                    problems.Add($"{yLabel} {Fmt(y)} outside [-90, 90]");
                if (!(x >= -180 && x <= 180))
                    problems.Add($"{xLabel} {Fmt(x)} outside [-180, 180]" + (longitudeNote is null ? "" : $" {longitudeNote}"));
                break;

            case HorizontalCrsKind.Projected:
                if (Epsg is int code && IsUtm(code))
                {
                    if (!(x >= 0 && x <= UtmMaxEasting))
                        problems.Add($"{xLabel} (easting) {Fmt(x)} m outside the UTM range [0, 1000000] m for EPSG:{code}");
                    if (!(y >= 0 && y <= UtmMaxNorthing))
                        problems.Add($"{yLabel} (northing) {Fmt(y)} m outside the UTM range [0, 10000000] m for EPSG:{code}");
                }
                else if (!double.IsFinite(x) || !double.IsFinite(y))
                {
                    problems.Add($"{xLabel}/{yLabel} ({Fmt(x)}, {Fmt(y)}) is not a finite projected position");
                }

                if (problems.Count == 0 && TryToWgs84(x, y, out var lon, out var lat)
                    && !(lat >= -90 && lat <= 90 && lon >= -180 && lon <= 180))
                {
                    problems.Add(
                        $"{xLabel}/{yLabel} ({Fmt(x)}, {Fmt(y)}) in EPSG:{Epsg} does not reproject to a valid WGS 84 position " +
                        $"(got latitude {Fmt(lat)}, longitude {Fmt(lon)})");
                }
                break;
        }
        return problems;
    }

    /// <summary>
    /// Computes the WGS 84 envelope of the native rectangle spanned by two
    /// opposite grid corners, for a finding's
    /// <see cref="ValidationFinding.BoundingBox"/>. A geographic rectangle is
    /// returned as-is (edges ordered, values unclamped); a projected one is
    /// reprojected corner by corner when a transform is available.
    /// </summary>
    /// <param name="x0">Longitude or easting of one corner.</param>
    /// <param name="y0">Latitude or northing of one corner.</param>
    /// <param name="x1">Longitude or easting of the opposite corner.</param>
    /// <param name="y1">Latitude or northing of the opposite corner.</param>
    /// <param name="bounds">The geographic envelope, when available.</param>
    /// <returns>
    /// <see langword="false"/> for an unknown CRS, or a projected CRS with no
    /// usable transform, so no metre values are passed off as degrees.
    /// </returns>
    public bool TryGetGeographicBounds(double x0, double y0, double x1, double y1, out BoundingBox? bounds)
    {
        bounds = null;
        if (Kind == HorizontalCrsKind.Geographic)
        {
            bounds = new BoundingBox(Math.Min(y0, y1), Math.Min(x0, x1), Math.Max(y0, y1), Math.Max(x0, x1));
            return true;
        }
        if (Kind != HorizontalCrsKind.Projected || _toWgs84 is null)
            return false;

        double south = double.PositiveInfinity, west = double.PositiveInfinity;
        double north = double.NegativeInfinity, east = double.NegativeInfinity;
        foreach (var (x, y) in new[] { (x0, y0), (x0, y1), (x1, y0), (x1, y1) })
        {
            if (!TryToWgs84(x, y, out var lon, out var lat))
                return false;
            south = Math.Min(south, lat);
            north = Math.Max(north, lat);
            west = Math.Min(west, lon);
            east = Math.Max(east, lon);
        }
        bounds = new BoundingBox(south, west, north, east);
        return true;
    }

    private bool TryToWgs84(double x, double y, out double lon, out double lat)
    {
        lon = lat = double.NaN;
        if (_toWgs84 is null || !double.IsFinite(x) || !double.IsFinite(y))
            return false;
        try
        {
            (lon, lat) = _toWgs84.Transform(x, y);
        }
        catch (Exception ex) when (ex is ArgumentException or ArithmeticException or InvalidOperationException)
        {
            return false;
        }
        return double.IsFinite(lon) && double.IsFinite(lat);
    }

    // WGS 84 UTM N/S (326xx/327xx), NAD83 UTM 1N–23N (269xx), ETRS89 UTM 28N–38N (258xx).
    private static bool IsUtm(int code) =>
        code is >= 32601 and <= 32660
            or >= 32701 and <= 32760
            or >= 26901 and <= 26923
            or >= 25828 and <= 25838;

    private static string Fmt(double value)
        => value.ToString("0.######", CultureInfo.InvariantCulture);
}
