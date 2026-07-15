using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Renderers.Skia;
using EncDotNet.S100.Renderers.Skia.Scene;
using EncDotNet.S100.Rendering.Scene;
using SkiaSharp;

namespace EncDotNet.S100.VisualRegression.Tests;

/// <summary>
/// Unit tests for tiled-pattern area-fill rendering through the headless
/// vector core (<see cref="VectorSceneBuilder"/> →
/// <see cref="PatternAreaPaintOp"/> → <see cref="SkiaDisplayListRenderer"/>).
/// Issue #192 acceptance: a dataset with patterned area fills renders those
/// patterns on the headless path.
/// </summary>
public sealed class PatternAreaFillRenderingTests
{
    private static readonly RgbaColor White = new(255, 255, 255, 255);
    private static readonly RgbaColor Black = new(0, 0, 0, 255);

    // A tiny SVG used as a pattern symbol — a 4 mm square containing a black
    // diagonal stroke. After rasterisation through SkiaSvgRasterizer at the
    // default 1.5 px/mm density the tile is 6 × 6 px with a few black pixels,
    // which is plenty to detect against a white background.
    private const string DiagonalSvg =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"4mm\" height=\"4mm\" " +
        "viewBox=\"0 0 4 4\">" +
        "<line x1=\"0\" y1=\"0\" x2=\"4\" y2=\"4\" stroke=\"black\" stroke-width=\"0.8\"/>" +
        "</svg>";

    private static readonly AreaFill DiagonalFill = new()
    {
        Name = "DIAG",
        PatternSymbol = "DIAG_SYM",
        V1X = 4,
        V1Y = 0,
        V2X = 0,
        V2Y = 4,
    };

    [Fact]
    public void Render_PatternArea_FillsInteriorWithTiledPattern()
    {
        // The renderer must produce visible (non-background) pattern strokes
        // inside the polygon when a pattern resolver is wired.
        var bitmap = RenderPatternedSquare(provideResolver: true);

        Assert.False(IsBlank(bitmap, White),
            "Patterned area produced no visible (non-background) pixels.");

        int blackish = CountBlackish(bitmap);
        Assert.True(blackish > 10,
            $"Expected the tiled diagonal pattern to leave several dark pixels; got {blackish}.");
    }

    [Fact]
    public void Render_PatternArea_NoResolver_IsSkipped()
    {
        // Without an areaFillProvider, pattern instructions are not lowered
        // into the IR — preserving the Mapsui-path behaviour where patterns
        // are rendered by the renderer's dedicated pattern phase instead.
        var bitmap = RenderPatternedSquare(provideResolver: false);

        Assert.True(IsBlank(bitmap, White),
            "Pattern area should be skipped when no area-fill provider is supplied.");
    }

    [Fact]
    public void Render_PatternArea_AdjacentPolygons_ShareGlobalTileOrigin()
    {
        // Two horizontally-adjacent polygons sharing one pattern must align
        // seamlessly across their shared edge because the tile shader is
        // anchored to a global world-space origin (mirrors Mapsui's
        // AnchoredPatternFillRenderer). A per-polygon anchor would produce
        // a visible discontinuity at the seam — the pattern in each polygon
        // would start at its own bbox corner, so the vertical phase of the
        // tile would differ between the two halves.
        //
        // Invariant: for each Y row that intersects both polygons, the LEFT
        // half is dark iff the RIGHT half is dark. (With a per-polygon
        // anchor, half the rows would mismatch.)

        var instructions = new List<DrawingInstruction>
        {
            MakeAreaInstruction("LEFT", priority: 1),
            MakeAreaInstruction("RIGHT", priority: 1),
        };
        var geometry = new InMemoryGeometry();
        geometry.Add("LEFT", SquareRing(lon: -0.002, lat: 0.0, halfLon: 0.002, halfLat: 0.002));
        geometry.Add("RIGHT", SquareRing(lon: 0.002, lat: 0.0, halfLon: 0.002, halfLat: 0.002));

        using var bitmap = RenderHeadless(instructions, geometry);

        // Bands well inside each polygon (avoid the polygon edge antialiasing
        // and the central seam).
        int leftStart = bitmap.Width / 8;
        int leftEnd = bitmap.Width * 3 / 8;
        int rightStart = bitmap.Width * 5 / 8;
        int rightEnd = bitmap.Width * 7 / 8;
        int yStart = bitmap.Height / 4;
        int yEnd = bitmap.Height * 3 / 4;

        int matchingRows = 0;
        int comparedRows = 0;
        for (int y = yStart; y < yEnd; y++)
        {
            bool leftDark = RowHasDark(bitmap, y, leftStart, leftEnd);
            bool rightDark = RowHasDark(bitmap, y, rightStart, rightEnd);
            comparedRows++;
            if (leftDark == rightDark)
                matchingRows++;
        }

        Assert.True(comparedRows > 0);
        // With a global anchor the match rate is ≈ 100 %; a per-polygon
        // anchor would put it close to 50 %. Demand strong agreement
        // (allow a small slack for antialiasing on tile-row boundaries).
        double matchRate = (double)matchingRows / comparedRows;
        Assert.True(matchRate >= 0.85,
            $"Pattern rows should align across polygons sharing a global tile origin; "
            + $"matched {matchingRows}/{comparedRows} rows ({matchRate:P0}).");
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private static SKBitmap RenderPatternedSquare(bool provideResolver)
    {
        var instructions = new List<DrawingInstruction>
        {
            MakeAreaInstruction("S1", priority: 1),
        };
        var geometry = new InMemoryGeometry();
        geometry.Add("S1", SquareRing(lon: 0.0, lat: 0.0, halfLon: 0.004, halfLat: 0.004));

        return RenderHeadless(instructions, geometry,
            areaFillProvider: provideResolver ? (_ => DiagonalFill) : null,
            provideAreaFill: provideResolver);
    }

    private static SKBitmap RenderHeadless(
        IReadOnlyList<DrawingInstruction> instructions,
        InMemoryGeometry geometry,
        Func<string, AreaFill?>? areaFillProvider = null,
        bool provideAreaFill = true)
    {
        // Note: keep the caller's null explicit (don't fall back to a default
        // resolver) — the "skipped" test relies on actually omitting it.
        if (provideAreaFill)
            areaFillProvider ??= (_ => DiagonalFill);
        else
            areaFillProvider = null;

        return HeadlessVectorRenderer.Render(
            instructions,
            geometry,
            palette: ColorPalette.Default,
            symbolProvider: name => name == "DIAG_SYM" ? DiagonalSvg : null,
            lineStyleProvider: null,
            symbolScale: 1.0,
            textScale: 1.0,
            widthPixels: 200,
            heightPixels: 200,
            background: White,
            areaFillProvider: areaFillProvider);
    }

    private static AreaInstruction MakeAreaInstruction(string featureRef, int priority) => new()
    {
        FeatureReference = featureRef,
        AreaFillReference = "DIAG",
        DrawingPriority = priority,
    };

    private static IReadOnlyList<GeoPosition> SquareRing(
        double lon, double lat, double halfLon, double halfLat)
    {
        return
        [
            new GeoPosition(lat - halfLat, lon - halfLon),
            new GeoPosition(lat - halfLat, lon + halfLon),
            new GeoPosition(lat + halfLat, lon + halfLon),
            new GeoPosition(lat + halfLat, lon - halfLon),
            new GeoPosition(lat - halfLat, lon - halfLon),
        ];
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

    private static int CountBlackish(SKBitmap bitmap)
    {
        int count = 0;
        for (int y = 0; y < bitmap.Height; y++)
        for (int x = 0; x < bitmap.Width; x++)
        {
            var p = bitmap.GetPixel(x, y);
            if (p.Red < 80 && p.Green < 80 && p.Blue < 80)
                count++;
        }
        return count;
    }

    private static IReadOnlyList<int> SampleDarkRows(SKBitmap bitmap, int x, int minY, int maxY)
    {
        var rows = new List<int>();
        for (int y = Math.Max(0, minY); y < Math.Min(bitmap.Height, maxY); y++)
        {
            var p = bitmap.GetPixel(x, y);
            if (p.Red < 128 && p.Green < 128 && p.Blue < 128)
                rows.Add(y);
        }
        return rows;
    }

    private static bool RowHasDark(SKBitmap bitmap, int y, int xStart, int xEnd)
    {
        for (int x = xStart; x < xEnd; x++)
        {
            var p = bitmap.GetPixel(x, y);
            if (p.Red < 128 && p.Green < 128 && p.Blue < 128)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Minimal in-memory geometry provider for these unit tests: stores a
    /// single ring per feature reference and exposes it as a polygon surface.
    /// </summary>
    private sealed class InMemoryGeometry : IFeatureGeometryProvider
    {
        private readonly Dictionary<string, FeatureGeometry> _features = new();

        public void Add(string id, IReadOnlyList<GeoPosition> ring)
        {
            _features[id] = new FeatureGeometry
            {
                Type = GeometryType.Surface,
                Coordinates = ring,
            };
        }

        public FeatureGeometry? GetGeometry(string featureRef) =>
            _features.TryGetValue(featureRef, out var g) ? g : null;
    }
}
