using EncDotNet.S100.Core;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Pipelines.Vector;
using SkiaSharp;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Exercises the Mapsui-free <see cref="HeadlessCompositor"/> end-to-end: the
/// renderer-neutral S-98 ordering engine drives the cross-dataset paint order,
/// the shared viewport is honoured (explicit) or fitted to the union extent
/// (default), and inactive datasets are excluded from painting. Ordering is
/// pinned via pixel inspection of overlapping solid area fills on different
/// S-98 display planes (S-98 Annex A §A-6.9.1).
/// </summary>
public class HeadlessCompositorTests
{
    private static HeadlessCompositor NewCompositor()
        => new(new ProjNetCrsTransformFactory());

    [Fact]
    public void Render_honors_explicit_viewport_dimensions()
    {
        var compositor = NewCompositor();
        var dataset = VectorLayer(
            datasetId: "a.000",
            plane: S98DisplayPlane.BaseChartUnder,
            fillHex: "#FF0000",
            west: -1, east: 1, south: -1, north: 1);

        var viewport = new Viewport
        {
            MinLongitude = -2,
            MaxLongitude = 2,
            MinLatitude = -2,
            MaxLatitude = 2,
            WidthPixels = 200,
            HeightPixels = 150,
            ScaleDenominator = 50_000,
        };

        using var bitmap = compositor.Render(
            new[] { dataset },
            new HeadlessCompositeOptions { Viewport = viewport });

        Assert.Equal(200, bitmap.Width);
        Assert.Equal(150, bitmap.Height);
    }

    [Fact]
    public void Render_fits_union_viewport_to_option_dimensions_and_paints_the_layer()
    {
        var compositor = NewCompositor();
        var dataset = VectorLayer(
            datasetId: "a.000",
            plane: S98DisplayPlane.BaseChartUnder,
            fillHex: "#FF0000",
            west: -1, east: 1, south: -1, north: 1);

        using var bitmap = compositor.Render(
            new[] { dataset },
            new HeadlessCompositeOptions { Width = 256, Height = 256 });

        Assert.Equal(256, bitmap.Width);
        Assert.Equal(256, bitmap.Height);

        // The single red area sits at the centre of its own fitted extent.
        var center = bitmap.GetPixel(128, 128);
        Assert.Equal(new SKColor(0xFF, 0x00, 0x00), center);
    }

    [Fact]
    public void Render_paints_higher_plane_over_lower_plane_regardless_of_input_order()
    {
        var compositor = NewCompositor();

        // Two fully-overlapping areas. Input order lists the OVER plane first
        // to prove the S-98 sort — not the input order — decides painting.
        var over = VectorLayer(
            datasetId: "over.000",
            plane: S98DisplayPlane.BaseChartOver,
            fillHex: "#0000FF",
            west: -1, east: 1, south: -1, north: 1);
        var under = VectorLayer(
            datasetId: "under.000",
            plane: S98DisplayPlane.BaseChartUnder,
            fillHex: "#FF0000",
            west: -1, east: 1, south: -1, north: 1);

        var viewport = SquareViewport(-1, 1, -1, 1, 128);

        using var bitmap = compositor.Render(
            new[] { over, under },
            new HeadlessCompositeOptions { Viewport = viewport });

        // BaseChartOver (blue) draws after BaseChartUnder (red).
        var center = bitmap.GetPixel(64, 64);
        Assert.Equal(new SKColor(0x00, 0x00, 0xFF), center);
    }

    [Fact]
    public void Render_excludes_inactive_datasets_from_painting()
    {
        var compositor = NewCompositor();
        var dataset = VectorLayer(
            datasetId: "a.000",
            plane: S98DisplayPlane.BaseChartUnder,
            fillHex: "#FF0000",
            west: -1, east: 1, south: -1, north: 1,
            active: false);

        var viewport = SquareViewport(-1, 1, -1, 1, 64);

        using var bitmap = compositor.Render(
            new[] { dataset },
            new HeadlessCompositeOptions
            {
                Viewport = viewport,
                Background = new RgbaColor(255, 255, 255, 255),
            });

        // Nothing active to paint — the whole frame is the background.
        var center = bitmap.GetPixel(32, 32);
        Assert.Equal(new SKColor(0xFF, 0xFF, 0xFF), center);
    }

    [Fact]
    public void Render_fits_union_across_antimeridian_without_collapsing()
    {
        // Two datasets on opposite sides of the ±180° seam (near +179° and
        // −179°). A naive min/max union would frame a near-global extent and
        // render blank; the seam-aware union auto-fit must frame the true
        // (~4°) extent so both layers paint (issue #413).
        var compositor = NewCompositor();
        var east = VectorLayer(
            datasetId: "east.000",
            plane: S98DisplayPlane.BaseChartUnder,
            fillHex: "#FF0000",
            west: 178.0, east: 179.5, south: 64.0, north: 66.0);
        var west = VectorLayer(
            datasetId: "west.000",
            plane: S98DisplayPlane.BaseChartUnder,
            fillHex: "#FF0000",
            west: -179.5, east: -178.0, south: 64.0, north: 66.0);

        using var bitmap = compositor.Render(
            new[] { east, west },
            new HeadlessCompositeOptions
            {
                Width = 400,
                Height = 400,
                Background = new RgbaColor(255, 255, 255, 255),
            });

        // Both clusters must paint — one on each half of the canvas — proving
        // the union fit did not collapse to a near-global viewport.
        Assert.True(HasRedPixel(bitmap, 0, bitmap.Width / 2),
            "Expected a painted feature in the left half of the composite.");
        Assert.True(HasRedPixel(bitmap, bitmap.Width / 2, bitmap.Width),
            "Expected a painted feature in the right half of the composite.");
    }

    [Fact]
    public void Render_places_projected_coverage_from_sampled_native_bounds()
    {
        var compositor = new HeadlessCompositor(new ScaledCrsTransformFactory());
        var coverage = ProjectedCoverageSubset();
        var viewport = SquareViewport(0, 2, 0, 2, 200);

        using var bitmap = compositor.Render(
            [coverage],
            new HeadlessCompositeOptions
            {
                Viewport = viewport,
                Background = new RgbaColor(255, 255, 255, 255),
            });

        Assert.Equal(new SKColor(0xFF, 0xFF, 0xFF), bitmap.GetPixel(20, 100));
        Assert.Equal(new SKColor(0xFF, 0x00, 0x00), bitmap.GetPixel(105, 95));
    }

    // ----------------------------------------------------------------
    // Issue #483 — S-104 water-level surface clipped to water areas.
    // End-to-end proof through the production compositor: the S-98 rule
    // R-101-104-B attaches the ENC's LandArea geometry to the S-104
    // surface, and TryLowerCoverage rasterises the per-cell land mask so
    // land cells paint transparent — revealing the ENC land beneath.
    // ----------------------------------------------------------------

    [Fact]
    public void Render_clips_S104_surface_to_water_when_S101_land_is_present()
    {
        var compositor = NewCompositor();

        // 8×4 EPSG:4326 grid, all cells one value → a solid red surface.
        var surface = S104Surface();
        // ENC land covering the WEST half (native lon < 3.5), painted green.
        var land = S101Land("#00FF00", west: -1.0, east: 3.5, south: -1.0, north: 5.0);

        var viewport = new Viewport
        {
            MinLongitude = 0,
            MaxLongitude = 8,
            MinLatitude = 0,
            MaxLatitude = 4,
            WidthPixels = 160,
            HeightPixels = 80,
            ScaleDenominator = 50_000,
        };

        using var composite = compositor.Render(
            new[] { land, surface },
            new HeadlessCompositeOptions
            {
                Viewport = viewport,
                Background = new RgbaColor(255, 255, 255, 255),
            });

        // West half (over land): the surface is masked away, so the green
        // ENC land shows through — proving the S-104 surface was clipped.
        var westPixel = composite.GetPixel(30, 40);
        Assert.Equal(new SKColor(0x00, 0xFF, 0x00), westPixel);

        // East half (over water): the red surface paints normally.
        var eastPixel = composite.GetPixel(130, 40);
        Assert.Equal(new SKColor(0xFF, 0x00, 0x00), eastPixel);

        // Control: with no ENC alongside it, the SAME west cell paints the
        // red surface — confirming the mask (not some other effect) removed
        // it above.
        using var surfaceOnly = compositor.Render(
            new[] { S104Surface() },
            new HeadlessCompositeOptions
            {
                Viewport = viewport,
                Background = new RgbaColor(255, 255, 255, 255),
            });
        Assert.Equal(new SKColor(0xFF, 0x00, 0x00), surfaceOnly.GetPixel(30, 40));
    }

    private static bool HasRedPixel(SKBitmap bitmap, int xStart, int xEnd)
    {
        for (int y = 0; y < bitmap.Height; y++)
            for (int x = xStart; x < xEnd; x++)
            {
                var p = bitmap.GetPixel(x, y);
                if (p.Red > 200 && p.Green < 80 && p.Blue < 80)
                    return true;
            }
        return false;
    }

    // ----------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------

    private static Viewport SquareViewport(double west, double east, double south, double north, int size)
        => new()
        {
            MinLongitude = west,
            MaxLongitude = east,
            MinLatitude = south,
            MaxLatitude = north,
            WidthPixels = size,
            HeightPixels = size,
            ScaleDenominator = 50_000,
        };

    /// <summary>
    /// Builds a single-sub-layer vector composite input whose one feature is a
    /// solid-filled rectangle spanning the given WGS84 box.
    /// </summary>
    private static HeadlessCompositeInput VectorLayer(
        string datasetId,
        S98DisplayPlane plane,
        string fillHex,
        double west, double east, double south, double north,
        bool active = true)
    {
        const string token = "FILL";
        const string featureRef = "1";

        var area = new AreaInstruction
        {
            FeatureReference = featureRef,
            FillColor = token,
        };

        var sub = new VectorSubLayer
        {
            LayerKey = datasetId + ".area",
            LayerName = datasetId,
            Instructions = new DrawingInstruction[] { area },
            Plane = plane,
            SourceFeatureType = "area",
        };

        var geometry = new SingleSurfaceGeometryProvider(featureRef, west, east, south, north);

        var result = new VectorPortrayalResult
        {
            SubLayers = new[] { sub },
            Palette = new ColorPalette("test", new Dictionary<string, string> { [token] = fillHex }),
            GeometryProvider = geometry,
            Product = "S-101",
            Spec = new SpecRef("S-101", default),
            SourceDatasetId = datasetId,
            Info = "test",
        };

        return HeadlessCompositeInput.ForVector(result, active);
    }

    private sealed class SingleSurfaceGeometryProvider : IFeatureGeometryProvider
    {
        private readonly string _featureRef;
        private readonly FeatureGeometry _geometry;

        public SingleSurfaceGeometryProvider(
            string featureRef, double west, double east, double south, double north)
        {
            _featureRef = featureRef;
            _geometry = new FeatureGeometry
            {
                Type = GeometryType.Surface,
                Coordinates = new[]
                {
                    new GeoPosition(south, west),
                    new GeoPosition(south, east),
                    new GeoPosition(north, east),
                    new GeoPosition(north, west),
                    new GeoPosition(south, west),
                },
            };
        }

        public FeatureGeometry? GetGeometry(string featureReference)
            => string.Equals(featureReference, _featureRef, System.StringComparison.Ordinal)
                ? _geometry
                : null;
    }

    /// <summary>
    /// Builds an S-104 gridded-surface coverage input: an 8×4 EPSG:4326 grid
    /// (native lon 0..7, lat 0..3) whose every cell holds the same value, styled
    /// with a single red band so the whole surface paints solid red.
    /// </summary>
    private static HeadlessCompositeInput S104Surface()
    {
        var metadata = new GridMetadata
        {
            NumRows = 4,
            NumColumns = 8,
            OriginLatitude = 0.0,
            OriginLongitude = 0.0,
            SpacingLatitudinal = 1.0,
            SpacingLongitudinal = 1.0,
        };
        var values = new float[metadata.NumRows * metadata.NumColumns];
        System.Array.Fill(values, 5.0f);
        var sampled = new SampledCoverage
        {
            Region = GridRegion.Full,
            Metadata = metadata,
            Values = new Dictionary<string, float[]> { ["waterLevelHeight"] = values },
        };
        var styled = new StyledCoverageLayer
        {
            Coverage = sampled,
            NoDataValue = float.NaN,
            Georeferencer = new GridGeoreferencer(metadata, "EPSG:4326"),
            ColorScheme = new CoverageColorScheme
            {
                FieldName = "waterLevelHeight",
                Bands = new[]
                {
                    new ColorBand { MinValue = 0f, MaxValue = 10f, Color = "#FF0000" },
                },
            },
        };
        var grid = new GridCoverageSubLayer
        {
            LayerKey = "s104.surface",
            LayerName = "S-104 surface",
            Plane = S98DisplayPlane.OnDemandSurface,
            Coverage = styled,
            Viewport = new Viewport
            {
                MinLatitude = 0.0,
                MaxLatitude = 4.0,
                MinLongitude = 0.0,
                MaxLongitude = 8.0,
                WidthPixels = 8,
                HeightPixels = 4,
                ScaleDenominator = 50_000,
            },
        };
        var result = new CoveragePortrayalResult
        {
            SubLayers = new[] { grid },
            Spec = new SpecRef("S-104", default),
            SourceDatasetId = "s104.h5",
            Info = "test",
        };
        return HeadlessCompositeInput.ForCoverage(result);
    }

    private static HeadlessCompositeInput ProjectedCoverageSubset()
    {
        var metadata = new GridMetadata
        {
            NumRows = 2,
            NumColumns = 2,
            OriginLatitude = 100.0,
            OriginLongitude = 100.0,
            SpacingLatitudinal = 10.0,
            SpacingLongitudinal = 10.0,
        };
        var sampled = new SampledCoverage
        {
            Region = new GridRegion(10, 12, 10, 12, 1, 1),
            Metadata = metadata,
            Values = new Dictionary<string, float[]>
            {
                ["depth"] = [5.0f, 5.0f, 5.0f, 5.0f],
            },
        };
        var styled = new StyledCoverageLayer
        {
            Coverage = sampled,
            NoDataValue = float.NaN,
            Georeferencer = new GridGeoreferencer(metadata, "TEST:PROJECTED"),
            ColorScheme = new CoverageColorScheme
            {
                FieldName = "depth",
                Bands =
                [
                    new ColorBand { MinValue = 0.0f, MaxValue = 10.0f, Color = "#FF0000" },
                ],
            },
        };
        var grid = new GridCoverageSubLayer
        {
            LayerKey = "projected.subset",
            LayerName = "Projected subset",
            Plane = S98DisplayPlane.Bathymetry,
            Coverage = styled,
            // Deliberately unrelated to the sampled grid. The compositor must
            // use the sampled georeferencer metadata for spatial placement.
            Viewport = SquareViewport(-50, 50, -50, 50, 100),
        };
        return HeadlessCompositeInput.ForCoverage(
            new CoveragePortrayalResult
            {
                SubLayers = [grid],
                Spec = new SpecRef("S-102", default),
                SourceDatasetId = "projected.h5",
                Info = "test",
            });
    }

    /// <summary>
    /// Builds an S-101 vector input with a single <c>LandArea</c> surface over
    /// the given WGS84 box, filled with <paramref name="fillHex"/>. The feature
    /// tag + geometry provider are what R-101-104-B reads to derive the land
    /// mask; the fill lets the test see the land beneath the clipped surface.
    /// </summary>
    private static HeadlessCompositeInput S101Land(
        string fillHex, double west, double east, double south, double north)
    {
        const string token = "LNDFILL";
        const string featureRef = "1";

        var sub = new VectorSubLayer
        {
            LayerKey = "s101.areas",
            LayerName = "S-101 (areas)",
            Instructions = new DrawingInstruction[]
            {
                new AreaInstruction { FeatureReference = featureRef, FillColor = token },
            },
            Plane = S98DisplayPlane.BaseChartUnder,
            SourceFeatureType = "area",
        };

        var result = new VectorPortrayalResult
        {
            SubLayers = new[] { sub },
            Palette = new ColorPalette("test", new Dictionary<string, string> { [token] = fillHex }),
            GeometryProvider = new SingleSurfaceGeometryProvider(featureRef, west, east, south, north),
            Product = "S-101",
            Spec = new SpecRef("S-101", default),
            SourceDatasetId = "s101-cell.000",
            Info = "test",
            FeatureTags = new Dictionary<long, VectorFeatureTag> { [1] = new VectorFeatureTag("LandArea", null) },
        };

        return HeadlessCompositeInput.ForVector(result);
    }

    private sealed class ScaledCrsTransformFactory : ICrsTransformFactory
    {
        public ICrsTransform Create(string sourceCrs, string targetCrs) =>
            sourceCrs == targetCrs
                ? IdentityCrsTransform.Instance
                : new ScaledCrsTransform();
    }

    private sealed class ScaledCrsTransform : ICrsTransform
    {
        public bool IsIdentity => false;

        public (double X, double Y) Transform(double x, double y) =>
            (x / 100.0, y / 100.0);
    }
}
