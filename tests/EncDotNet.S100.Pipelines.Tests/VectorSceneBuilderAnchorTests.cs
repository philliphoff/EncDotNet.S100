using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Renderers.Skia.Scene;
using EncDotNet.S100.Rendering.Scene;
using SkiaSharp;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Resolved anchors (#1511): each point symbol and text op says where its
/// anchor came from (<see cref="AnchorPlacement"/>), and that anchor is where
/// the renderer draws it.
/// </summary>
public sealed class VectorSceneBuilderAnchorTests
{
    private static readonly RgbaColor Red = new(255, 0, 0, 255);

    /// <summary>
    /// "bent": a two-segment curve, 1 W to 0.9 W along 50 N then north to 50.1 N.
    /// "split": a two-part curve whose second part is longer. "islands": a
    /// two-part surface whose second part is larger. "square": 50–50.1 N,
    /// 1–0.9 W.
    /// </summary>
    private sealed class Shapes : IFeatureGeometryProvider
    {
        public static readonly GeoPosition[] Bent = [new(50, -1), new(50, -0.9), new(50.1, -0.9)];

        public FeatureGeometry? GetGeometry(string featureReference) => featureReference switch
        {
            "bent" => new() { Type = GeometryType.Curve, Coordinates = Bent },
            "split" => new()
            {
                Type = GeometryType.Curve,
                Coordinates = [new(50, -1), new(50, -0.99), new(50.2, -1), new(50.2, -0.9)],
                Parts = [[new(50, -1), new(50, -0.99)], [new(50.2, -1), new(50.2, -0.9)]],
            },
            "islands" => new()
            {
                Type = GeometryType.Surface,
                Coordinates = Square(50, -1, 0.01),
                Parts = [Square(50, -1, 0.01), Square(50.2, -1, 0.1)],
            },
            "square" => new() { Type = GeometryType.Surface, Coordinates = Square(50, -1, 0.1) },
            _ => null,
        };

        private static GeoPosition[] Square(double lat, double lon, double size) =>
            [new(lat, lon), new(lat, lon + size), new(lat + size, lon + size), new(lat + size, lon), new(lat, lon)];
    }

    private static List<PaintOp> Build(params DrawingInstruction[] instructions) =>
        new VectorSceneBuilder { ResolveColor = static _ => Red }
            .Build(instructions, new Shapes()).Ops.ToList();

    private static (double X, double Y) World(double lat, double lon) => WebMercator.FromLonLat(lon, lat);

    // The point a placement names on the source curve, from its segment.
    private static (double X, double Y) OnSource(IReadOnlyList<GeoPosition> curve, AnchorPlacement placement)
    {
        var a = curve[placement.Segment];
        var b = curve[placement.Segment + 1];
        var t = placement.SegmentPosition;
        return World(a.Latitude + t * (b.Latitude - a.Latitude), a.Longitude + t * (b.Longitude - a.Longitude));
    }

    [Fact]
    public void LinePlacedText_SaysWhereAlongTheCurveItIs()
    {
        var op = Assert.IsType<TextPaintOp>(Assert.Single(Build(
            new TextInstruction { FeatureReference = "bent", Text = "T", LinePlacementPosition = 0.75 })));

        Assert.Equal(AnchorPlacementKind.Line, op.Placement.Kind);
        Assert.Equal(0.75, op.Placement.LinePosition);
        // Three quarters of the way along two equal segments: half way up the second.
        Assert.Equal(1, op.Placement.Segment);
        Assert.Equal(0.5, op.Placement.SegmentPosition, 9);
        Assert.Equal(World(50.05, -0.9).X, op.World.X, 3);
        Assert.Equal(World(50.05, -0.9).Y, op.World.Y, 3);
        AssertSame(OnSource(Shapes.Bent, op.Placement), op.World);
    }

    [Fact]
    public void LinePlacedSymbol_GoesWhereTheTextDoes()
    {
        var ops = Build(
            new PointInstruction { FeatureReference = "bent", SymbolReference = "S", LinePlacementPosition = 0.25 },
            new TextInstruction { FeatureReference = "bent", Text = "T", LinePlacementPosition = 0.25 });

        var point = Assert.IsType<PointPaintOp>(ops[0]);
        var text = Assert.IsType<TextPaintOp>(ops[1]);
        Assert.Equal(AnchorPlacementKind.Line, point.Placement.Kind);
        Assert.Equal(text.Placement, point.Placement);
        Assert.Equal(text.World, point.World);
        Assert.Equal(0, point.Placement.Segment);
        AssertSame(World(50, -0.95), point.World);
    }

    [Fact]
    public void LinePlacement_IsClamped()
    {
        var op = Assert.IsType<TextPaintOp>(Assert.Single(Build(
            new TextInstruction { FeatureReference = "bent", Text = "T", LinePlacementPosition = 1.5 })));

        Assert.Equal(1, op.Placement.LinePosition);
        Assert.Equal(1, op.Placement.Segment);
        Assert.Equal(1, op.Placement.SegmentPosition);
        AssertSame(World(50.1, -0.9), op.World);
    }

    [Fact]
    public void LinePlacement_OnSeveralCurves_NamesTheLongest()
    {
        var ops = Build(
            new PointInstruction { FeatureReference = "split", SymbolReference = "S", LinePlacementPosition = 0.5 },
            new TextInstruction { FeatureReference = "split", Text = "T", LinePlacementPosition = 0.5 });

        foreach (var placement in new[] { Assert.IsType<PointPaintOp>(ops[0]).Placement, Assert.IsType<TextPaintOp>(ops[1]).Placement })
        {
            Assert.Equal(AnchorPlacementKind.Line, placement.Kind);
            Assert.Equal(1, placement.Part);
        }

        AssertSame(World(50.2, -0.95), Assert.IsType<PointPaintOp>(ops[0]).World);
    }

    [Fact]
    public void TextOnACurveWithoutPlacement_NamesTheMiddleVertex()
    {
        var op = Assert.IsType<TextPaintOp>(Assert.Single(Build(
            new TextInstruction { FeatureReference = "bent", Text = "T" })));

        Assert.Equal(AnchorPlacementKind.CurveMiddleVertex, op.Placement.Kind);
        Assert.Equal(1, op.Placement.Segment);
        Assert.Equal(World(50, -0.9), op.World);
    }

    [Fact]
    public void SymbolOnACurveWithoutPlacement_StaysAtItsFirstVertex()
    {
        var op = Assert.IsType<PointPaintOp>(Assert.Single(Build(
            new PointInstruction { FeatureReference = "bent", SymbolReference = "S" })));

        Assert.Equal(AnchorPlacementKind.Point, op.Placement.Kind);
        Assert.Equal(World(50, -1), op.World);
    }

    [Fact]
    public void SurfacePlacement_NamesTheLargestPartAndVisibleParts()
    {
        var ops = Build(
            new PointInstruction { FeatureReference = "islands", SymbolReference = "S" },
            new TextInstruction { FeatureReference = "islands", Text = "T", AreaPlacement = AreaPlacementMode.Geographic });

        var point = Assert.IsType<PointPaintOp>(ops[0]);
        Assert.Equal(new AnchorPlacement(AnchorPlacementKind.Surface, Part: 1, VisibleParts: true), point.Placement);
        Assert.NotNull(point.PlacementArea);
        Assert.Equal(2, point.PlacementArea!.Area.NumGeometries);

        var text = Assert.IsType<TextPaintOp>(ops[1]);
        Assert.Equal(new AnchorPlacement(AnchorPlacementKind.Surface, Part: 1), text.Placement);
        Assert.Null(text.PlacementArea);
        Assert.Equal(point.World, text.World);
    }

    [Fact]
    public void AugmentedPoint_IsACoordinate()
    {
        var op = Assert.IsType<PointPaintOp>(Assert.Single(Build(
            new PointInstruction { FeatureReference = "square", SymbolReference = "S", CoordinateOverride = new GeoPosition(50.01, -0.99) })));

        Assert.Equal(new AnchorPlacement(AnchorPlacementKind.Coordinate), op.Placement);
        Assert.Equal(World(50.01, -0.99), op.World);
    }

    [Fact]
    public void Placement_SurvivesTheSceneCache()
    {
        var scene = new VectorSceneBuilder { ResolveColor = static _ => Red }.Build(
            [
                new PointInstruction { FeatureReference = "square", SymbolReference = "S" },
                new TextInstruction { FeatureReference = "bent", Text = "T", LinePlacementPosition = 0.75 },
            ],
            new Shapes());

        var read = VectorSceneSerializer.Deserialize(VectorSceneSerializer.Serialize(scene));

        Assert.Equal(((PointPaintOp)scene.Ops[0]).Placement, ((PointPaintOp)read.Ops[0]).Placement);
        Assert.Equal(((TextPaintOp)scene.Ops[1]).Placement, ((TextPaintOp)read.Ops[1]).Placement);
    }

    // ── The renderer draws at the exposed anchor ──────────────────────────

    private static Viewport View(double south, double west, double north, double east) => new()
    {
        MinLatitude = south,
        MinLongitude = west,
        MaxLatitude = north,
        MaxLongitude = east,
        WidthPixels = 400,
        HeightPixels = 400,
        ScaleDenominator = 50_000,
    };

    // Centre of the pixels that are not the white background.
    private static (double X, double Y) Drawn(VectorScene scene, Viewport viewport)
    {
        var renderer = new SkiaDisplayListRenderer { Background = new RgbaColor(255, 255, 255, 255) };
        using var bitmap = renderer.Render(scene, viewport);
        double sumX = 0, sumY = 0;
        int count = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y) != SKColors.White)
                {
                    sumX += x + 0.5;
                    sumY += y + 0.5;
                    count++;
                }
            }
        }

        Assert.True(count > 0, "Nothing was drawn.");
        return (sumX / count, sumY / count);
    }

    private static void AssertDrawnAt((double X, double Y) world, VectorScene scene, Viewport viewport)
    {
        var (x, y) = WorldToScreen.Create(viewport).Project(world);
        var drawn = Drawn(scene, viewport);
        Assert.InRange(drawn.X, x - 1, x + 1);
        Assert.InRange(drawn.Y, y - 1, y + 1);
    }

    [Fact]
    public void Renderer_DrawsALinePlacedSymbolAtItsAnchor()
    {
        var scene = new VectorSceneBuilder { ResolveColor = static _ => Red }.Build(
            [new PointInstruction { FeatureReference = "bent", SymbolReference = "S", LinePlacementPosition = 0.75 }],
            new Shapes());
        var op = Assert.IsType<PointPaintOp>(Assert.Single(scene.Ops));

        AssertDrawnAt(OnSource(Shapes.Bent, op.Placement), scene, View(49.95, -1.05, 50.15, -0.85));
    }

    [Fact]
    public void Renderer_DrawsAnAreaSymbolAtItsAnchorInView()
    {
        var scene = new VectorSceneBuilder { ResolveColor = static _ => Red }.Build(
            [new PointInstruction { FeatureReference = "square", SymbolReference = "S" }],
            new Shapes());
        var op = Assert.IsType<PointPaintOp>(Assert.Single(scene.Ops));
        var viewport = View(49.95, -1.05, 50.15, -0.85);

        Assert.Equal(op.World, ViewExtent.Of(viewport).Anchor(op));
        AssertDrawnAt(op.World, scene, viewport);
    }

    [Fact]
    public void Renderer_DrawsAVisiblePartsSymbolWhereTheViewExtentPutsIt()
    {
        var scene = new VectorSceneBuilder { ResolveColor = static _ => Red }.Build(
            [new PointInstruction { FeatureReference = "square", SymbolReference = "S" }],
            new Shapes());
        var op = Assert.IsType<PointPaintOp>(Assert.Single(scene.Ops));
        // Only the square's south-west corner is in view.
        var viewport = View(49.99, -1.01, 50.03, -0.97);

        var anchor = ViewExtent.Of(viewport).Anchor(op);

        Assert.NotEqual(op.World, anchor);
        AssertDrawnAt(anchor, scene, viewport);
    }

    private static void AssertSame((double X, double Y) expected, (double X, double Y) actual)
    {
        Assert.Equal(expected.X, actual.X, 6);
        Assert.Equal(expected.Y, actual.Y, 6);
    }
}
