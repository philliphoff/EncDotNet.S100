using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Renderers.Skia.Scene;
using EncDotNet.S100.Rendering.Scene;
using SkiaSharp;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Verifies the bundled Natural Earth land basemap (issues #411, #731): the shared
/// <see cref="NaturalEarthBasemap"/> source decodes to tiled levels of detail
/// that keep the full Natural Earth coastline, lowers to a non-empty parchment
/// <see cref="VectorScene"/>, and the <see cref="HeadlessCompositor"/> honours
/// <see cref="BasemapKind.Offline"/> by painting land beneath the chart so the
/// output differs from <see cref="BasemapKind.None"/>.
/// </summary>
public sealed class NaturalEarthBasemapTests
{
    // A viewport wholly over solid land (the Sahara) so the centre pixel is
    // land in the Natural Earth 1:10m set.
    private static readonly Viewport SaharaViewport = new()
    {
        MinLongitude = 10,
        MaxLongitude = 30,
        MinLatitude = 15,
        MaxLatitude = 30,
        WidthPixels = 128,
        HeightPixels = 128,
        ScaleDenominator = 20_000_000,
    };

    [Fact]
    public void LandScene_is_nonempty_parchment_areas()
    {
        var scene = NaturalEarthBasemap.GetLandScene(SaharaViewport);

        Assert.NotEmpty(scene.Ops);
        Assert.All(scene.Ops, op =>
        {
            var area = Assert.IsType<AreaPaintOp>(op);
            Assert.Equal(NaturalEarthBasemap.LandFill, area.Fill);
            Assert.NotEmpty(area.WorldShell);
        });
    }

    [Fact]
    public void Levels_run_from_coarse_to_full_resolution()
    {
        var levels = NaturalEarthBasemap.Levels;

        Assert.True(levels.Count > 1);
        for (int i = 1; i < levels.Count; i++)
            Assert.True(levels[i].Tolerance < levels[i - 1].Tolerance);
        Assert.Equal(0, levels[^1].Tolerance);
    }

    [Fact]
    public void SelectLevel_keeps_the_error_within_a_pixel()
    {
        var levels = NaturalEarthBasemap.Levels;

        // A world view gets the coarsest level; a harbour view the full one.
        Assert.Same(levels[0], NaturalEarthBasemap.SelectLevel(100_000));
        Assert.Same(levels[^1], NaturalEarthBasemap.SelectLevel(5));

        foreach (var metresPerPixel in new[] { 50.0, 400, 1_000, 3_000, 10_000 })
            Assert.True(NaturalEarthBasemap.SelectLevel(metresPerPixel)!.Tolerance <= metresPerPixel);
    }

    [Fact]
    public void Full_level_keeps_the_natural_earth_coastline_detail()
    {
        // Puget Sound (issue #731): the old bundled copy, simplified to ~8%,
        // had ~110 points in this 1°×1° box; full Natural Earth has ~490.
        var (minX, minY) = WebMercator.FromLonLat(-123, 47);
        var (maxX, maxY) = WebMercator.FromLonLat(-122, 48);

        int points = 0;
        foreach (var polygon in NaturalEarthBasemap.GetLandPolygons(minX, minY, maxX, maxY, metresPerPixel: 10))
        {
            foreach (var (x, y) in polygon.WorldShell)
            {
                if (x > minX && x < maxX && y > minY && y < maxY)
                    points++;
            }
        }

        Assert.True(points > 400, $"only {points} coastline points in Puget Sound");
    }

    [Fact]
    public void GetTiles_returns_only_tiles_in_the_rectangle()
    {
        var level = NaturalEarthBasemap.Levels[^1];
        var (minX, minY) = WebMercator.FromLonLat(-122.56, 47.52);
        var (maxX, maxY) = WebMercator.FromLonLat(-122.33, 47.66);

        var tiles = level.GetTiles(minX, minY, maxX, maxY);

        var tile = Assert.Single(tiles);
        double half = WebMercator.Circumference / 2.0;
        Assert.InRange(minX, tile.Column * level.TileSize - half, (tile.Column + 1) * level.TileSize - half);
        Assert.InRange(minY, tile.Row * level.TileSize - half, (tile.Row + 1) * level.TileSize - half);
    }

    [Fact]
    public void LandFill_is_the_viewer_parchment_tone()
        => Assert.Equal(new RgbaColor(238, 232, 220), NaturalEarthBasemap.LandFill);

    [Fact]
    public void Compositor_offline_basemap_paints_land_and_differs_from_none()
    {
        var compositor = new HeadlessCompositor(new ProjNetCrsTransformFactory());

        var viewport = SaharaViewport;

        var white = new RgbaColor(255, 255, 255, 255);

        using var none = compositor.Render(
            Array.Empty<HeadlessCompositeInput>(),
            new HeadlessCompositeOptions
            {
                Viewport = viewport,
                Background = white,
                Basemap = BasemapKind.None,
            });

        using var offline = compositor.Render(
            Array.Empty<HeadlessCompositeInput>(),
            new HeadlessCompositeOptions
            {
                Viewport = viewport,
                Background = white,
                Basemap = BasemapKind.Offline,
            });

        // No basemap: the frame is the plain background.
        Assert.Equal(new SKColor(0xFF, 0xFF, 0xFF), none.GetPixel(64, 64));

        // Offline basemap: land is painted in the parchment tone.
        Assert.Equal(new SKColor(238, 232, 220), offline.GetPixel(64, 64));
    }

    [Fact]
    public void Compositor_offline_basemap_whole_world_viewport_paints_asia()
    {
        // `s100 render --bbox -180,-80,180,80 -w 1600 -h 800` aspect-fits the
        // box by widening EPSG:3857 X, so the viewport spans ~±279° — more than
        // one world. That once tripped the antimeridian seam-wrap, which folded
        // every vertex into a single 360° window ([−279°, 81°)): land east of
        // 81°E vanished and polygons crossing that fold smeared into horizontal
        // stripes. Mirror the CLI's aspect-fit and assert land over Mongolia.
        const int width = 1600, height = 800;
        var (minX, minY) = WebMercator.FromLonLat(-180, -80);
        var (maxX, maxY) = WebMercator.FromLonLat(180, 80);
        double grow = ((maxY - minY) * width / height - (maxX - minX)) / 2.0;
        minX -= grow;
        maxX += grow;
        var (minLon, minLat) = WebMercator.ToLonLat(minX, minY);
        var (maxLon, maxLat) = WebMercator.ToLonLat(maxX, maxY);
        Assert.True(maxLon > 180.0, "The aspect-fit must widen the viewport past ±180° to exercise the bug.");

        var viewport = new Viewport
        {
            MinLongitude = minLon,
            MaxLongitude = maxLon,
            MinLatitude = minLat,
            MaxLatitude = maxLat,
            WidthPixels = width,
            HeightPixels = height,
            ScaleDenominator = 100_000_000,
        };

        var compositor = new HeadlessCompositor(new ProjNetCrsTransformFactory());
        using var bitmap = compositor.Render(
            Array.Empty<HeadlessCompositeInput>(),
            new HeadlessCompositeOptions
            {
                Viewport = viewport,
                Background = new RgbaColor(255, 255, 255, 255),
                Basemap = BasemapKind.Offline,
            });

        var parchment = new SKColor(238, 232, 220);
        Assert.Equal(parchment, bitmap.GetPixel(PixelX(100), PixelY(50)));  // Mongolia
        Assert.Equal(parchment, bitmap.GetPixel(PixelX(-100), PixelY(45))); // Great Plains

        // Open ocean (mid-Pacific) stays background — no smeared stripes.
        Assert.Equal(new SKColor(0xFF, 0xFF, 0xFF), bitmap.GetPixel(PixelX(-150), PixelY(10)));

        int PixelX(double lon) =>
            (int)((WebMercator.FromLonLat(lon, 0).X - minX) / (maxX - minX) * width);

        int PixelY(double lat) =>
            (int)((maxY - WebMercator.FromLonLat(0, lat).Y) / (maxY - minY) * height);
    }
}
