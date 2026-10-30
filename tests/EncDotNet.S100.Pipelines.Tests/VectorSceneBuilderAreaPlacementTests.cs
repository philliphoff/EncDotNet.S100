using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Rendering.Scene;
using NetTopologySuite.Geometries;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// S-100 Part 9 <c>AreaPlacement</c> (#1126): a surface's point symbols and
/// text go at a representative point inside it, and with <c>VisibleParts</c>
/// move into the part of the surface in view when that point is out of view.
/// </summary>
public sealed class VectorSceneBuilderAreaPlacementTests
{
    /// <summary>
    /// "square": 50–50.1 N, 1–0.9 W. "ell": an L whose vertex mean lies
    /// outside it (the corner square is missing). "line": a curve.
    /// </summary>
    private sealed class Shapes : IFeatureGeometryProvider
    {
        public FeatureGeometry? GetGeometry(string featureReference) => featureReference switch
        {
            "square" => new()
            {
                Type = GeometryType.Surface,
                Coordinates = [new(50, -1), new(50, -0.9), new(50.1, -0.9), new(50.1, -1), new(50, -1)],
            },
            "ell" => new()
            {
                Type = GeometryType.Surface,
                Coordinates =
                [
                    new(50, -1), new(50, -0.7), new(50.02, -0.7), new(50.02, -0.98),
                    new(50.3, -0.98), new(50.3, -1), new(50, -1),
                ],
            },
            "line" => new() { Type = GeometryType.Curve, Coordinates = [new(50, -1), new(50, -0.9)] },
            _ => null,
        };
    }

    private static List<PaintOp> Build(params DrawingInstruction[] instructions) =>
        new VectorSceneBuilder { ResolveColor = static _ => new RgbaColor(0, 0, 0, 255) }
            .Build(instructions, new Shapes()).Ops.ToList();

    private static (double X, double Y) World(double lat, double lon) => WebMercator.FromLonLat(lon, lat);

    private static Polygon Footprint(string feature)
    {
        var ring = new Shapes().GetGeometry(feature)!.Coordinates
            .Select(p => World(p.Latitude, p.Longitude))
            .Select(w => new Coordinate(w.X, w.Y))
            .ToArray();
        return new GeometryFactory().CreatePolygon(ring);
    }

    private static bool Inside(string feature, (double X, double Y) world) =>
        Footprint(feature).Contains(new GeometryFactory().CreatePoint(new Coordinate(world.X, world.Y)));

    [Fact]
    public void AreaSymbol_GoesAtTheCentreOfTheArea()
    {
        var op = Assert.IsType<PointPaintOp>(Assert.Single(Build(
            new PointInstruction { FeatureReference = "square", SymbolReference = "CTNARE51" })));

        var (westX, southY) = World(50, -1);
        var (eastX, northY) = World(50.1, -0.9);
        Assert.Equal((westX + eastX) / 2, op.World.X, 1);
        Assert.Equal((southY + northY) / 2, op.World.Y, 1);
    }

    [Fact]
    public void AreaSymbolAndText_StayInsideAConcaveArea()
    {
        var ops = Build(
            new PointInstruction { FeatureReference = "ell", SymbolReference = "CTNARE51" },
            new TextInstruction { FeatureReference = "ell", Text = "Ell" });

        Assert.True(Inside("ell", Assert.IsType<PointPaintOp>(ops[0]).World));
        Assert.True(Inside("ell", Assert.IsType<TextPaintOp>(ops[1]).World));
    }

    [Fact]
    public void VisibleParts_CarriesTheAreaForKeepingItInView()
    {
        var ops = Build(
            new PointInstruction { FeatureReference = "square", SymbolReference = "S" },
            new TextInstruction { FeatureReference = "square", Text = "T" });

        Assert.NotNull(Assert.IsType<PointPaintOp>(ops[0]).PlacementArea);
        Assert.NotNull(Assert.IsType<TextPaintOp>(ops[1]).PlacementArea);
    }

    [Fact]
    public void Geographic_FixesTheAnchor()
    {
        var ops = Build(
            new PointInstruction { FeatureReference = "square", SymbolReference = "S", AreaPlacement = AreaPlacementMode.Geographic },
            new TextInstruction { FeatureReference = "square", Text = "T", AreaPlacement = AreaPlacementMode.Geographic });

        Assert.Null(Assert.IsType<PointPaintOp>(ops[0]).PlacementArea);
        Assert.Null(Assert.IsType<TextPaintOp>(ops[1]).PlacementArea);
    }

    [Fact]
    public void CurvesAndAugmentedPoints_KeepTheirAnchors()
    {
        var ops = Build(
            new PointInstruction { FeatureReference = "line", SymbolReference = "S" },
            new PointInstruction { FeatureReference = "square", SymbolReference = "S", CoordinateOverride = new GeoPosition(50.01, -0.99) });

        var onLine = Assert.IsType<PointPaintOp>(ops[0]);
        Assert.Equal(World(50, -1), onLine.World);
        Assert.Null(onLine.PlacementArea);
        var augmented = Assert.IsType<PointPaintOp>(ops[1]);
        Assert.Equal(World(50.01, -0.99), augmented.World);
        Assert.Null(augmented.PlacementArea);
    }

    [Fact]
    public void InView_TheAnchorStays()
    {
        var op = Assert.IsType<PointPaintOp>(Assert.Single(Build(
            new PointInstruction { FeatureReference = "square", SymbolReference = "S" })));
        var (minX, minY) = World(49.9, -1.1);
        var (maxX, maxY) = World(50.2, -0.8);

        Assert.Equal(op.World, new ViewExtent(minX, minY, maxX, maxY).Anchor(op));
    }

    [Fact]
    public void AnchorOutOfView_MovesIntoTheVisiblePart()
    {
        var op = Assert.IsType<PointPaintOp>(Assert.Single(Build(
            new PointInstruction { FeatureReference = "square", SymbolReference = "S" })));
        // The view shows only the square's south-west corner.
        var (minX, minY) = World(49.9, -1.1);
        var (maxX, maxY) = World(50.02, -0.98);

        var anchor = new ViewExtent(minX, minY, maxX, maxY).Anchor(op);

        // The centre of the visible corner, (50–50.02 N, 1–0.98 W).
        var (westX, southY) = World(50, -1);
        Assert.Equal((westX + maxX) / 2, anchor.X, 1);
        Assert.Equal((southY + maxY) / 2, anchor.Y, 1);
    }

    [Fact]
    public void AreaOutOfView_TheAnchorStays()
    {
        var op = Assert.IsType<TextPaintOp>(Assert.Single(Build(
            new TextInstruction { FeatureReference = "square", Text = "T" })));
        var (minX, minY) = World(51, 1);
        var (maxX, maxY) = World(51.1, 1.1);

        Assert.Equal(op.World, new ViewExtent(minX, minY, maxX, maxY).Anchor(op));
    }

    [Fact]
    public void OverlayIndex_FindsAnAreaSymbolWhoseAnchorIsOutOfView()
    {
        var op = Assert.IsType<PointPaintOp>(Assert.Single(Build(
            new PointInstruction { FeatureReference = "square", SymbolReference = "S" })));
        var index = new OverlaySpatialIndex(new VectorScene([op]));
        var (minX, minY) = World(49.9, -1.1);
        var (maxX, maxY) = World(50.02, -0.98);

        var results = new List<PaintOp>();
        index.Query(minX, minY, maxX, maxY, [], results);

        Assert.Same(op, Assert.Single(results));
    }

    [Fact]
    public void BaseIndex_FindsAnAreaSymbolWhoseAnchorIsOutOfView()
    {
        var op = Assert.IsType<PointPaintOp>(Assert.Single(Build(
            new PointInstruction { FeatureReference = "square", SymbolReference = "S" })));
        var index = new BaseSpatialIndex(new VectorScene([op]), indexAnchors: true);
        var (minX, minY) = World(49.9, -1.1);
        var (maxX, maxY) = World(50.02, -0.98);

        Assert.Same(op, Assert.Single(index.Query(minX, minY, maxX, maxY)));
    }
}
