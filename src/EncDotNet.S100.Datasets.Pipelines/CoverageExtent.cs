using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Computes the geographic (WGS84, EPSG:4326) footprint of a coverage grid,
/// reprojecting from the grid's native CRS when that CRS is projected (e.g.
/// an S-102 tile in UTM zone 31N). S-100 gridded coverages name their
/// georeferencing attributes <c>gridOrigin*/gridSpacing*Latitudinal</c> /
/// <c>*Longitudinal</c> regardless of CRS, so for a projected grid those
/// values are native metres (northing/easting), not degrees — a raw
/// <c>BoundingBox</c> built from them is <em>not</em> WGS84.
/// </summary>
/// <remarks>
/// Callers that publish a WGS84 extent (e.g. the MCP dataset catalogue's
/// <c>LoadedDataset.Bounds</c>, documented as decimal degrees WGS-84) must
/// use this helper for projected grids; otherwise geographic point-in-bounds
/// tests silently fail on UTM tiles. See S-102 Edition 3.0.0 §12 (CRS) and
/// S-100 Part 10c §10.2.1.2 (grid georeferencing attributes).
/// </remarks>
public static class CoverageExtent
{
    /// <summary>
    /// Computes the WGS84 bounding box of the supplied coverage metadata,
    /// reprojecting the grid's native corner extent through
    /// <paramref name="transformFactory"/> when the grid CRS is projected.
    /// Returns <c>null</c> when the grid is degenerate (zero rows/columns).
    /// </summary>
    /// <param name="metadata">Coverage metadata carrying the grid georeferencing and horizontal CRS.</param>
    /// <param name="transformFactory">Factory used to build the native → WGS84 transform for projected grids.</param>
    public static BoundingBox? ToWgs84Bounds(CoverageMetadata metadata, ICrsTransformFactory transformFactory)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(transformFactory);

        var grid = metadata.GridMetadata;
        if (grid.NumRows <= 0 || grid.NumColumns <= 0)
            return null;

        // Native corner extent. Grid origin is the node position of the first
        // grid point; the far node is origin + spacing * (count - 1). Take
        // min/max so a negative spacing (origin at the north/east edge) still
        // yields a correct box.
        var minX = grid.OriginLongitude;
        var maxX = grid.OriginLongitude + (grid.NumColumns - 1) * grid.SpacingLongitudinal;
        var minY = grid.OriginLatitude;
        var maxY = grid.OriginLatitude + (grid.NumRows - 1) * grid.SpacingLatitudinal;
        if (minX > maxX) (minX, maxX) = (maxX, minX);
        if (minY > maxY) (minY, maxY) = (maxY, minY);

        var transform = transformFactory.Create(metadata.HorizontalCRS, "EPSG:4326");
        if (transform.IsIdentity)
        {
            // Geographic grid: native Y is latitude, native X is longitude.
            return new BoundingBox(minY, minX, maxY, maxX);
        }

        // A projected rectangle maps to a slightly curved quad in geographic
        // space, so reproject all four corners and take the min/max envelope.
        double south = double.PositiveInfinity, west = double.PositiveInfinity;
        double north = double.NegativeInfinity, east = double.NegativeInfinity;
        (double X, double Y)[] corners = [(minX, minY), (minX, maxY), (maxX, minY), (maxX, maxY)];
        foreach (var (x, y) in corners)
        {
            var (lon, lat) = transform.Transform(x, y);
            if (lat < south) south = lat;
            if (lat > north) north = lat;
            if (lon < west) west = lon;
            if (lon > east) east = lon;
        }

        return new BoundingBox(south, west, north, east);
    }
}
