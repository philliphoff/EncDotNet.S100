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
}
