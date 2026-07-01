using System.Collections.Generic;
using System.Globalization;
using EncDotNet.S100.Core;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using SkiaSharp;
using Xunit;

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
                    (south, west),
                    (south, east),
                    (north, east),
                    (north, west),
                    (south, west),
                },
            };
        }

        public FeatureGeometry? GetGeometry(string featureReference)
            => string.Equals(featureReference, _featureRef, System.StringComparison.Ordinal)
                ? _geometry
                : null;
    }
}
