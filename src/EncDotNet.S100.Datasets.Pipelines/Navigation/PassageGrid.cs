using System.Globalization;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Validation;

namespace EncDotNet.S100.Datasets.Pipelines.Navigation;

/// <summary>
/// A regular S-102 or S-104 grid's georeferencing as latitude rows and
/// longitude columns, so the passage sources can find the cell or node under
/// a WGS-84 position whatever quirks the producer wrote (#943).
/// </summary>
/// <remarks>
/// <para>
/// Three producer quirks are absorbed here, at lookup, rather than by the
/// readers rewriting the dataset:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <b>Swapped axis order.</b> When the reader's
/// <see cref="AxisOrderAssessment"/> says the grid origins are longitude
/// first, the origin, spacing and node counts are exchanged back. The value
/// array is not transposed: only the georeferencing attributes are swapped, and
/// the values stay in rows of latitude. The readers report the swap without
/// rewriting the grid (#650), so the coverage the renderer and validators see
/// is the one in the file.
/// </description></item>
/// <item><description>
/// <b>Geographic CRSs other than WGS-84.</b> Any CRS
/// <see cref="GridGeoreferencing.Classify"/> calls geographic (ETRS89, NAD83,
/// GDA2020, …) is read as longitude and latitude in degrees, without a datum
/// shift. Those datums differ from WGS-84 by a metre or two, well inside an
/// S-102 or S-104 grid cell. Projected CRSs are reprojected through the
/// transform factory as before.
/// </description></item>
/// <item><description>
/// <b>The 0…360° longitude frame.</b> In a geographic grid the query
/// longitude is wrapped to the copy nearest the grid's centre, so a grid with
/// its origin at 225° (or one spanning the antimeridian, say 170°…190°) is
/// found from −135°.
/// </description></item>
/// </list>
/// </remarks>
internal sealed class PassageGrid
{
    private readonly ICrsTransform? _toNative;
    private readonly ICrsTransform? _toWgs84;
    private readonly double _centerX;

    private PassageGrid(
        double originY, double originX, double spacingY, double spacingX, int rows, int columns,
        ICrsTransform? toNative, ICrsTransform? toWgs84)
    {
        OriginY = originY;
        OriginX = originX;
        SpacingY = spacingY;
        SpacingX = spacingX;
        Rows = rows;
        Columns = columns;
        _toNative = toNative;
        _toWgs84 = toWgs84;
        _centerX = originX + (columns * spacingX / 2);
    }

    /// <summary>The latitude (or northing) of the first row.</summary>
    public double OriginY { get; }

    /// <summary>The longitude (or easting) of the first column.</summary>
    public double OriginX { get; }

    /// <summary>The step between rows.</summary>
    public double SpacingY { get; }

    /// <summary>The step between columns.</summary>
    public double SpacingX { get; }

    /// <summary>The number of rows (along latitude).</summary>
    public int Rows { get; }

    /// <summary>The number of columns (along longitude); a value's index is <c>row * Columns + column</c>.</summary>
    public int Columns { get; }

    /// <summary>Whether the grid is in a geographic CRS, in degrees.</summary>
    public bool IsGeographic => _toNative is null;

    /// <summary>
    /// The grid's own extent, from the first row and column to one spacing
    /// past the last, in degrees (longitudes possibly beyond ±180°); <c>null</c>
    /// for a projected grid.
    /// </summary>
    public BoundingBox? GeographicExtent => IsGeographic
        ? new BoundingBox(
            Math.Min(OriginY, OriginY + (Rows * SpacingY)),
            Math.Min(OriginX, OriginX + (Columns * SpacingX)),
            Math.Max(OriginY, OriginY + (Rows * SpacingY)),
            Math.Max(OriginX, OriginX + (Columns * SpacingX)))
        : null;

    /// <summary>
    /// Builds the lookup for one grid, from its georeferencing as encoded.
    /// </summary>
    /// <param name="originLatitude">The encoded origin latitude.</param>
    /// <param name="originLongitude">The encoded origin longitude.</param>
    /// <param name="spacingLatitudinal">The encoded latitudinal spacing.</param>
    /// <param name="spacingLongitudinal">The encoded longitudinal spacing.</param>
    /// <param name="numPointsLatitudinal">The encoded number of latitudinal points.</param>
    /// <param name="numPointsLongitudinal">The encoded number of longitudinal points.</param>
    /// <param name="horizontalCrs">The declared EPSG code, or <c>null</c> for WGS-84.</param>
    /// <param name="axisOrder">The reader's axis-order assessment, if any.</param>
    /// <param name="transforms">Reprojects WGS-84 into a projected grid's CRS.</param>
    /// <returns>The lookup.</returns>
    /// <exception cref="NotSupportedException">The factory cannot reproject the grid's CRS.</exception>
    public static PassageGrid Create(
        double originLatitude, double originLongitude,
        double spacingLatitudinal, double spacingLongitudinal,
        int numPointsLatitudinal, int numPointsLongitudinal,
        int? horizontalCrs,
        AxisOrderAssessment? axisOrder,
        ICrsTransformFactory transforms)
    {
        if (GridGeoreferencing.Classify(horizontalCrs) == HorizontalCrsKind.Geographic)
        {
            return axisOrder?.Outcome == AxisOrderOutcome.Swapped && !axisOrder.Normalized
                ? new PassageGrid(originLongitude, originLatitude, spacingLongitudinal, spacingLatitudinal, numPointsLongitudinal, numPointsLatitudinal, null, null)
                : new PassageGrid(originLatitude, originLongitude, spacingLatitudinal, spacingLongitudinal, numPointsLatitudinal, numPointsLongitudinal, null, null);
        }

        var crs = string.Create(CultureInfo.InvariantCulture, $"EPSG:{horizontalCrs}");
        return new PassageGrid(
            originLatitude, originLongitude, spacingLatitudinal, spacingLongitudinal, numPointsLatitudinal, numPointsLongitudinal,
            transforms.Create("EPSG:4326", crs),
            transforms.Create(crs, "EPSG:4326"));
    }

    /// <summary>A WGS-84 position in the grid's axes.</summary>
    /// <param name="latitude">The latitude.</param>
    /// <param name="longitude">The longitude, in any frame.</param>
    /// <returns>The position along the columns (<c>X</c>) and rows (<c>Y</c>).</returns>
    public (double X, double Y) ToGrid(double latitude, double longitude) =>
        _toNative is null
            ? (WrapNear(longitude, _centerX), latitude)
            : _toNative.Transform(longitude, latitude);

    /// <summary>A point in the grid's axes as WGS-84.</summary>
    /// <param name="x">Along the columns.</param>
    /// <param name="y">Along the rows.</param>
    /// <returns>The position.</returns>
    public GeoPosition ToWgs84(double x, double y)
    {
        var (lon, lat) = _toWgs84 is null ? (x, y) : _toWgs84.Transform(x, y);
        return new GeoPosition(lat, lon);
    }

    /// <summary>
    /// Whether a WGS-84 box meets the grid: the grid's own extent for a
    /// geographic grid (the box shifted by whole turns to its frame), or
    /// <paramref name="fallback"/> for a projected one.
    /// </summary>
    /// <param name="fallback">The dataset's declared WGS-84 bounds.</param>
    /// <param name="south">The box's south edge.</param>
    /// <param name="west">The box's west edge.</param>
    /// <param name="north">The box's north edge.</param>
    /// <param name="east">The box's east edge.</param>
    /// <returns><c>true</c> if they overlap.</returns>
    public bool Intersects(BoundingBox fallback, double south, double west, double north, double east)
    {
        var b = GeographicExtent ?? fallback;
        var shift = IsGeographic ? WrapNear((west + east) / 2, (b.WestLongitude + b.EastLongitude) / 2) - ((west + east) / 2) : 0;
        return b.NorthLatitude >= south && b.SouthLatitude <= north && b.EastLongitude >= west + shift && b.WestLongitude <= east + shift;
    }

    /// <summary>The longitude 360° turns away from <paramref name="longitude"/> that is nearest <paramref name="center"/>.</summary>
    internal static double WrapNear(double longitude, double center) =>
        longitude + (360 * Math.Round((center - longitude) / 360));
}
