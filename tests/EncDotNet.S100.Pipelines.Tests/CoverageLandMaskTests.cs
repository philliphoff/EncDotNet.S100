using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Unit tests for <see cref="CoverageLandMask"/>, the pure point-in-polygon
/// helper that decides which gridded-coverage cells fall on land and must be
/// hidden when the S-104 surface is clipped to water (issue #483). The grid is
/// a geographic (EPSG:4326) 1° graticule so the identity transform is exercised;
/// cell <c>(row, col)</c> has centre <c>(lon = col, lat = row)</c>.
/// </summary>
public class CoverageLandMaskTests
{
    private const int Rows = 5;
    private const int Cols = 5;

    [Fact]
    public void Compute_MasksCellsInsideExterior_ExcludingHole()
    {
        var georef = BuildGeoreferencer();

        // Exterior square lon/lat ∈ [0.5, 3.5] → covers rows/cols 1,2,3.
        // Interior hole lon/lat ∈ [1.5, 2.5] → cuts out the (row 2, col 2) cell.
        var land = new FeatureGeometry
        {
            Type = GeometryType.Surface,
            Coordinates = Ring(0.5, 3.5, 0.5, 3.5),
            InteriorRings = new[] { Ring(1.5, 2.5, 1.5, 2.5) },
        };

        var mask = CoverageLandMask.Compute(
            georef, Rows, Cols, new[] { land }, IdentityCrsTransform.Instance);

        Assert.NotNull(mask);

        // Inside the exterior, outside the hole → masked.
        Assert.True(mask![Index(1, 1)]);
        Assert.True(mask[Index(1, 3)]);
        Assert.True(mask[Index(3, 2)]);

        // Centre cell sits in the hole (water) → NOT masked.
        Assert.False(mask[Index(2, 2)]);

        // Corners are outside the land entirely → NOT masked.
        Assert.False(mask[Index(0, 0)]);
        Assert.False(mask[Index(4, 4)]);
    }

    [Fact]
    public void Compute_ReturnsNull_WhenNoLandAreas()
    {
        var georef = BuildGeoreferencer();

        var mask = CoverageLandMask.Compute(
            georef, Rows, Cols, Array.Empty<FeatureGeometry>(), IdentityCrsTransform.Instance);

        Assert.Null(mask);
    }

    [Fact]
    public void Compute_ReturnsNull_WhenLandDoesNotOverlapGrid()
    {
        var georef = BuildGeoreferencer();

        // Land square far to the east — no cell centre falls inside it.
        var land = new FeatureGeometry
        {
            Type = GeometryType.Surface,
            Coordinates = Ring(100.0, 101.0, 100.0, 101.0),
        };

        var mask = CoverageLandMask.Compute(
            georef, Rows, Cols, new[] { land }, IdentityCrsTransform.Instance);

        Assert.Null(mask);
    }

    [Fact]
    public void Compute_IgnoresNonSurfaceGeometries()
    {
        var georef = BuildGeoreferencer();

        var curve = new FeatureGeometry
        {
            Type = GeometryType.Curve,
            Coordinates = new[] { new GeoPosition(1.0, 1.0), new GeoPosition(3.0, 3.0) },
        };

        var mask = CoverageLandMask.Compute(
            georef, Rows, Cols, new[] { curve }, IdentityCrsTransform.Instance);

        Assert.Null(mask);
    }

    private static GridGeoreferencer BuildGeoreferencer()
    {
        var metadata = new GridMetadata
        {
            NumRows = Rows,
            NumColumns = Cols,
            OriginLatitude = 0.0,
            OriginLongitude = 0.0,
            SpacingLatitudinal = 1.0,
            SpacingLongitudinal = 1.0,
        };
        return new GridGeoreferencer(metadata, "EPSG:4326");
    }

    // Axis-aligned rectangle ring in WGS84 (latitude, longitude) order.
    private static IReadOnlyList<GeoPosition> Ring(double minLon, double maxLon, double minLat, double maxLat) =>
        new[]
        {
            new GeoPosition(minLat, minLon),
            new GeoPosition(minLat, maxLon),
            new GeoPosition(maxLat, maxLon),
            new GeoPosition(maxLat, minLon),
            new GeoPosition(minLat, minLon),
        };

    private static int Index(int row, int col) => row * Cols + col;
}
