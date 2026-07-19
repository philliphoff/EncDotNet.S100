using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Renderers.Skia.Scene;
using SkiaSharp;

namespace EncDotNet.S100.VisualRegression.Tests;

/// <summary>
/// Tests for the optional explicit <see cref="Viewport"/> honoured by
/// <see cref="HeadlessVectorRenderer.Render"/>. An explicit viewport frames an
/// exact window instead of auto-fitting the scene extent, and — because it
/// carries a meaningful <see cref="Viewport.ScaleDenominator"/> — also enables
/// S-100 Part 9 scale-visibility culling, bringing the single-dataset headless
/// path into alignment with the composite/GUI paths.
/// </summary>
public sealed class HeadlessVectorRendererViewportTests
{
    private static readonly RgbaColor White = new(255, 255, 255, 255);

    // A single red area near the origin (lat/lon 0.001–0.009).
    private sealed class OriginAreaGeometryProvider : IFeatureGeometryProvider
    {
        public FeatureGeometry? GetGeometry(string featureReference) => featureReference == "area"
            ? new FeatureGeometry
            {
                Type = GeometryType.Surface,
                Coordinates =
                [
                    new GeoPosition(0.001, 0.001),
                    new GeoPosition(0.001, 0.009),
                    new GeoPosition(0.009, 0.009),
                    new GeoPosition(0.009, 0.001),
                ],
            }
            : null;
    }

    private static IReadOnlyList<DrawingInstruction> AreaOnly(double? scaleMinimum = null) =>
    [
        new AreaInstruction
        {
            FeatureReference = "area",
            FillColor = "FILL",
            ScaleMinimum = scaleMinimum,
        },
    ];

    private static ColorPalette TestPalette() => new(
        "Test",
        new Dictionary<string, string> { ["FILL"] = "#FFA0A0" });

    private static SKBitmap Render(IReadOnlyList<DrawingInstruction> instructions, Viewport? viewport) =>
        HeadlessVectorRenderer.Render(
            instructions,
            new OriginAreaGeometryProvider(),
            TestPalette(),
            symbolProvider: null,
            lineStyleProvider: null,
            symbolScale: 1.0,
            textScale: 1.0,
            widthPixels: 200,
            heightPixels: 200,
            background: White,
            viewport: viewport);

    private static Viewport MakeViewport(
        double minLon, double minLat, double maxLon, double maxLat, double scaleDenominator) => new()
        {
            MinLongitude = minLon,
            MinLatitude = minLat,
            MaxLongitude = maxLon,
            MaxLatitude = maxLat,
            WidthPixels = 200,
            HeightPixels = 200,
            ScaleDenominator = scaleDenominator,
        };

    [Fact]
    public void Explicit_Viewport_Frames_The_Requested_Window_Not_The_Auto_Fit()
    {
        // Auto-fit (no viewport) frames the area, so its red fill is visible.
        using var autoFit = Render(AreaOnly(), viewport: null);
        Assert.True(HasRedPixel(autoFit),
            "Auto-fit should frame the origin area and draw its fill.");

        // An explicit viewport far from the origin excludes the geometry, so
        // the output is the untouched background — proving the exact window was
        // honoured rather than the auto-fitted extent.
        using var elsewhere = Render(AreaOnly(), MakeViewport(-71.0, 40.0, -70.0, 41.0, 50_000.0));
        Assert.False(HasRedPixel(elsewhere),
            "An explicit viewport away from the geometry must not auto-fit back onto it.");
    }

    [Fact]
    public void Explicit_Viewport_Enables_Scale_Visibility_Culling()
    {
        // A viewport that contains the geometry, at a denominator (5000) more
        // zoomed-out than the instruction's ScaleMinimum (500 = largest allowed
        // denominator). Same window, two instruction variants:
        var viewport = MakeViewport(-0.02, -0.02, 0.02, 0.02, 5_000.0);

        // No scale limits → drawn (confirms the window frames the geometry).
        using var unlimited = Render(AreaOnly(scaleMinimum: null), viewport);
        Assert.True(HasRedPixel(unlimited),
            "The viewport should contain the origin area when it has no scale limits.");

        // ScaleMinimum 500 < viewport denominator 5000 → culled by scale
        // visibility, which is only active because a viewport was supplied.
        using var culled = Render(AreaOnly(scaleMinimum: 500.0), viewport);
        Assert.False(HasRedPixel(culled),
            "An explicit viewport must honour S-100 scale-visibility culling.");

        // The same scale-limited instruction under auto-fit is still drawn,
        // because the auto-fit path leaves culling disabled.
        using var autoFit = Render(AreaOnly(scaleMinimum: 500.0), viewport: null);
        Assert.True(HasRedPixel(autoFit),
            "Auto-fit leaves scale-visibility culling disabled, so the area is drawn.");
    }

    private static bool HasRedPixel(SKBitmap bitmap)
    {
        for (int y = 0; y < bitmap.Height; y++)
            for (int x = 0; x < bitmap.Width; x++)
            {
                var p = bitmap.GetPixel(x, y);
                if (p.Red > 200 && p.Green < 200 && p.Blue < 200)
                    return true;
            }
        return false;
    }
}
