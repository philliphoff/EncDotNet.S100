using EncDotNet.S100.Core;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Rendering.Scene;
using SkiaSharp;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Cross-cell overlap suppression in the headless composite (issue #859): a
/// finer cell hides an overlapping coarser cell under its coverage while the
/// finer cell draws, as the viewer does.
/// </summary>
/// <remarks>
/// The coarse cell fills a red box over ±2°. The fine cell's coverage spans ±1°
/// but it fills only its western half blue, so its eastern half shows whether
/// the coarse red is hidden there. On the 120-pixel ±3° viewport used here, one
/// degree is about 20 pixels and (0°, 0°) is pixel (60, 60).
/// </remarks>
public class HeadlessOverlapSuppressionTests
{
    private static readonly SKColor White = new(255, 255, 255, 255);

    // Inside the fine coverage, where only the coarse cell draws.
    private const int HiddenX = 70;
    // Outside the fine coverage, inside the coarse one.
    private const int CoarseOnlyX = 90;
    // Where the fine cell draws.
    private const int FineX = 50;
    private const int Row = 60;

    [Fact]
    public void Fine_cell_hides_the_coarse_cell_under_its_coverage()
    {
        using var bitmap = Render([Coarse(), Fine()], new HeadlessCompositeOptions { Viewport = Viewport(20_000) });

        Assert.Equal(White, bitmap.GetPixel(HiddenX, Row));
        Assert.True(IsRed(bitmap.GetPixel(CoarseOnlyX, Row)));
        Assert.True(IsBlue(bitmap.GetPixel(FineX, Row)));
    }

    [Fact]
    public void Draw_order_does_not_matter()
    {
        using var bitmap = Render([Fine(), Coarse()], new HeadlessCompositeOptions { Viewport = Viewport(20_000) });

        Assert.Equal(White, bitmap.GetPixel(HiddenX, Row));
        Assert.True(IsRed(bitmap.GetPixel(CoarseOnlyX, Row)));
    }

    [Fact]
    public void Rotated_display_hides_the_coarse_cell_too()
    {
        // Turned half a revolution, east lands west of the centre.
        var viewport = Viewport(20_000) with { RotationDegrees = 180 };
        using var bitmap = Render([Coarse(), Fine()], new HeadlessCompositeOptions { Viewport = viewport });

        Assert.Equal(White, bitmap.GetPixel(120 - HiddenX, Row));
        Assert.True(IsRed(bitmap.GetPixel(120 - CoarseOnlyX, Row)));
        Assert.True(IsBlue(bitmap.GetPixel(120 - FineX, Row)));
    }

    [Fact]
    public void With_scale_visibility_the_fine_cell_hides_only_while_it_draws()
    {
        var inScale = new HeadlessCompositeOptions { Viewport = Viewport(20_000), HonorScaleVisibility = true };
        using (var bitmap = Render([Coarse(), Fine()], inScale))
            Assert.Equal(White, bitmap.GetPixel(HiddenX, Row));

        // Past the fine cell's 1:50,000 minimum display scale it draws nothing,
        // so the coarse cell shows through again.
        var pastFine = new HeadlessCompositeOptions { Viewport = Viewport(100_000), HonorScaleVisibility = true };
        using (var bitmap = Render([Coarse(), Fine()], pastFine))
        {
            Assert.True(IsRed(bitmap.GetPixel(HiddenX, Row)));
            Assert.True(IsRed(bitmap.GetPixel(FineX, Row)));
        }
    }

    [Fact]
    public void Ignoring_scale_minima_hides_nothing()
    {
        var options = new HeadlessCompositeOptions
        {
            Viewport = Viewport(20_000),
            Mariner = MarinerSettings.Default with { IgnoreScaleMinimum = true },
        };
        using var bitmap = Render([Coarse(), Fine()], options);

        Assert.True(IsRed(bitmap.GetPixel(HiddenX, Row)));
    }

    [Fact]
    public void Cells_of_the_same_scale_do_not_hide_each_other()
    {
        using var bitmap = Render(
            [Coarse(), Fine(compilationScale: 100_000)],
            new HeadlessCompositeOptions { Viewport = Viewport(20_000) });

        Assert.True(IsRed(bitmap.GetPixel(HiddenX, Row)));
    }

    [Fact]
    public void Inactive_fine_cell_hides_nothing()
    {
        using var bitmap = Render([Coarse(), Fine(active: false)], new HeadlessCompositeOptions { Viewport = Viewport(20_000) });

        Assert.True(IsRed(bitmap.GetPixel(HiddenX, Row)));
    }

    [Fact]
    public void Coarse_cell_shows_through_a_hole_in_the_fine_coverage()
    {
        // A hole at 0.25°..0.75° E, ±0.25° N.
        IReadOnlyList<GeoPosition> hole = [new(-0.25, 0.25), new(-0.25, 0.75), new(0.25, 0.75), new(0.25, 0.25), new(-0.25, 0.25)];
        using var bitmap = Render([Coarse(), Fine(hole: hole)], new HeadlessCompositeOptions { Viewport = Viewport(20_000) });

        Assert.True(IsRed(bitmap.GetPixel(HiddenX, Row)));
        // Half a degree north of the hole is still hidden.
        Assert.Equal(White, bitmap.GetPixel(HiddenX, Row - 10));
    }

    [Fact]
    public void Tiles_wholly_under_the_fine_coverage_are_empty()
    {
        // At zoom 9 a tile is about 0.7° wide. Neither cell has a minimum
        // display scale here, so both draw at every zoom.
        var compositor = new HeadlessCompositor(new ProjNetCrsTransformFactory());
        var scene = compositor.Prepare(
            [Coarse(minimumDisplayScale: null), Fine(minimumDisplayScale: null)],
            new HeadlessCompositeOptions { HonorScaleVisibility = true, EnableSeamWrap = false });
        var renderer = new HeadlessTileRenderer(scene);

        var tiles = renderer.Render(new TileBlock(9, 256, 256, 3, 1));
        try
        {
            // 0°..0.7° E: inside the fine coverage, where it draws nothing.
            Assert.True(tiles[0].IsEmpty);
            // 0.7°..1.4° E: across the fine coverage's eastern edge.
            Assert.False(tiles[1].IsEmpty);
            Assert.Equal(0, tiles[1].Bitmap.GetPixel(10, 128).Alpha);
            Assert.True(IsRed(tiles[1].Bitmap.GetPixel(250, 128)));
            // 1.4°..2.1° E: the coarse cell only.
            Assert.True(IsRed(tiles[2].Bitmap.GetPixel(10, 128)));
        }
        finally
        {
            foreach (var tile in tiles)
                tile.Dispose();
        }
    }

    [Fact]
    public void Coverage_converts_to_one_web_mercator_footprint()
    {
        Assert.Null(CoverageOverlap.ToWebMercator([]));
        Assert.Null(CoverageOverlap.ToWebMercator([new CoverageArea { ExteriorRing = [new(0, 0), new(0, 1)] }]));

        var footprint = CoverageOverlap.ToWebMercator([Area(-1, 1, -1, 1), Area(0, 2, -1, 1)]);

        Assert.NotNull(footprint);
        Assert.True(footprint.IsValid);
        var (minX, minY) = WebMercator.FromLonLat(-1, -1);
        var (maxX, maxY) = WebMercator.FromLonLat(2, 1);
        Assert.Equal(minX, footprint.EnvelopeInternal.MinX, 6);
        Assert.Equal(minY, footprint.EnvelopeInternal.MinY, 6);
        Assert.Equal(maxX, footprint.EnvelopeInternal.MaxX, 6);
        Assert.Equal(maxY, footprint.EnvelopeInternal.MaxY, 6);
    }

    // ----------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------

    private static SKBitmap Render(IReadOnlyList<HeadlessCompositeInput> inputs, HeadlessCompositeOptions options) =>
        new HeadlessCompositor(new ProjNetCrsTransformFactory()).Render(inputs, options);

    private static bool IsRed(SKColor c) => c.Red > 200 && c.Green < 80 && c.Blue < 80 && c.Alpha == 255;

    private static bool IsBlue(SKColor c) => c.Blue > 200 && c.Red < 80 && c.Green < 80;

    private static Viewport Viewport(double scaleDenominator) => new()
    {
        MinLongitude = -3,
        MaxLongitude = 3,
        MinLatitude = -3,
        MaxLatitude = 3,
        WidthPixels = 120,
        HeightPixels = 120,
        ScaleDenominator = scaleDenominator,
    };

    private static HeadlessCompositeInput Coarse(int? minimumDisplayScale = 200_000) => Cell(
        "coarse.000", "#FF0000", fill: (-2, 2, -2, 2), coverage: Area(-2, 2, -2, 2),
        compilationScale: 100_000, minimumDisplayScale);

    private static HeadlessCompositeInput Fine(
        int compilationScale = 20_000,
        int? minimumDisplayScale = 50_000,
        IReadOnlyList<GeoPosition>? hole = null,
        bool active = true)
    {
        var coverage = Area(-1, 1, -1, 1);
        if (hole is not null)
            coverage = new CoverageArea { ExteriorRing = coverage.ExteriorRing, InteriorRings = [hole] };

        return Cell(
            "fine.000", "#0000FF", fill: (-1, -0.25, -1, 1), coverage,
            compilationScale, minimumDisplayScale, active);
    }

    private static CoverageArea Area(double west, double east, double south, double north) => new()
    {
        ExteriorRing =
        [
            new GeoPosition(south, west),
            new GeoPosition(south, east),
            new GeoPosition(north, east),
            new GeoPosition(north, west),
            new GeoPosition(south, west),
        ],
    };

    /// <summary>
    /// A one-feature S-101-like cell: a solid fill over <paramref name="fill"/>
    /// (west, east, south, north) and the given coverage and scales.
    /// </summary>
    private static HeadlessCompositeInput Cell(
        string datasetId,
        string fillHex,
        (double West, double East, double South, double North) fill,
        CoverageArea coverage,
        int compilationScale,
        int? minimumDisplayScale,
        bool active = true)
    {
        const string token = "FILL";
        const string featureRef = "1";

        var sub = new VectorSubLayer
        {
            LayerKey = datasetId + ".area",
            LayerName = datasetId,
            Instructions = [new AreaInstruction { FeatureReference = featureRef, FillColor = token }],
            Plane = S98DisplayPlane.BaseChartUnder,
            SourceFeatureType = "area",
        };

        var result = new VectorPortrayalResult
        {
            SubLayers = [sub],
            Palette = new ColorPalette("test", new Dictionary<string, string> { [token] = fillHex }),
            GeometryProvider = new SurfaceProvider(featureRef, fill.West, fill.East, fill.South, fill.North),
            Product = "S-101",
            Spec = new SpecRef("S-101", default),
            SourceDatasetId = datasetId,
            Info = "test",
            CellCompilationScale = compilationScale,
            CellMinimumDisplayScale = minimumDisplayScale,
            CoverageAreas = [coverage],
        };

        return HeadlessCompositeInput.ForVector(result, active);
    }

    private sealed class SurfaceProvider(string featureRef, double west, double east, double south, double north)
        : IFeatureGeometryProvider
    {
        private readonly FeatureGeometry _geometry = new()
        {
            Type = GeometryType.Surface,
            Coordinates =
            [
                new GeoPosition(south, west),
                new GeoPosition(south, east),
                new GeoPosition(north, east),
                new GeoPosition(north, west),
                new GeoPosition(south, west),
            ],
        };

        public FeatureGeometry? GetGeometry(string featureReference) =>
            string.Equals(featureReference, featureRef, StringComparison.Ordinal) ? _geometry : null;
    }
}
