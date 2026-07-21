using EncDotNet.S100.Core;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Unit tests for <see cref="CoverageExtent.ToWgs84Bounds"/>: confirms a
/// geographic grid passes through unchanged and a projected (UTM) grid is
/// reprojected into a correct WGS-84 envelope — the contract
/// <c>LoadedDataset.Bounds</c> relies on so geographic point-in-bounds tests
/// work on UTM S-102 tiles.
/// </summary>
public class CoverageExtentTests
{
    [Fact]
    public void ToWgs84Bounds_GeographicGrid_ReturnsNativeExtent()
    {
        var metadata = BuildMetadata(
            crs: "EPSG:4326",
            originLat: 51.0, originLon: 4.0,
            spacingLat: 0.01, spacingLon: 0.01,
            numRows: 11, numCols: 21);

        var bounds = CoverageExtent.ToWgs84Bounds(metadata, new ProjNetCrsTransformFactory());

        Assert.NotNull(bounds);
        Assert.Equal(51.0, bounds!.SouthLatitude, 6);
        Assert.Equal(4.0, bounds.WestLongitude, 6);
        Assert.Equal(51.0 + 10 * 0.01, bounds.NorthLatitude, 6);
        Assert.Equal(4.0 + 20 * 0.01, bounds.EastLongitude, 6);
    }

    [Fact]
    public void ToWgs84Bounds_ProjectedUtmGrid_ReprojectsToGeographicEnvelope()
    {
        // UTM 31N tile near Rotterdam: native metres, 100 m spacing, 50×50.
        const double originNorthing = 5_750_000.0;
        const double originEasting = 592_000.0;
        const double spacing = 100.0;
        const int count = 50;

        var metadata = BuildMetadata(
            crs: "EPSG:32631",
            originLat: originNorthing, originLon: originEasting,
            spacingLat: spacing, spacingLon: spacing,
            numRows: count, numCols: count);

        var factory = new ProjNetCrsTransformFactory();
        var bounds = CoverageExtent.ToWgs84Bounds(metadata, factory);

        Assert.NotNull(bounds);

        // Independently reproject the SW and NE native corners and confirm the
        // envelope contains them and lands in the expected geographic region.
        var toWgs84 = factory.Create("EPSG:32631", "EPSG:4326");
        var (swLon, swLat) = toWgs84.Transform(originEasting, originNorthing);
        var (neLon, neLat) = toWgs84.Transform(
            originEasting + (count - 1) * spacing,
            originNorthing + (count - 1) * spacing);

        Assert.True(bounds!.SouthLatitude <= swLat + 1e-9);
        Assert.True(bounds.NorthLatitude >= neLat - 1e-9);
        Assert.True(bounds.WestLongitude <= Math.Min(swLon, neLon) + 1e-9);
        Assert.True(bounds.EastLongitude >= Math.Max(swLon, neLon) - 1e-9);

        // Sanity: Rotterdam is ~51.9 N, ~4.4 E.
        Assert.InRange(bounds.SouthLatitude, 51.0, 52.5);
        Assert.InRange(bounds.WestLongitude, 3.5, 5.0);
    }

    [Fact]
    public void ToWgs84Bounds_DegenerateGrid_ReturnsNull()
    {
        var metadata = BuildMetadata(
            crs: "EPSG:4326",
            originLat: 51.0, originLon: 4.0,
            spacingLat: 0.01, spacingLon: 0.01,
            numRows: 0, numCols: 0);

        Assert.Null(CoverageExtent.ToWgs84Bounds(metadata, new ProjNetCrsTransformFactory()));
    }

    private static CoverageMetadata BuildMetadata(
        string crs,
        double originLat, double originLon,
        double spacingLat, double spacingLon,
        int numRows, int numCols)
        => new()
        {
            Spec = new SpecRef("S-102", default),
            Extent = new BoundingBox(originLat, originLon, originLat + 1, originLon + 1),
            GridMetadata = new GridMetadata
            {
                NumRows = numRows,
                NumColumns = numCols,
                OriginLatitude = originLat,
                OriginLongitude = originLon,
                SpacingLatitudinal = spacingLat,
                SpacingLongitudinal = spacingLon,
            },
            HorizontalCRS = crs,
            VerticalDatum = "MSL",
            NoDataValue = 1_000_000f,
            ValueFields =
            [
                new CoverageValueField { Name = "depth", Type = CoverageValueType.Float, Units = "m", FillValue = 1_000_000f },
            ],
        };
}
