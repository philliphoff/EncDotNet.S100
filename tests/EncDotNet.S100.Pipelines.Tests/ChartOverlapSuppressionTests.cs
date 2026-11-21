using System.Diagnostics.Metrics;
using EncDotNet.S100.Core;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Rendering.Engine;
using SkiaSharp;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Cross-cell scale-band overlap suppression in the chart engine's compositor
/// (issues #859, #1279 slice 10): a finer chart cell hides an overlapping
/// coarser one where its normalised NTS coverage footprint reaches and while it
/// is itself drawing, as in the viewer.
/// </summary>
public class ChartOverlapSuppressionTests
{
    private static readonly SKColor Red = new(0xFF, 0x00, 0x00);
    private static readonly SKColor Blue = new(0x00, 0x00, 0xFF);
    private static readonly SKColor White = new(0xFF, 0xFF, 0xFF);

    // A 400×400 viewport over ±2°: 100 px per degree, origin at the top-left.
    private static Viewport ViewportAt(double scaleDenominator) => new()
    {
        MinLongitude = -2,
        MaxLongitude = 2,
        MinLatitude = -2,
        MaxLatitude = 2,
        WidthPixels = 400,
        HeightPixels = 400,
        ScaleDenominator = scaleDenominator,
    };

    // The fine cell's data coverage is the south-west quadrant (-1..0°), but it
    // only fills the southern half of it (-1..-0.5° latitude). Inside its
    // coverage and north of its fill, (-0.5°, -0.25°), the coarse cell's red
    // shows unless suppression clips it away, leaving the white background.
    private static readonly (double Lon, double Lat)[] FineCoverage = Box(-1, 0, -1, 0);
    private static readonly (double Lon, double Lat)[] FineFill = Box(-1, 0, -1, -0.5);
    private static readonly (double Lon, double Lat)[] World = Box(-2, 2, -2, 2);

    [Fact]
    public void Finer_cell_hides_the_coarser_cell_where_its_coverage_reaches()
    {
        var fine = Cell("fine.000", "#0000FF", FineCoverage, FineFill, rank: 10_000);
        var coarse = Cell("coarse.000", "#FF0000", World, World, rank: 100_000);

        using var bitmap = Render([fine, coarse], ViewportAt(5_000));

        Assert.Equal(White, Pixel(bitmap, -0.5, -0.25));
        Assert.Equal(Blue, Pixel(bitmap, -0.5, -0.75));
        Assert.Equal(Red, Pixel(bitmap, 1, 1));
    }

    [Fact]
    public void Finer_cell_stops_hiding_once_it_stops_drawing()
    {
        // Past its 50 000 minimum display scale the fine cell draws nothing, so
        // the coarse cell shows across its coverage instead of a blank hole.
        var fine = Cell("fine.000", "#0000FF", FineCoverage, FineFill, rank: 10_000, minimumDisplayScale: 50_000);
        var coarse = Cell("coarse.000", "#FF0000", World, World, rank: 100_000, minimumDisplayScale: 500_000);

        using var hiding = Render([fine, coarse], ViewportAt(40_000), honorScaleVisibility: true);
        using var showing = Render([fine, coarse], ViewportAt(80_000), honorScaleVisibility: true);

        Assert.Equal(White, Pixel(hiding, -0.5, -0.25));
        Assert.Equal(Red, Pixel(showing, -0.5, -0.25));
        Assert.Equal(Red, Pixel(showing, -0.5, -0.75));
    }

    [Fact]
    public void Coarser_cell_shows_through_a_hole_in_the_finer_coverage()
    {
        var fine = Cell(
            "fine.000", "#0000FF", Box(-1.5, 0, -1.5, 0), Box(-1.5, 0, -1.5, 0), rank: 10_000,
            hole: Box(-1, -0.5, -1, -0.5));
        var coarse = Cell("coarse.000", "#FF0000", World, World, rank: 100_000);

        using var bitmap = Render([fine, coarse], ViewportAt(5_000));

        Assert.Equal(Red, Pixel(bitmap, -0.75, -0.75));
        Assert.Equal(Blue, Pixel(bitmap, -0.25, -0.25));
    }

    [Fact]
    public void Cells_of_the_same_band_do_not_hide_each_other()
    {
        var first = Cell("a.000", "#0000FF", FineCoverage, FineFill, rank: 50_000);
        var second = Cell("b.000", "#FF0000", World, World, rank: 50_000);

        using var bitmap = Render([first, second], ViewportAt(5_000));

        Assert.Equal(Red, Pixel(bitmap, -0.5, -0.25));
    }

    [Fact]
    public void Ignoring_scale_minimum_turns_suppression_off()
    {
        var fine = Cell("fine.000", "#0000FF", FineCoverage, FineFill, rank: 10_000);
        var coarse = Cell("coarse.000", "#FF0000", World, World, rank: 100_000);

        using var bitmap = Render(
            [fine, coarse], ViewportAt(5_000), mariner: new MarinerSettings { IgnoreScaleMinimum = true });

        Assert.Equal(Red, Pixel(bitmap, -0.5, -0.25));
    }

    [Fact]
    public void Inactive_finer_cell_does_not_hide_the_coarser_cell()
    {
        var fine = Cell("fine.000", "#0000FF", FineCoverage, FineFill, rank: 10_000, active: false);
        var coarse = Cell("coarse.000", "#FF0000", World, World, rank: 100_000);

        using var bitmap = Render([fine, coarse], ViewportAt(5_000));

        Assert.Equal(Red, Pixel(bitmap, -0.5, -0.25));
    }

    [Fact]
    public void Suppression_follows_a_rotated_display()
    {
        var fine = Cell("fine.000", "#0000FF", FineCoverage, FineFill, rank: 10_000);
        var coarse = Cell("coarse.000", "#FF0000", World, World, rank: 100_000);

        // Turned 180° about the centre, (-0.5°, -0.25°) lands where (0.5°, 0.25°)
        // would be north-up.
        using var bitmap = Render([fine, coarse], ViewportAt(5_000) with { RotationDegrees = 180 });

        Assert.Equal(White, Pixel(bitmap, 0.5, 0.25));
        Assert.Equal(Red, Pixel(bitmap, -0.5, -0.25));
    }

    [Fact]
    public void Overlapping_coverage_areas_of_one_finer_cell_hide_their_overlap()
    {
        // Two coverage areas that overlap over (-1..0°, -1..0°). Drawn ring by
        // ring with the even-odd rule they would cancel there; the normalised
        // footprint is their union, so the overlap is hidden like the rest.
        var fine = Cell(
            "fine.000", "#0000FF", Box(-1.5, 0, -1.5, 0), Box(-1.5, -1.2, -1.5, -1.2), rank: 10_000,
            extraCoverage: Box(-1, 0.5, -1, 0.5));
        var coarse = Cell("coarse.000", "#FF0000", World, World, rank: 100_000);

        using var bitmap = Render([fine, coarse], ViewportAt(5_000));

        Assert.Equal(White, Pixel(bitmap, -0.5, -0.25));
        Assert.Equal(White, Pixel(bitmap, 0.25, 0.25));
        Assert.Equal(Red, Pixel(bitmap, 1, 1));
    }

    [Fact]
    public void Finer_cell_whose_footprint_misses_the_coarser_cell_does_not_hide_it()
    {
        // The coarse cell's coverage is an L along the south and west edges, so
        // its bounding box is the whole view but its footprint never reaches
        // the fine cell's (0..1°, 0..1°). Its fill spills past its coverage, as
        // symbols and labels can; the fine cell takes no part, so it stays.
        (double Lon, double Lat)[] ell = [(-2, -2), (2, -2), (2, -1), (-1, -1), (-1, 2), (-2, 2)];
        var fine = Cell("fine.000", "#0000FF", Box(0, 1, 0, 1), Box(0, 1, 0, 0.2), rank: 10_000);
        var coarse = Cell("coarse.000", "#FF0000", ell, World, rank: 100_000);

        using var bitmap = Render([fine, coarse], ViewportAt(5_000));

        Assert.Equal(Red, Pixel(bitmap, 0.5, 0.5));
        Assert.Equal(Blue, Pixel(bitmap, 0.5, 0.1));
    }

    [Fact]
    public void Layer_hidden_across_a_whole_view_is_skipped_with_the_same_picture()
    {
        var fine = Cell("fine.000", "#0000FF", FineCoverage, FineFill, rank: 10_000);
        var coarse = Cell("coarse.000", "#FF0000", World, World, rank: 100_000);
        var composition = NewCompositor().Prepare([fine, coarse], new ChartCompositeOptions());

        // Wholly inside the fine coverage and north of its fill.
        var viewport = new Viewport
        {
            MinLongitude = -0.9,
            MaxLongitude = -0.1,
            MinLatitude = -0.4,
            MaxLatitude = -0.1,
            WidthPixels = 80,
            HeightPixels = 30,
            ScaleDenominator = 5_000,
        };

        long skipped = 0;
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Name == "s100.render.chart.layers.hidden.count")
                    l.EnableMeasurementEvents(instrument);
            },
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => Interlocked.Add(ref skipped, value));
        listener.Start();

        using var bitmap = new SKBitmap(80, 30, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            composition.Draw(canvas, viewport, []);
        }

        Assert.True(Interlocked.Read(ref skipped) >= 1);
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
                Assert.Equal(White, bitmap.GetPixel(x, y));
        }
    }

    private static ChartCompositor NewCompositor() =>
        new(new ProjNetCrsTransformFactory(), new InteroperabilityAuthority(ProductRegistry.Default.DisplayPlanes));

    private static SKBitmap Render(
        ChartCompositeInput[] inputs,
        Viewport viewport,
        bool honorScaleVisibility = false,
        MarinerSettings? mariner = null) =>
        NewCompositor().Render(inputs, new ChartCompositeOptions
        {
            Viewport = viewport,
            Background = new RgbaColor(255, 255, 255, 255),
            HonorScaleVisibility = honorScaleVisibility,
            Mariner = mariner,
        });

    /// <summary>The pixel at a longitude and latitude in <see cref="ViewportAt"/>'s frame.</summary>
    private static SKColor Pixel(SKBitmap bitmap, double longitude, double latitude)
    {
        // Near the equator Web Mercator is close enough to linear for a pixel probe
        // well inside a box.
        int x = (int)Math.Round((longitude + 2) * 100);
        int y = (int)Math.Round((2 - latitude) * 100);
        return bitmap.GetPixel(x, y);
    }

    private static (double Lon, double Lat)[] Box(double west, double east, double south, double north) =>
        [(west, south), (east, south), (east, north), (west, north)];

    /// <summary>
    /// A chart cell with one area fill and a declared data coverage, which may
    /// differ, both sharing <paramref name="hole"/> when one is given.
    /// </summary>
    private static ChartCompositeInput Cell(
        string datasetId,
        string fillHex,
        (double Lon, double Lat)[] coverage,
        (double Lon, double Lat)[] fill,
        int rank,
        int? minimumDisplayScale = null,
        (double Lon, double Lat)[]? hole = null,
        bool active = true,
        (double Lon, double Lat)[]? extraCoverage = null)
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

        GeoPosition[][] holes = hole is null ? [] : [Ring(hole)];
        var result = new VectorPortrayalResult
        {
            SubLayers = [sub],
            Palette = new ColorPalette("test", new Dictionary<string, string> { [token] = fillHex }),
            GeometryProvider = new SurfaceProvider(featureRef, Ring(fill), holes),
            Product = "S-101",
            Spec = new SpecRef("S-101", default),
            SourceDatasetId = datasetId,
            Info = "test",
            CellCompilationScale = rank,
            CellMinimumDisplayScale = minimumDisplayScale,
            CoverageAreas = extraCoverage is null
                ? [new CoverageArea { ExteriorRing = Ring(coverage), InteriorRings = holes }]
                : [new CoverageArea { ExteriorRing = Ring(coverage), InteriorRings = holes }, new CoverageArea { ExteriorRing = Ring(extraCoverage), InteriorRings = [] }],
        };

        return ChartCompositeInput.ForVector(result, active);

        static GeoPosition[] Ring((double Lon, double Lat)[] box) =>
            [.. box.Select(p => new GeoPosition(p.Lat, p.Lon)), new GeoPosition(box[0].Lat, box[0].Lon)];
    }

    private sealed class SurfaceProvider(string featureRef, GeoPosition[] shell, GeoPosition[][] holes)
        : IFeatureGeometryProvider
    {
        private readonly FeatureGeometry _geometry = new()
        {
            Type = GeometryType.Surface,
            Coordinates = shell,
            InteriorRings = holes,
        };

        public FeatureGeometry? GetGeometry(string featureReference) =>
            featureReference == featureRef ? _geometry : null;
    }
}
