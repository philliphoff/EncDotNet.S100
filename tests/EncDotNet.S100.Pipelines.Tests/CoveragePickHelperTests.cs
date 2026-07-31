using EncDotNet.S100.Core;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Unit tests for <see cref="CoveragePickHelper"/>: confirms the
/// helper reprojects geographic clicks into the grid CRS and snaps
/// to the containing cell, and rejects clicks outside the grid extent.
/// </summary>
public class CoveragePickHelperTests
{
    [Fact]
    public void Sample_InBoundsCell_ReturnsExpectedRowColAndValue()
    {
        var source = BuildStubSource(originLat: 10.0, originLon: 20.0, spacing: 1.0,
            depths: new[,]
            {
                { 1f, 2f, 3f },
                { 4f, 5f, 6f },
                { 7f, 8f, 9f },
            });

        // Click at (lat 11.5, lon 21.5) → row 1, col 1 (centre cell).
        var result = CoveragePickHelper.Sample(source, IdentityFactory.Instance, latitude: 11.5, longitude: 21.5);

        Assert.NotNull(result);
        Assert.Equal(1, result!.Row);
        Assert.Equal(1, result.Col);
        Assert.Equal(5f, result.Values["depth"]);
    }

    [Fact]
    public void Sample_OutOfExtentClick_ReturnsNull()
    {
        var source = BuildStubSource(originLat: 10.0, originLon: 20.0, spacing: 1.0,
            depths: new[,] { { 1f, 2f }, { 3f, 4f } });

        // Click far north of the grid.
        Assert.Null(CoveragePickHelper.Sample(source, IdentityFactory.Instance, latitude: 50.0, longitude: 21.0));

        // Click south of the origin (negative row).
        Assert.Null(CoveragePickHelper.Sample(source, IdentityFactory.Instance, latitude: 5.0, longitude: 21.0));
    }

    [Fact]
    public void Sample_NoDataCell_ReturnsCellWithFillValue()
    {
        const float fill = 1_000_000f;
        var source = BuildStubSource(originLat: 0.0, originLon: 0.0, spacing: 1.0, fill: fill,
            depths: new[,]
            {
                { fill, 2f },
                { 3f, 4f },
            });

        var result = CoveragePickHelper.Sample(source, IdentityFactory.Instance, latitude: 0.5, longitude: 0.5);
        Assert.NotNull(result);
        Assert.Equal(fill, result!.Values["depth"]);
        Assert.Equal(fill, result.NoDataValue);
    }

    /// <summary>
    /// A projected (UTM zone 31N, EPSG:32631) S-102 grid stores its origin
    /// and spacing in native metres, not degrees. The helper must reproject
    /// the WGS84 click into the grid CRS before snapping to a cell — the
    /// exact path the depth-assimilation base-depth resolver depends on for
    /// UTM tiles (e.g. the Rotterdam S-102 tile). Uses the real
    /// <see cref="ProjNetCrsTransformFactory"/> rather than an identity stub.
    /// </summary>
    [Fact]
    public void Sample_ProjectedUtmGrid_ReprojectsClickToExpectedCell()
    {
        // Native UTM 31N grid: origin at (easting 592000, northing 5750000),
        // 100 m spacing, 5×5 — a synthetic stand-in for the Rotterdam tile.
        const double originNorthing = 5_750_000.0;
        const double originEasting = 592_000.0;
        const double spacing = 100.0;
        var depths = new float[5, 5];
        for (int r = 0; r < 5; r++)
            for (int c = 0; c < 5; c++)
                depths[r, c] = r * 10 + c;

        var source = BuildStubSource(
            originLat: originNorthing, originLon: originEasting, spacing: spacing,
            depths: depths, horizontalCrs: "EPSG:32631");

        var factory = new ProjNetCrsTransformFactory();

        // Aim at the centre of cell (row 2, col 3) in native metres, then
        // project that native point back to WGS84 to obtain the click.
        double targetEasting = originEasting + (3 + 0.5) * spacing;
        double targetNorthing = originNorthing + (2 + 0.5) * spacing;
        var toWgs84 = factory.Create("EPSG:32631", "EPSG:4326");
        var (lon, lat) = toWgs84.Transform(targetEasting, targetNorthing);

        var result = CoveragePickHelper.Sample(source, factory, latitude: lat, longitude: lon);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Row);
        Assert.Equal(3, result.Col);
        Assert.Equal(depths[2, 3], result.Values["depth"]);
    }

    /// <summary>
    /// A WGS84 click that reprojects to a point outside a projected (UTM)
    /// grid's native extent must be rejected (returns null), not clamped to
    /// an edge cell.
    /// </summary>
    [Fact]
    public void Sample_ProjectedUtmGrid_ClickOutsideExtent_ReturnsNull()
    {
        const double originNorthing = 5_750_000.0;
        const double originEasting = 592_000.0;
        const double spacing = 100.0;
        var source = BuildStubSource(
            originLat: originNorthing, originLon: originEasting, spacing: spacing,
            depths: new float[3, 3], horizontalCrs: "EPSG:32631");

        var factory = new ProjNetCrsTransformFactory();

        // A native point well south-west of the origin → outside the grid.
        var toWgs84 = factory.Create("EPSG:32631", "EPSG:4326");
        var (lon, lat) = toWgs84.Transform(originEasting - 5_000.0, originNorthing - 5_000.0);

        Assert.Null(CoveragePickHelper.Sample(source, factory, latitude: lat, longitude: lon));
    }

    private static StubCoverageSource BuildStubSource(
        double originLat,
        double originLon,
        double spacing,
        float[,] depths,
        float fill = 1_000_000f,
        string horizontalCrs = "EPSG:4326")
    {
        var rows = depths.GetLength(0);
        var cols = depths.GetLength(1);
        var grid = new GridMetadata
        {
            NumRows = rows,
            NumColumns = cols,
            OriginLatitude = originLat,
            OriginLongitude = originLon,
            SpacingLatitudinal = spacing,
            SpacingLongitudinal = spacing,
        };
        var meta = new CoverageMetadata
        {
            Spec = new SpecRef("S-102", default),
            Extent = new BoundingBox(
                southLatitude: originLat,
                westLongitude: originLon,
                northLatitude: originLat + (rows - 1) * spacing,
                eastLongitude: originLon + (cols - 1) * spacing),
            GridMetadata = grid,
            HorizontalCRS = horizontalCrs,
            VerticalDatum = "MSL",
            NoDataValue = fill,
            ValueFields =
            [
                new CoverageValueField { Name = "depth", Type = CoverageValueType.Float, Units = "m", FillValue = fill },
            ],
        };
        return new StubCoverageSource(meta, depths);
    }

    private sealed class StubCoverageSource : ICoverageSource
    {
        private readonly float[,] _depths;
        public StubCoverageSource(CoverageMetadata metadata, float[,] depths)
        {
            Metadata = metadata;
            _depths = depths;
        }
        public CoverageMetadata Metadata { get; }
        public IReadOnlyList<DateTime> AvailableTimes => Array.Empty<DateTime>();
        public void SelectTime(DateTime time) { }
        public SampledCoverage Sample(GridRegion region, CancellationToken cancellationToken = default)
        {
            var rs = region.RowStart ?? 0;
            var re = region.RowEnd ?? Metadata.GridMetadata.NumRows;
            var cs = region.ColStart ?? 0;
            var ce = region.ColEnd ?? Metadata.GridMetadata.NumColumns;
            var rows = re - rs;
            var cols = ce - cs;
            var slice = new float[rows * cols];
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    slice[r * cols + c] = _depths[rs + r, cs + c];
            return new SampledCoverage
            {
                Region = region,
                Metadata = Metadata.GridMetadata,
                Values = new Dictionary<string, float[]> { ["depth"] = slice },
            };
        }
    }

    private sealed class IdentityFactory : ICrsTransformFactory
    {
        public static readonly IdentityFactory Instance = new();
        public ICrsTransform Create(string sourceCrs, string targetCrs) => IdentityCrsTransform.Instance;
    }
}
