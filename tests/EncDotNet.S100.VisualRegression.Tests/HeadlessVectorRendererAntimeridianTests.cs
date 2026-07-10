using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Renderers.Skia.Scene;
using EncDotNet.S100.Rendering.Scene;
using SkiaSharp;

namespace EncDotNet.S100.VisualRegression.Tests;

/// <summary>
/// Regression test for issue #413: the headless auto-fit must frame a dataset
/// whose geometry straddles the ±180° antimeridian on its true (narrow) extent
/// instead of collapsing to a near-global viewport that renders blank.
/// </summary>
public sealed class HeadlessVectorRendererAntimeridianTests
{
    private static readonly RgbaColor White = new(255, 255, 255, 255);

    /// <summary>
    /// Two point features on opposite sides of the seam: one near +179°, one
    /// near −179° (a ~2° true extent across the dateline).
    /// </summary>
    private sealed class SeamGeometryProvider : IFeatureGeometryProvider
    {
        public FeatureGeometry? GetGeometry(string featureReference) => featureReference switch
        {
            "east" => new FeatureGeometry
            {
                Type = GeometryType.Point,
                Coordinates = [new GeoPosition(65.0, 179.0)], // (Latitude, Longitude)
            },
            "west" => new FeatureGeometry
            {
                Type = GeometryType.Point,
                Coordinates = [new GeoPosition(65.0, -179.0)], // (Latitude, Longitude)
            },
            _ => null,
        };
    }

    private static IReadOnlyList<DrawingInstruction> SeamInstructions() =>
    [
        new PointInstruction { FeatureReference = "east", SymbolReference = "QUESMRK1" },
        new PointInstruction { FeatureReference = "west", SymbolReference = "QUESMRK1" },
    ];

    private static ColorPalette TestPalette() => new("Test", new Dictionary<string, string>());

    [Fact]
    public void Fit_across_antimeridian_is_narrow_not_global()
    {
        var scene = HeadlessVectorRenderer.BuildScene(
            SeamInstructions(),
            new SeamGeometryProvider(),
            TestPalette(),
            symbolProvider: null,
            lineStyleProvider: null,
            symbolScale: 1.0,
            textScale: 1.0);

        var viewport = HeadlessVectorRenderer.FitViewport(scene, 400, 400);

        // The framed longitude span must be a handful of degrees (the 2° extent
        // plus padding / aspect growth), NOT near-global.
        Assert.InRange(viewport.LongitudeSpan, 1.0, 60.0);
        // The seam-shifted window runs past +180° in the resolved frame.
        Assert.True(viewport.MaxLongitude > 180.0,
            "Auto-fit across the antimeridian should produce a shifted (>180°) frame.");
    }

    [Fact]
    public void Render_across_antimeridian_is_not_blank()
    {
        using var bitmap = HeadlessVectorRenderer.Render(
            SeamInstructions(),
            new SeamGeometryProvider(),
            TestPalette(),
            symbolProvider: null,
            lineStyleProvider: null,
            symbolScale: 1.0,
            textScale: 1.0,
            widthPixels: 400,
            heightPixels: 400,
            background: White);

        // Both point features (fallback dots) must render — one on the left
        // half, one on the right half — proving neither collapsed sub-pixel.
        Assert.True(HasNonBackgroundPixel(bitmap, 0, bitmap.Width / 2),
            "Expected a rendered feature in the left half of the canvas.");
        Assert.True(HasNonBackgroundPixel(bitmap, bitmap.Width / 2, bitmap.Width),
            "Expected a rendered feature in the right half of the canvas.");
    }

    private static bool HasNonBackgroundPixel(SKBitmap bitmap, int xStart, int xEnd)
    {
        for (int y = 0; y < bitmap.Height; y++)
        for (int x = xStart; x < xEnd; x++)
        {
            var p = bitmap.GetPixel(x, y);
            if (p.Red != White.R || p.Green != White.G || p.Blue != White.B)
                return true;
        }
        return false;
    }
}
