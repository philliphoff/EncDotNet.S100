using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Renderers.Skia;
using EncDotNet.S100.Renderers.Skia.Scene;
using EncDotNet.S100.Rendering.Scene;
using SkiaSharp;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// The size factors that turn S-100 Part 9 millimetres and points into pixels
/// (#1508): the default <see cref="SymbolSizeProfile.Screen"/> keeps the
/// viewer's sizes, and <see cref="SymbolSizeProfile.Part9Millimetres"/> draws
/// physical sizes for print.
/// </summary>
public sealed class SymbolSizeProfileTests
{
    private sealed class Geometry : IFeatureGeometryProvider
    {
        public FeatureGeometry? GetGeometry(string featureReference) => featureReference switch
        {
            "line" => new FeatureGeometry
            {
                Type = GeometryType.Curve,
                Coordinates = [new GeoPosition(50, -1), new GeoPosition(50, -0.9)],
            },
            "area" => new FeatureGeometry
            {
                Type = GeometryType.Surface,
                Coordinates =
                [
                    new GeoPosition(50, -1), new GeoPosition(50, -0.9),
                    new GeoPosition(50.1, -0.9), new GeoPosition(50.1, -1), new GeoPosition(50, -1),
                ],
            },
            _ => new FeatureGeometry
            {
                Type = GeometryType.Point,
                Coordinates = [new GeoPosition(50, -1)],
            },
        };
    }

    private static readonly LineStyle Dashed = new()
    {
        Name = "DASHED",
        Width = 0.64f,
        Color = "CHBLK",
        DashPattern = [0f, 3.2f],
        IntervalLength = 6.4f,
        Symbols = [new LineStyleSymbol("SYM", 1f)],
    };

    private static readonly DrawingInstruction[] Instructions =
    [
        new PointInstruction
        {
            FeatureReference = "point", SymbolReference = "SYM", SymbolScale = 2,
            LocalOffsetX = 3.2, LocalOffsetY = -1.6,
        },
        new PointInstruction { FeatureReference = "missing", SymbolReference = "NONE" },
        new LineInstruction { FeatureReference = "line", LineWidth = 0.96, LineColor = "CHBLK" },
        new LineInstruction { FeatureReference = "line", LineStyleReference = "DASHED" },
        new TextInstruction
        {
            FeatureReference = "text", Text = "Label", FontSize = 10,
            OffsetX = 1.6, OffsetY = 3.2, HaloColor = "CHWHT", HaloWidth = 0.32,
        },
    ];

    private static VectorScene Build(SymbolSizeProfile? profile)
    {
        var builder = new VectorSceneBuilder
        {
            ResolveColor = _ => new RgbaColor(0, 0, 0, 255),
            SymbolResolver = name => name == "SYM" ? new SymbolAsset("<svg/>", 0, 0) : null,
            LineStyleProvider = name => name == "DASHED" ? Dashed : null,
            SymbolScale = 1.5,
            TextScale = 2,
            SizeProfile = profile ?? SymbolSizeProfile.Screen,
        };
        return builder.Build(Instructions, new Geometry());
    }

    private static PointPaintOp Symbol(VectorScene scene) =>
        scene.Ops.OfType<PointPaintOp>().Single(p => p.Symbol is not null);

    private static PointPaintOp Fallback(VectorScene scene) =>
        scene.Ops.OfType<PointPaintOp>().Single(p => p.Symbol is null);

    private static LinePaintOp Simple(VectorScene scene) =>
        scene.Ops.OfType<LinePaintOp>().Single(l => l.Symbols.Count == 0);

    private static LinePaintOp Styled(VectorScene scene) =>
        scene.Ops.OfType<LinePaintOp>().Single(l => l.Symbols.Count > 0);

    [Fact]
    public void Screen_IsTheDefault()
    {
        Assert.Same(SymbolSizeProfile.Screen, new VectorSceneBuilder { ResolveColor = _ => default }.SizeProfile);
        Assert.Equal(new SymbolSizeProfile(), SymbolSizeProfile.Screen);
    }

    [Fact]
    public void Screen_HasTheViewerFactors()
    {
        var screen = SymbolSizeProfile.Screen;
        Assert.Equal(0.6, screen.PointSymbolScale);
        Assert.Equal(0.32, screen.MillimetresPerPixel);
        Assert.Equal(1.5, screen.PatternPixelsPerMillimetre);
        Assert.Equal(1.0, screen.PixelsPerFontPoint);
        Assert.Equal(0.15, screen.FallbackDotScale);
        Assert.Equal(1.5, SkiaSvgRasterizer.DefaultPixelsPerMm);
    }

    /// <summary>The default sizes are exactly the constants the builder used before profiles existed.</summary>
    [Fact]
    public void Screen_KeepsTodaysSizes()
    {
        var scene = Build(profile: null);

        var symbol = Symbol(scene);
        Assert.Equal(0.6 * 2 * 1.5, symbol.Symbol!.Value.Scale);
        Assert.Equal(3.2 / 0.32, symbol.OffsetXpx);
        Assert.Equal(-1.6 / 0.32, symbol.OffsetYpx);
        Assert.Equal(0.15 * 1 * 1.5, Fallback(scene).FallbackScale);

        Assert.Equal(0.96 / 0.32, Simple(scene).WidthPx);

        var styled = Styled(scene);
        Assert.Equal(Math.Max(0.64f / 0.32, 1.0), styled.WidthPx);
        Assert.Equal(6.4f / 0.32, styled.SymbolIntervalPx);
        Assert.Equal(1 / 0.32 / (96 / 25.4) * 1.5, styled.Symbols[0].Symbol.Scale);
        Assert.Equal(1f / 0.32, styled.Symbols[0].PositionPx);

        var text = scene.Ops.OfType<TextPaintOp>().Single();
        Assert.Equal(10 * 2, text.FontSizePx);
        Assert.Equal(1.6 / 0.32, text.OffsetXpx);
        Assert.Equal(3.2 / 0.32, text.OffsetYpx);
        Assert.Equal(0.32 / 0.32 * 2, text.HaloWidthPx);
    }

    [Fact]
    public void Part9Millimetres_At96Dpi_DrawsPhysicalSizes()
    {
        var profile = SymbolSizeProfile.Part9Millimetres();
        const double pxPerMm = 96 / 25.4;

        Assert.Equal(1.0, profile.PointSymbolScale, 12);
        Assert.Equal(1 / pxPerMm, profile.MillimetresPerPixel, 12);
        Assert.Equal(pxPerMm, profile.PatternPixelsPerMillimetre, 12);
        Assert.Equal(96.0 / 72, profile.PixelsPerFontPoint, 12);
        Assert.Equal(1.0, profile.LineSymbolScale, 12);

        var scene = Build(profile);
        Assert.Equal(2 * 1.5, Symbol(scene).Symbol!.Value.Scale, 12);
        Assert.Equal(3.2 * pxPerMm, Symbol(scene).OffsetXpx, 9);
        Assert.Equal(0.96 * pxPerMm, Simple(scene).WidthPx, 9);
        Assert.Equal(1.5, Styled(scene).Symbols[0].Symbol.Scale, 12);
        Assert.Equal(10 * 96.0 / 72 * 2, scene.Ops.OfType<TextPaintOp>().Single().FontSizePx, 9);
    }

    /// <summary>Physical point symbols are 1/0.6 ≈ 1.67 times the screen size: the gap #1508 closes.</summary>
    [Fact]
    public void Part9Millimetres_PointSymbolsAreAboutOnePointSixTimesTheScreenSize()
    {
        double ratio = Symbol(Build(SymbolSizeProfile.Part9Millimetres())).Symbol!.Value.Scale
            / Symbol(Build(SymbolSizeProfile.Screen)).Symbol!.Value.Scale;
        Assert.Equal(1 / 0.6, ratio, 12);
    }

    [Fact]
    public void Part9Millimetres_ScalesWithResolution()
    {
        var at96 = Build(SymbolSizeProfile.Part9Millimetres(96));
        var at300 = Build(SymbolSizeProfile.Part9Millimetres(300));
        const double factor = 300.0 / 96;

        Assert.Equal(Symbol(at96).Symbol!.Value.Scale * factor, Symbol(at300).Symbol!.Value.Scale, 9);
        Assert.Equal(Simple(at96).WidthPx * factor, Simple(at300).WidthPx, 9);
        Assert.Equal(
            TextOf(at96).FontSizePx * factor, TextOf(at300).FontSizePx, 9);
        // Line-style symbols stay sized to the line's own millimetres.
        Assert.Equal(Styled(at96).Symbols[0].Symbol.Scale * factor, Styled(at300).Symbols[0].Symbol.Scale, 9);
        Assert.Equal(Styled(at96).SymbolIntervalPx * factor, Styled(at300).SymbolIntervalPx, 9);

        static TextPaintOp TextOf(VectorScene s) => s.Ops.OfType<TextPaintOp>().Single();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-96)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Part9Millimetres_RejectsABadResolution(double dpi) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => SymbolSizeProfile.Part9Millimetres(dpi));

    [Fact]
    public void Factors_MustBePositive() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new SymbolSizeProfile { MillimetresPerPixel = 0 });

    /// <summary>Pattern tiles are rasterised at the profile's density, so a denser profile makes a larger tile.</summary>
    [Fact]
    public void HeadlessPatternTiles_FollowTheProfile()
    {
        const string svg =
            "<svg xmlns='http://www.w3.org/2000/svg' width='2mm' height='2mm' viewBox='0 0 2 2'>"
            + "<rect x='0' y='0' width='2' height='2' fill='black'/></svg>";
        var fill = new AreaFill
        {
            Name = "FILL",
            PatternSymbol = "PAT",
            V1X = 4,
            V1Y = 0,
            V2X = 0,
            V2Y = 4,
        };
        DrawingInstruction[] instructions = [new AreaInstruction { FeatureReference = "area", AreaFillReference = "FILL" }];

        int TileWidth(SymbolSizeProfile? profile)
        {
            var scene = HeadlessVectorRenderer.BuildScene(
                instructions,
                new Geometry(),
                new ColorPalette("test", new Dictionary<string, string>()),
                name => name == "PAT" ? svg : null,
                lineStyleProvider: null,
                symbolScale: 1,
                textScale: 1,
                areaFillProvider: name => name == "FILL" ? fill : null,
                sizeProfile: profile);
            var op = scene.Ops.OfType<PatternAreaPaintOp>().Single();
            using var bitmap = SKBitmap.Decode(op.TilePng);
            return bitmap.Width;
        }

        int screen = TileWidth(null);
        Assert.Equal(screen, TileWidth(SymbolSizeProfile.Screen));
        int part9 = TileWidth(SymbolSizeProfile.Part9Millimetres());
        Assert.Equal(96 / 25.4 / 1.5, (double)part9 / screen, 1);
    }
}
