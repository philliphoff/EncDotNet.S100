using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Renderers.Skia.Scene;
using SkiaSharp;

namespace EncDotNet.S100.VisualRegression.Tests;

/// <summary>
/// Tests for the <see cref="DrawingInstructionCategory"/> suppression filter
/// honoured by <see cref="HeadlessVectorRenderer.Render"/>. Drives the
/// renderer with a synthetic four-category display list (one area, one line,
/// one point, one text) so each category can be asserted in isolation.
/// </summary>
public sealed class HeadlessVectorRendererFilterTests
{
    private static readonly RgbaColor White = new(255, 255, 255, 255);

    /// <summary>
    /// Trivial geometry provider that returns the same point/curve/surface
    /// shapes regardless of feature reference, so the test can focus on
    /// instruction-category filtering rather than feature lookup mechanics.
    /// </summary>
    private sealed class StubGeometryProvider : IFeatureGeometryProvider
    {
        public FeatureGeometry? GetGeometry(string featureReference) => featureReference switch
        {
            "area" => new FeatureGeometry
            {
                Type = GeometryType.Surface,
                Coordinates =
                [
                    (0.001, 0.001),
                    (0.001, 0.009),
                    (0.009, 0.009),
                    (0.009, 0.001),
                ],
            },
            "line" => new FeatureGeometry
            {
                Type = GeometryType.Curve,
                Coordinates = [(0.005, 0.001), (0.005, 0.009)],
            },
            "point" or "text" => new FeatureGeometry
            {
                Type = GeometryType.Point,
                Coordinates = [(0.005, 0.005)],
            },
            _ => null,
        };
    }

    private static IReadOnlyList<DrawingInstruction> BuildAllFourCategories() =>
    [
        new AreaInstruction
        {
            FeatureReference = "area",
            FillColor = "FILL",
        },
        new LineInstruction
        {
            FeatureReference = "line",
            LineColor = "LINE",
            LineWidth = 0.32, // mm — translates to a visible stroke on a 200x200 canvas.
        },
        new PointInstruction
        {
            FeatureReference = "point",
            // QUESMRK1 has a known purple fallback colour in ColorResolver, so
            // the point's dot can't be mistaken for the black text glyph.
            SymbolReference = "QUESMRK1",
        },
        new TextInstruction
        {
            FeatureReference = "text",
            Text = "X",
            FontSize = 24.0,
            FontColor = "TEXT",
        },
    ];

    private static ColorPalette TestPalette() => new(
        "Test",
        new Dictionary<string, string>
        {
            ["FILL"] = "#FFA0A0", // light red — distinct from text/line.
            ["LINE"] = "#000080", // navy
            ["TEXT"] = "#000000", // black
        });

    private static SKBitmap RenderWith(DrawingInstructionCategory hidden) =>
        HeadlessVectorRenderer.Render(
            BuildAllFourCategories(),
            new StubGeometryProvider(),
            TestPalette(),
            symbolProvider: null,
            lineStyleProvider: null,
            symbolScale: 1.0,
            textScale: 1.0,
            widthPixels: 200,
            heightPixels: 200,
            background: White,
            hiddenCategories: hidden);

    [Fact]
    public void Hidden_None_Renders_All_Categories()
    {
        using var bitmap = RenderWith(DrawingInstructionCategory.None);

        // The fill ("#FFA0A0") covers most of the canvas; we should see at
        // least one red-ish pixel proving the area was drawn.
        Assert.True(HasPixelMatching(bitmap, p => p.Red > 200 && p.Green < 200 && p.Blue < 200),
            "Default render should include the area fill.");
    }

    [Fact]
    public void Hidden_All_Produces_Blank_Bitmap()
    {
        using var bitmap = RenderWith(DrawingInstructionCategory.All);
        Assert.True(IsBlank(bitmap, White),
            "Suppressing all four categories must leave the background untouched.");
    }

    [Fact]
    public void Hidden_Text_Removes_Black_Glyph_Pixels_But_Preserves_Fill()
    {
        // Black is the text foreground; with text shown there must be at
        // least one near-black pixel from the glyph. With text hidden the
        // remaining categories use non-black colours so no near-black pixel
        // should remain.
        using var withText = RenderWith(DrawingInstructionCategory.None);
        using var noText = RenderWith(DrawingInstructionCategory.Text);

        Assert.True(HasPixelMatching(withText, IsNearBlack),
            "Default render should contain near-black glyph pixels for the text.");
        Assert.False(HasPixelMatching(noText, IsNearBlack),
            "Hiding text should remove near-black glyph pixels.");

        // Fills still render (the area's red is unaffected by the text filter).
        Assert.True(HasPixelMatching(noText, p => p.Red > 200 && p.Green < 200 && p.Blue < 200),
            "Hiding text must not suppress the area fill.");
    }

    [Fact]
    public void Hidden_Combined_Flags_Are_Honoured()
    {
        // Hiding both Text and Areas leaves only the line + point (navy +
        // fallback) — asserting no fill-red and no glyph-black pixels remain.
        using var bitmap = RenderWith(
            DrawingInstructionCategory.Text | DrawingInstructionCategory.Areas);

        Assert.False(HasPixelMatching(bitmap, p => p.Red > 200 && p.Green < 200 && p.Blue < 200),
            "Hiding areas should remove the red fill.");
        Assert.False(HasPixelMatching(bitmap, IsNearBlack),
            "Hiding text should remove black glyph pixels.");
    }

    private static bool IsNearBlack(SKColor p) =>
        p.Red < 60 && p.Green < 60 && p.Blue < 60 && p.Alpha > 128;

    private static bool HasPixelMatching(SKBitmap bitmap, Func<SKColor, bool> predicate)
    {
        for (int y = 0; y < bitmap.Height; y++)
        for (int x = 0; x < bitmap.Width; x++)
        {
            if (predicate(bitmap.GetPixel(x, y)))
                return true;
        }
        return false;
    }

    private static bool IsBlank(SKBitmap bitmap, RgbaColor background)
    {
        for (int y = 0; y < bitmap.Height; y++)
        for (int x = 0; x < bitmap.Width; x++)
        {
            var p = bitmap.GetPixel(x, y);
            if (p.Red != background.R || p.Green != background.G || p.Blue != background.B)
                return false;
        }
        return true;
    }
}
