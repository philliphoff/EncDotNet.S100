namespace EncDotNet.S100.Pipelines.Coverage;

/// <summary>
/// Encapsulates the grid-to-world coordinate transform for a sampled coverage.
/// Converts grid cell (row, col) positions to geographic coordinates in the
/// grid's native CRS.
/// </summary>
public sealed class GridGeoreferencer
{
    private readonly GridMetadata _metadata;

    public GridGeoreferencer(GridMetadata metadata, string crs)
    {
        _metadata = metadata;
        CRS = crs;
    }

    /// <summary>The EPSG code or identifier of the grid's native CRS (e.g. "EPSG:4326", "EPSG:32608").</summary>
    public string CRS { get; }

    /// <summary>The underlying grid metadata.</summary>
    public GridMetadata Metadata => _metadata;

    /// <summary>
    /// Converts a grid cell position to coordinates in the grid's native CRS.
    /// For geographic CRS, returns (longitude, latitude).
    /// For projected CRS (e.g. UTM), returns (easting, northing).
    /// </summary>
    public (double X, double Y) ToNative(int row, int col) =>
        (
            _metadata.OriginLongitude + col * _metadata.SpacingLongitudinal,
            _metadata.OriginLatitude + row * _metadata.SpacingLatitudinal
        );

    /// <summary>
    /// Inverse: converts native CRS coordinates to the nearest grid cell.
    /// </summary>
    public (int Row, int Col) ToGrid(double x, double y) =>
        (
            (int)((y - _metadata.OriginLatitude) / _metadata.SpacingLatitudinal),
            (int)((x - _metadata.OriginLongitude) / _metadata.SpacingLongitudinal)
        );
}
