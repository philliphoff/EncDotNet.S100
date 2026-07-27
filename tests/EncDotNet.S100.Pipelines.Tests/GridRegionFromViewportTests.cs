using EncDotNet.S100.Pipelines.Coverage;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Tests for <see cref="GridRegion.FromViewport"/> — the render-time
/// entry point for viewport-scoped coverage sampling (issue #487).
/// </summary>
public class GridRegionFromViewportTests
{
    // A 1000×1000 EPSG:4326 grid spanning 0.1° square around the equator
    // at 0.0001°/cell. Cell size ≈ 11 m at the equator.
    private static readonly GridMetadata Wgs84Grid = new()
    {
        NumRows = 1000,
        NumColumns = 1000,
        OriginLatitude = 0.0,
        OriginLongitude = 0.0,
        SpacingLatitudinal = 0.0001,
        SpacingLongitudinal = 0.0001,
    };

    [Fact]
    public void ViewportMatchesGridExtent_ProducesFullSubsetWithStrideOne()
    {
        // Viewport exactly covers the grid, resolved at ≥ 1 pixel per cell.
        var viewport = new Viewport
        {
            MinLatitude = 0.0,
            MaxLatitude = 0.1,
            MinLongitude = 0.0,
            MaxLongitude = 0.1,
            WidthPixels = 1000,
            HeightPixels = 1000,
            ScaleDenominator = 50_000,
        };

        var region = GridRegion.FromViewport(viewport, Wgs84Grid);

        Assert.Equal(0, region.RowStart);
        Assert.Equal(1000, region.RowEnd);
        Assert.Equal(0, region.ColStart);
        Assert.Equal(1000, region.ColEnd);
        Assert.Equal(1, region.RowStride);
        Assert.Equal(1, region.ColStride);
    }

    [Fact]
    public void ZoomedInViewport_ProducesSubsetWithStrideOne()
    {
        // A viewport covering the middle 10 % of the grid at ~10 cells per
        // display pixel would over-sample the display — stride stays 1.
        var viewport = new Viewport
        {
            MinLatitude = 0.045,
            MaxLatitude = 0.055,
            MinLongitude = 0.045,
            MaxLongitude = 0.055,
            WidthPixels = 800,
            HeightPixels = 800,
            ScaleDenominator = 5_000,
        };

        var region = GridRegion.FromViewport(viewport, Wgs84Grid);

        // Intersection covers rows 450..550 and cols 450..550 inclusive
        // (viewport lands on cell 550's node at both axes), so the
        // half-open subset is [450, 551) × [450, 551) = 101 × 101 cells.
        // Stride 1 (viewport ground resolution < cell size).
        Assert.Equal(450, region.RowStart);
        Assert.Equal(551, region.RowEnd);
        Assert.Equal(450, region.ColStart);
        Assert.Equal(551, region.ColEnd);
        Assert.Equal(1, region.RowStride);
        Assert.Equal(1, region.ColStride);
    }

    [Fact]
    public void ZoomedOutViewport_ProducesStrideGreaterThanOne()
    {
        // Viewport is 4× wider than the grid but rendered onto only
        // 200 × 200 pixels. Ground resolution ≈ 0.4°/200 = 0.002°/pixel.
        // cellSize = 0.0001°, so cellsPerPixel = 20 → stride = 20.
        var viewport = new Viewport
        {
            MinLatitude = -0.15,
            MaxLatitude = 0.25,
            MinLongitude = -0.15,
            MaxLongitude = 0.25,
            WidthPixels = 200,
            HeightPixels = 200,
            ScaleDenominator = 500_000,
        };

        var region = GridRegion.FromViewport(viewport, Wgs84Grid);

        // Intersection is the full grid.
        Assert.Equal(0, region.RowStart);
        Assert.Equal(1000, region.RowEnd);
        Assert.Equal(0, region.ColStart);
        Assert.Equal(1000, region.ColEnd);
        // Stride is order-of-magnitude larger than 1.
        Assert.Equal(20, region.RowStride);
        Assert.Equal(20, region.ColStride);

        // Effective cells sampled is grid / stride² — an order of
        // magnitude fewer.
        long sampledRows = (region.RowEnd!.Value - region.RowStart!.Value) / region.RowStride;
        long sampledCols = (region.ColEnd!.Value - region.ColStart!.Value) / region.ColStride;
        Assert.True(sampledRows * sampledCols * 10 < 1_000_000,
            "Zoomed-out viewport must sample at least 10× fewer cells.");
    }

    [Fact]
    public void ViewportDisjointFromGrid_ProducesEmptyRegion()
    {
        // Viewport lies entirely north of the grid.
        var viewport = new Viewport
        {
            MinLatitude = 10.0,
            MaxLatitude = 11.0,
            MinLongitude = 10.0,
            MaxLongitude = 11.0,
            WidthPixels = 500,
            HeightPixels = 500,
            ScaleDenominator = 50_000,
        };

        var region = GridRegion.FromViewport(viewport, Wgs84Grid);

        Assert.Equal(region.RowStart, region.RowEnd);
        Assert.Equal(region.ColStart, region.ColEnd);
    }

    [Fact]
    public void ViewportPartiallyOffGrid_ClampsToGridBounds()
    {
        // Viewport hangs off the north-east corner. Bounds must clamp
        // to [0, 1000] rather than reporting rows > 1000.
        var viewport = new Viewport
        {
            MinLatitude = 0.05,
            MaxLatitude = 0.15,
            MinLongitude = 0.05,
            MaxLongitude = 0.15,
            WidthPixels = 500,
            HeightPixels = 500,
            ScaleDenominator = 50_000,
        };

        var region = GridRegion.FromViewport(viewport, Wgs84Grid);

        Assert.Equal(500, region.RowStart);
        Assert.Equal(1000, region.RowEnd);
        Assert.Equal(500, region.ColStart);
        Assert.Equal(1000, region.ColEnd);
    }

    [Fact]
    public void NonWgs84GridWithoutTransform_Throws()
    {
        var viewport = new Viewport
        {
            MinLatitude = 47.0,
            MaxLatitude = 47.1,
            MinLongitude = -122.5,
            MaxLongitude = -122.4,
            WidthPixels = 500,
            HeightPixels = 500,
            ScaleDenominator = 50_000,
        };
        var utmGrid = new GridMetadata
        {
            NumRows = 1000,
            NumColumns = 1000,
            OriginLatitude = 5_200_000,   // northing (metres)
            OriginLongitude = 500_000,    // easting  (metres)
            SpacingLatitudinal = 10,
            SpacingLongitudinal = 10,
        };

        Assert.Throws<ArgumentException>(() =>
            GridRegion.FromViewport(viewport, utmGrid, "EPSG:32610", wgs84ToNative: null));
    }

    [Fact]
    public void NonWgs84GridWithTransform_UsesProjectedBounds()
    {
        // A synthetic "UTM-like" transform: EPSG:4326 (deg) → native
        // metres by multiplying by 100000 on each axis. The grid is a
        // 1000×1000 cell tile of 10-metre cells rooted at native
        // origin (100000, 100000) — i.e. covering
        // (100 000..110 000) × (100 000..110 000) in native metres.
        // A viewport covering (1..1.05) longitude × (1..1.05) latitude
        // maps to native (100 000..105 000)², which is the SW quadrant
        // of the grid.
        var viewport = new Viewport
        {
            MinLatitude = 1.0,
            MaxLatitude = 1.05,
            MinLongitude = 1.0,
            MaxLongitude = 1.05,
            WidthPixels = 500,
            HeightPixels = 500,
            ScaleDenominator = 50_000,
        };
        var utmGrid = new GridMetadata
        {
            NumRows = 1000,
            NumColumns = 1000,
            OriginLatitude = 100_000,
            OriginLongitude = 100_000,
            SpacingLatitudinal = 10,
            SpacingLongitudinal = 10,
        };
        var transform = new ScaleCrsTransform(100_000);

        var region = GridRegion.FromViewport(viewport, utmGrid, "EPSG:32610", transform);

        // Viewport covers native (100 000, 100 000) → (105 000, 105 000)
        // → cells 0..500 inclusive, half-open [0, 501).
        Assert.Equal(0, region.RowStart);
        Assert.Equal(501, region.RowEnd);
        Assert.Equal(0, region.ColStart);
        Assert.Equal(501, region.ColEnd);
    }

    [Fact]
    public void SampledSubsetOriginMatchesGeoreferencer()
    {
        // Correctness prerequisite for the pipeline change: the georeferencer
        // built from sampled.Metadata must locate cell (0,0) at the true geo
        // position of the subset's first cell.
        var viewport = new Viewport
        {
            MinLatitude = 0.025,
            MaxLatitude = 0.075,
            MinLongitude = 0.025,
            MaxLongitude = 0.075,
            WidthPixels = 500,
            HeightPixels = 500,
            ScaleDenominator = 50_000,
        };

        var region = GridRegion.FromViewport(viewport, Wgs84Grid);

        // Simulate the source's subset-adjusted metadata (S102CoverageSource
        // does this in-line; we replicate the arithmetic here).
        var sampledMetadata = new GridMetadata
        {
            NumRows = (region.RowEnd!.Value - region.RowStart!.Value) / region.RowStride,
            NumColumns = (region.ColEnd!.Value - region.ColStart!.Value) / region.ColStride,
            OriginLatitude = Wgs84Grid.OriginLatitude + region.RowStart!.Value * Wgs84Grid.SpacingLatitudinal,
            OriginLongitude = Wgs84Grid.OriginLongitude + region.ColStart!.Value * Wgs84Grid.SpacingLongitudinal,
            SpacingLatitudinal = Wgs84Grid.SpacingLatitudinal * region.RowStride,
            SpacingLongitudinal = Wgs84Grid.SpacingLongitudinal * region.ColStride,
        };
        var georef = new GridGeoreferencer(sampledMetadata, "EPSG:4326");

        // Cell (0, 0) of the sampled coverage must sit at the geographic
        // position of the source grid's cell (RowStart, ColStart).
        var (x, y) = georef.ToNative(0, 0);
        Assert.Equal(Wgs84Grid.OriginLongitude + region.ColStart!.Value * Wgs84Grid.SpacingLongitudinal, x, 9);
        Assert.Equal(Wgs84Grid.OriginLatitude + region.RowStart!.Value * Wgs84Grid.SpacingLatitudinal, y, 9);
    }

    // A minimal ICrsTransform that scales each axis by a constant — enough
    // to prove FromViewport projects viewport corners into the grid's
    // native CRS before intersecting.
    private sealed class ScaleCrsTransform : ICrsTransform
    {
        private readonly double _scale;
        public ScaleCrsTransform(double scale) => _scale = scale;
        public (double X, double Y) Transform(double x, double y) => (x * _scale, y * _scale);
        public bool IsIdentity => false;
    }
}
