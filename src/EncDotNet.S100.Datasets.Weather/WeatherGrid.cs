using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;

namespace EncDotNet.S100.Datasets.Weather;

/// <summary>
/// A normalised regular latitude/longitude grid (EPSG:4326) in the order
/// <see cref="GridMetadata"/> expects: the origin is the south-west point,
/// rows run north and columns run east, and every longitude is in −180 to 180.
/// Point <c>(row, col)</c> is at
/// <c>(OriginLatitude + row × SpacingLatitudinal, OriginLongitude + col × SpacingLongitudinal)</c>.
/// </summary>
/// <param name="Rows">Number of rows.</param>
/// <param name="Columns">Number of columns.</param>
/// <param name="OriginLatitude">Latitude of the south-west point, in degrees.</param>
/// <param name="OriginLongitude">Longitude of the south-west point, in degrees.</param>
/// <param name="SpacingLatitudinal">Degrees between rows; positive.</param>
/// <param name="SpacingLongitudinal">Degrees between columns; positive.</param>
public sealed record WeatherGrid(
    int Rows,
    int Columns,
    double OriginLatitude,
    double OriginLongitude,
    double SpacingLatitudinal,
    double SpacingLongitudinal)
{
    /// <summary>The number of points, <see cref="Rows"/> × <see cref="Columns"/>.</summary>
    public int CellCount => Rows * Columns;

    /// <summary>
    /// The grid footprint as <see cref="CoverageMetadata.NativeExtent"/>
    /// defines it: from the origin to one spacing past the last point on
    /// each axis.
    /// </summary>
    public BoundingBox Extent => new(
        OriginLatitude,
        OriginLongitude,
        OriginLatitude + SpacingLatitudinal * Rows,
        OriginLongitude + SpacingLongitudinal * Columns);

    /// <summary>Returns the grid as coverage pipeline <see cref="GridMetadata"/>.</summary>
    public GridMetadata ToGridMetadata() => new()
    {
        NumRows = Rows,
        NumColumns = Columns,
        OriginLatitude = OriginLatitude,
        OriginLongitude = OriginLongitude,
        SpacingLatitudinal = SpacingLatitudinal,
        SpacingLongitudinal = SpacingLongitudinal,
    };
}
