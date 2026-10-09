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
/// Covers the XYZ tile path (issue #847): the Mapsui-free
/// <see cref="XyzTileGrid"/> math, block planning, and that
/// <see cref="HeadlessTileRenderer"/> paints tiles that join without seams,
/// honour scale visibility per zoom, and pick up data stored one world east.
/// </summary>
public class HeadlessTileRendererTests
{
    private static readonly HeadlessCompositeOptions TileCompositeOptions = new()
    {
        HonorScaleVisibility = true,
        EnableSeamWrap = false,
    };

    [Fact]
    public void Grid_bounds_span_the_world_at_zoom_zero()
    {
        var (minX, minY, maxX, maxY) = XyzTileGrid.TileWorldBounds(0, 0, 0);

        Assert.Equal(-XyzTileGrid.Extent, minX, 6);
        Assert.Equal(-XyzTileGrid.Extent, minY, 6);
        Assert.Equal(XyzTileGrid.Extent, maxX, 6);
        Assert.Equal(XyzTileGrid.Extent, maxY, 6);
        Assert.Equal(156543.034, XyzTileGrid.Resolution(0), 3);
    }

    [Fact]
    public void Grid_columns_wrap_an_extent_that_crosses_the_antimeridian()
    {
        var (minX, _) = WebMercator.FromLonLat(170, 0);
        var (maxX, _) = WebMercator.FromLonLat(190, 0);

        Assert.Equal([0, 3], XyzTileGrid.Columns(2, minX, maxX));
    }

    [Fact]
    public void Grid_columns_cover_every_column_for_a_world_wide_extent()
    {
        Assert.Equal([0, 1, 2, 3], XyzTileGrid.Columns(2, -XyzTileGrid.Extent * 1.5, XyzTileGrid.Extent * 1.5));
    }

    [Fact]
    public void Grid_columns_and_rows_exclude_a_tile_the_extent_only_touches()
    {
        double size = XyzTileGrid.TileWorldSize(3);
        double edgeX = -XyzTileGrid.Extent + 2 * size;
        double edgeY = XyzTileGrid.Extent - 5 * size;

        Assert.Equal([1], XyzTileGrid.Columns(3, edgeX - size / 2, edgeX));
        Assert.Equal((4, 4), XyzTileGrid.Rows(3, edgeY, edgeY + size / 2));
    }

    [Fact]
    public void Grid_rows_clamp_to_the_grid()
    {
        Assert.Equal((0, 7), XyzTileGrid.Rows(3, -XyzTileGrid.Extent * 2, XyzTileGrid.Extent * 2));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(50.0)]
    public void Grid_zoom_for_scale_round_trips_the_zoom_scale(double latitude)
    {
        for (int zoom = 0; zoom <= 20; zoom++)
        {
            double denominator = XyzTileGrid.ScaleDenominator(zoom, latitude);
            Assert.Equal(zoom, XyzTileGrid.ZoomForScaleDenominator(denominator, latitude));
        }

        // One zoom level halves the denominator; latitude shrinks it by cos.
        Assert.Equal(
            XyzTileGrid.ScaleDenominator(10, 0) * Math.Cos(60 * Math.PI / 180),
            XyzTileGrid.ScaleDenominator(10, 60), 6);
    }

    [Fact]
    public void PlanBlocks_aligns_blocks_to_the_block_grid_and_covers_every_tile_once()
    {
        var blocks = HeadlessTileRenderer.PlanBlocks(5, [3, 4, 5, 6, 7, 8, 9], 1, 2, 4);

        Assert.Equal(
            [new TileBlock(5, 3, 1, 1, 2), new TileBlock(5, 4, 1, 4, 2), new TileBlock(5, 8, 1, 2, 2)],
            blocks);
        Assert.Equal(14, blocks.Sum(b => b.TileCount));
    }

    [Fact]
    public void PlanBlocks_splits_wrapped_columns_into_separate_runs()
    {
        var blocks = HeadlessTileRenderer.PlanBlocks(3, [0, 1, 6, 7], 0, 0, 8);

        Assert.Equal([new TileBlock(3, 0, 0, 2, 1), new TileBlock(3, 6, 0, 2, 1)], blocks);
    }

    [Fact]
    public void Block_render_matches_single_tile_renders_at_the_shared_edges()
    {
        // A triangle whose slanted edges cross the tile boundaries.
        var scene = Prepare(Area("tri.000", "#2060C0", [(-0.9, -0.8), (0.95, -0.1), (-0.2, 0.9)]));
        var renderer = new HeadlessTileRenderer(scene);
        const int zoom = 8;
        int center = XyzTileGrid.TilesPerAxis(zoom) / 2;

        var block = renderer.Render(new TileBlock(zoom, center - 1, center - 1, 2, 2));
        try
        {
            Assert.Equal(4, block.Count);
            foreach (var tile in block)
            {
                var single = Assert.Single(renderer.Render(new TileBlock(zoom, tile.X, tile.Y, 1, 1)));
                using (single)
                {
                    // Skia's anti-aliased coverage of a pixel on a shape's edge
                    // depends a little on the canvas size (about a twentieth of
                    // a pixel), so some edge pixels differ; every pixel a shape
                    // covers fully or not at all is identical bar rounding.
                    var expected = tile.Bitmap.Pixels;
                    var actual = single.Bitmap.Pixels;
                    Assert.Equal(expected.Length, actual.Length);
                    int edgeDifferences = 0;
                    for (int i = 0; i < expected.Length; i++)
                    {
                        var (e, a) = (expected[i], actual[i]);
                        int difference = new[]
                        {
                            Math.Abs(e.Red - a.Red), Math.Abs(e.Green - a.Green),
                            Math.Abs(e.Blue - a.Blue), Math.Abs(e.Alpha - a.Alpha),
                        }.Max();
                        if (difference <= 2)
                            continue;

                        Assert.True(e.Alpha is > 0 and < 255 || a.Alpha is > 0 and < 255, "Only edge pixels may differ.");
                        edgeDifferences++;
                    }

                    Assert.True(edgeDifferences < expected.Length / 100, $"{edgeDifferences} edge pixels differ.");
                    Assert.Contains(actual, p => p.Alpha == 255);
                }
            }
        }
        finally
        {
            foreach (var tile in block)
                tile.Dispose();
        }
    }

    [Fact]
    public void Render_marks_tiles_with_nothing_drawn_as_empty()
    {
        var scene = Prepare(Area("a.000", "#FF0000", Box(10, 40, 10, 40)));
        var renderer = new HeadlessTileRenderer(scene);

        // At zoom 2 the box lies well inside tile (2, 1).
        var tiles = renderer.Render(new TileBlock(2, 1, 1, 2, 2));
        try
        {
            Assert.Equal([true, false, true, true], tiles.Select(t => t.IsEmpty));
            Assert.Equal((2, 1), (tiles[1].X, tiles[1].Y));
        }
        finally
        {
            foreach (var tile in tiles)
                tile.Dispose();
        }
    }

    [Fact]
    public void Render_stops_drawing_a_cell_past_its_minimum_display_scale()
    {
        var scene = Prepare(Area("a.000", "#FF0000", Box(-1, 1, -1, 1), minimumDisplayScale: 2_000_000));
        var renderer = new HeadlessTileRenderer(scene);

        // Zoom 8 at the equator is about 1:2.2 million, zoom 9 about 1:1.1 million.
        Assert.True(renderer.ScaleDenominator(8) > 2_000_000);
        Assert.True(renderer.ScaleDenominator(9) < 2_000_000);
        Assert.True(RenderCenterTile(renderer, 8).IsEmpty);
        Assert.False(RenderCenterTile(renderer, 9).IsEmpty);
    }

    [Fact]
    public void Render_draws_data_stored_east_of_the_antimeridian_in_the_wrapped_tiles()
    {
        // A box kept in a 0…360° frame, at 181°–183°: it belongs in the tiles
        // just east of -180°.
        var scene = Prepare(Area("east.000", "#00A000", Box(181, 183, -1, 1)));
        var renderer = new HeadlessTileRenderer(scene);
        const int zoom = 5;
        int row = XyzTileGrid.TilesPerAxis(zoom) / 2;

        var tile = Assert.Single(renderer.Render(new TileBlock(zoom, 0, row, 1, 1)));
        using (tile)
        {
            Assert.False(tile.IsEmpty);
        }
    }

    [Fact]
    public void Render_with_pixel_ratio_two_doubles_the_tile_image()
    {
        var scene = Prepare(Area("a.000", "#FF0000", Box(-1, 1, -1, 1)));
        var renderer = new HeadlessTileRenderer(scene, new HeadlessTileOptions { PixelRatio = 2 });

        using var tile = RenderCenterTile(renderer, 9);
        Assert.Equal(512, renderer.TilePixelSize);
        Assert.Equal(512, tile.Bitmap.Width);
        Assert.Equal(new SKColor(0xFF, 0x00, 0x00), tile.Bitmap.GetPixel(10, 500));
    }

    private static HeadlessTile RenderCenterTile(HeadlessTileRenderer renderer, int zoom)
    {
        // The tile just north-east of (0°, 0°).
        int center = XyzTileGrid.TilesPerAxis(zoom) / 2;
        return Assert.Single(renderer.Render(new TileBlock(zoom, center, center - 1, 1, 1)));
    }

    private static HeadlessCompositeScene Prepare(HeadlessCompositeInput input) =>
        new HeadlessCompositor(new ProjNetCrsTransformFactory()).Prepare([input], TileCompositeOptions);

    private static (double Lon, double Lat)[] Box(double west, double east, double south, double north) =>
        [(west, south), (east, south), (east, north), (west, north)];

    private static HeadlessCompositeInput Area(
        string datasetId, string fillHex, (double Lon, double Lat)[] ring, int? minimumDisplayScale = null)
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
            GeometryProvider = new RingGeometryProvider(featureRef, ring),
            Product = "S-101",
            Spec = new SpecRef("S-101", default),
            SourceDatasetId = datasetId,
            Info = "test",
            CellMinimumDisplayScale = minimumDisplayScale,
        };

        return HeadlessCompositeInput.ForVector(result);
    }

    private sealed class RingGeometryProvider(string featureRef, (double Lon, double Lat)[] ring) : IFeatureGeometryProvider
    {
        private readonly FeatureGeometry _geometry = new()
        {
            Type = GeometryType.Surface,
            Coordinates = [.. ring.Select(p => new GeoPosition(p.Lat, p.Lon)), new GeoPosition(ring[0].Lat, ring[0].Lon)],
        };

        public FeatureGeometry? GetGeometry(string featureReference) =>
            featureReference == featureRef ? _geometry : null;
    }
}
