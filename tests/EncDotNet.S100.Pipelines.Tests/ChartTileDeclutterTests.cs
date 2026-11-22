using EncDotNet.S100.Core;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Renderers.Skia.Scene;
using EncDotNet.S100.Rendering.Engine;
using EncDotNet.S100.Rendering.Scene;
using SkiaSharp;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Covers label declutter on the XYZ tile path (issue #1664): overlapping
/// labels are removed once per zoom level over the whole composition, so tiles,
/// blocks, pixel ratios and layers all agree on which labels are dropped.
/// </summary>
public class ChartTileDeclutterTests
{
    private const int Zoom = 10;

    private static readonly ChartCompositeOptions TileCompositeOptions = new()
    {
        HonorScaleVisibility = true,
        EnableSeamWrap = false,
        KeepAreaSymbolsInView = false,
    };

    /// <summary>The column just east of 0° and the row just north of the equator.</summary>
    private static readonly int EastColumn = XyzTileGrid.TilesPerAxis(Zoom) / 2;

    private static readonly int NorthRow = XyzTileGrid.TilesPerAxis(Zoom) / 2 - 1;

    // Two centred labels anchored on the edge between two tiles (0°), so both
    // straddle it; "high" has the higher drawing priority and wins.
    private static readonly Label Low = new("low", 0.0, 0.2, "LOWER PRIORITY NAME", Priority: 10);
    private static readonly Label High = new("high", 0.0, 0.2, "HIGHER PRIORITY", Priority: 20);

    [Fact]
    public void SuppressedLabels_drops_the_lower_priority_of_two_overlapping_labels()
    {
        var scene = Prepare(Labels("a.000", Low, High));

        var suppressed = SuppressedAt(scene, Zoom);

        Assert.Equal(["LOWER PRIORITY NAME"], suppressed.Select(t => t.Text));
    }

    [Fact]
    public void SuppressedLabels_keeps_labels_that_do_not_overlap()
    {
        // About 0.1° apart: some 70 pixels at zoom 10, beyond the labels' height.
        var scene = Prepare(Labels("a.000", Low, High with { Lat = 0.3 }));

        Assert.Empty(SuppressedAt(scene, Zoom));
    }

    [Fact]
    public void SuppressedLabels_never_lets_one_features_labels_at_one_anchor_suppress_each_other()
    {
        // A composite label: two lines of one feature at one anchor.
        var scene = Prepare(Labels("a.000", Low with { Id = "f" }, High with { Id = "f" }));

        Assert.Empty(SuppressedAt(scene, Zoom));
    }

    [Fact]
    public void SuppressedLabels_ignores_labels_culled_by_scale_visibility()
    {
        // The higher-priority label only shows at 1:100 000 and larger, so at
        // zoom 10 (about 1:550 000) it neither draws nor displaces the other.
        var scene = Prepare(Labels("a.000", Low, High with { ScaleMinimum = 100_000 }));

        Assert.Empty(SuppressedAt(scene, Zoom));
    }

    [Fact]
    public void Tiles_drop_the_same_label_on_both_sides_of_a_tile_edge_whether_blocked_or_single()
    {
        var both = new ChartTileRenderer(Prepare(Labels("a.000", Low, High)));
        var winnerOnly = new ChartTileRenderer(Prepare(Labels("a.000", High)));
        var undecluttered = new ChartTileRenderer(
            Prepare(Labels("a.000", Low, High)), new ChartTileOptions { DeclutterLabels = false });

        // One 2×1 block across the edge, and each tile on its own.
        var block = new TileBlock(Zoom, EastColumn - 1, NorthRow, 2, 1);
        AssertSamePixels(winnerOnly, both, block);
        AssertSamePixels(winnerOnly, both, block with { Columns = 1 });
        AssertSamePixels(winnerOnly, both, block with { X = EastColumn, Columns = 1 });

        // Without declutter the lower-priority label shows through.
        Assert.False(SamePixels(winnerOnly, undecluttered, block));
    }

    [Fact]
    public void Single_tile_renders_match_block_renders()
    {
        var renderer = new ChartTileRenderer(Prepare(Labels("a.000", Low, High)));
        var tiles = renderer.Render(new TileBlock(Zoom, EastColumn - 1, NorthRow, 2, 1));
        try
        {
            foreach (var tile in tiles)
            {
                using var single = Assert.Single(renderer.Render(new TileBlock(Zoom, tile.X, tile.Y, 1, 1)));
                Assert.True(
                    CountDifferences(tile.Bitmap, single.Bitmap, tolerance: 8) == 0,
                    $"Tile ({tile.X}, {tile.Y}) differs between its block and a single render.");
            }
        }
        finally
        {
            foreach (var tile in tiles)
                tile.Dispose();
        }
    }

    [Fact]
    public void Labels_from_different_layers_suppress_each_other()
    {
        var scene = Prepare(Labels("a.000", Low), Labels("b.000", High));

        Assert.Equal(2, scene.Layers.Count);
        var suppressed = Assert.Single(SuppressedAt(scene, Zoom));

        // The label of the layer drawn on top wins.
        var top = Assert.IsType<VectorCompositeLayer>(scene.Layers[^1]);
        Assert.DoesNotContain(suppressed, top.Scene.Ops.OfType<TextPaintOp>());
    }

    [Fact]
    public void Pixel_ratio_two_drops_the_same_labels_as_one()
    {
        var both = Prepare(Labels("a.000", Low, High));
        var winnerOnly = Prepare(Labels("a.000", High));
        var block = new TileBlock(Zoom, EastColumn - 1, NorthRow, 2, 1);
        var hiDpi = new ChartTileOptions { PixelRatio = 2 };

        AssertSamePixels(new ChartTileRenderer(winnerOnly, hiDpi), new ChartTileRenderer(both, hiDpi), block);

        // One set per zoom level, kept by the composition the pixel ratios share.
        Assert.Same(SuppressedAt(both, Zoom), SuppressedAt(both, Zoom));
    }

    [Fact]
    public void RenderView_drops_the_zoom_levels_labels()
    {
        var both = new ChartTileRenderer(Prepare(Labels("a.000", Low, High)));
        var winnerOnly = new ChartTileRenderer(Prepare(Labels("a.000", High)));
        var (minX, minY) = WebMercator.FromLonLat(-0.3, 0.0);
        var (maxX, maxY) = WebMercator.FromLonLat(0.3, 0.4);

        // A print at twice the zoom level's resolution.
        using var expected = winnerOnly.RenderView(Zoom, minX, minY, maxX, maxY, 900, 600);
        using var actual = both.RenderView(Zoom, minX, minY, maxX, maxY, 900, 600);

        Assert.Equal(0, CountDifferences(expected, actual, tolerance: 0));
    }

    private static IReadOnlySet<TextPaintOp> SuppressedAt(ChartComposition scene, int zoom) =>
        scene.SuppressedLabels(XyzTileGrid.ScaleDenominator(zoom, 0), XyzTileGrid.Resolution(zoom));

    private static void AssertSamePixels(ChartTileRenderer expected, ChartTileRenderer actual, TileBlock block) =>
        Assert.True(SamePixels(expected, actual, block), $"Block {block} differs from the expected render.");

    private static bool SamePixels(ChartTileRenderer expected, ChartTileRenderer actual, TileBlock block)
    {
        var expectedTiles = expected.Render(block);
        var actualTiles = actual.Render(block);
        try
        {
            Assert.Contains(actualTiles, t => !t.IsEmpty);
            return expectedTiles.Zip(actualTiles).All(p => CountDifferences(p.First.Bitmap, p.Second.Bitmap, tolerance: 0) == 0);
        }
        finally
        {
            foreach (var tile in expectedTiles.Concat(actualTiles))
                tile.Dispose();
        }
    }

    private static int CountDifferences(SKBitmap expected, SKBitmap actual, int tolerance)
    {
        Assert.Equal((expected.Width, expected.Height), (actual.Width, actual.Height));
        var e = expected.Pixels;
        var a = actual.Pixels;
        int differences = 0;
        for (int i = 0; i < e.Length; i++)
        {
            int difference = Math.Max(
                Math.Max(Math.Abs(e[i].Red - a[i].Red), Math.Abs(e[i].Green - a[i].Green)),
                Math.Max(Math.Abs(e[i].Blue - a[i].Blue), Math.Abs(e[i].Alpha - a[i].Alpha)));
            if (difference > tolerance)
                differences++;
        }

        return differences;
    }

    private static ChartComposition Prepare(params ChartCompositeInput[] inputs) =>
        new ChartCompositor(new ProjNetCrsTransformFactory(), new InteroperabilityAuthority(ProductRegistry.Default.DisplayPlanes))
            .Prepare(inputs, TileCompositeOptions);

    private static ChartCompositeInput Labels(string datasetId, params Label[] labels)
    {
        const string token = "CHBLK";
        var sub = new VectorSubLayer
        {
            LayerKey = datasetId + ".text",
            LayerName = datasetId,
            Instructions =
            [
                .. labels.Select(l => new TextInstruction
                {
                    FeatureReference = l.Id,
                    Text = l.Text,
                    FontSize = 12,
                    FontColor = token,
                    DrawingPriority = l.Priority,
                    ScaleMinimum = l.ScaleMinimum,
                }),
            ],
            Plane = S98DisplayPlane.BaseChartOver,
            SourceFeatureType = "text",
        };

        var result = new VectorPortrayalResult
        {
            SubLayers = [sub],
            Palette = new ColorPalette("test", new Dictionary<string, string> { [token] = "#000000" }),
            GeometryProvider = new PointGeometryProvider(labels),
            Product = "S-101",
            Spec = new SpecRef("S-101", default),
            SourceDatasetId = datasetId,
            Info = "test",
        };

        return ChartCompositeInput.ForVector(result);
    }

    private sealed record Label(string Id, double Lon, double Lat, string Text, int Priority, double? ScaleMinimum = null);

    private sealed class PointGeometryProvider(IReadOnlyList<Label> labels) : IFeatureGeometryProvider
    {
        public FeatureGeometry? GetGeometry(string featureReference) =>
            labels.FirstOrDefault(l => l.Id == featureReference) is { } label
                ? new FeatureGeometry { Type = GeometryType.Point, Coordinates = [new GeoPosition(label.Lat, label.Lon)] }
                : null;
    }
}
