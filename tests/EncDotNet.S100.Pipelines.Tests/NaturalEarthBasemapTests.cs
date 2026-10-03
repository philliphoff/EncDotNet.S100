using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Renderers.Skia.Scene;
using EncDotNet.S100.Rendering.Scene;
using SkiaSharp;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Verifies the bundled Natural Earth land basemap (issue #411): the shared
/// <see cref="NaturalEarthBasemap"/> source parses to a non-empty parchment
/// <see cref="VectorScene"/>, and the <see cref="HeadlessCompositor"/> honours
/// <see cref="BasemapKind.Offline"/> by painting land beneath the chart so the
/// output differs from <see cref="BasemapKind.None"/>.
/// </summary>
public sealed class NaturalEarthBasemapTests
{
    [Fact]
    public void LandScene_is_nonempty_parchment_areas()
    {
        var scene = NaturalEarthBasemap.LandScene;

        Assert.NotEmpty(scene.Ops);
        Assert.All(scene.Ops, op =>
        {
            var area = Assert.IsType<AreaPaintOp>(op);
            Assert.Equal(NaturalEarthBasemap.LandFill, area.Fill);
            Assert.NotEmpty(area.WorldShell);
        });
    }

    [Fact]
    public void LandFill_is_the_viewer_parchment_tone()
        => Assert.Equal(new RgbaColor(238, 232, 220), NaturalEarthBasemap.LandFill);

    [Fact]
    public void Compositor_offline_basemap_paints_land_and_differs_from_none()
    {
        var compositor = new HeadlessCompositor(new ProjNetCrsTransformFactory());

        // A viewport wholly over solid land (the Sahara) so the centre pixel is
        // land in the Natural Earth 1:10m set.
        var viewport = new Viewport
        {
            MinLongitude = 10,
            MaxLongitude = 30,
            MinLatitude = 15,
            MaxLatitude = 30,
            WidthPixels = 128,
            HeightPixels = 128,
            ScaleDenominator = 20_000_000,
        };

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
