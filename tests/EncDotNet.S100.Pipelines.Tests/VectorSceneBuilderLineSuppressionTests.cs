using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Rendering.Scene;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// S-100 Part 9 line suppression in <see cref="VectorSceneBuilder"/> (#1126):
/// where lines of different features share an edge, only the one drawn last
/// is drawn there.
/// </summary>
public sealed class VectorSceneBuilderLineSuppressionTests
{
    private static readonly GeoPosition SW = new(50, -1);
    private static readonly GeoPosition S = new(50, -0.9);
    private static readonly GeoPosition SE = new(50, -0.8);
    private static readonly GeoPosition NE = new(50.1, -0.8);
    private static readonly GeoPosition N = new(50.1, -0.9);
    private static readonly GeoPosition NW = new(50.1, -1);

    /// <summary>
    /// Two squares, "west" and "east", sharing the edge S–N; "edge" is a line
    /// feature along it, and "path" a line that crosses it lengthways.
    /// </summary>
    private sealed class Squares : IFeatureGeometryProvider
    {
        public FeatureGeometry? GetGeometry(string featureReference) => featureReference switch
        {
            "west" => new() { Type = GeometryType.Surface, Coordinates = [SW, S, N, NW, SW] },
            "east" => new() { Type = GeometryType.Surface, Coordinates = [S, SE, NE, N, S] },
            "edge" => new() { Type = GeometryType.Curve, Coordinates = [N, S] },
            "path" => new() { Type = GeometryType.Curve, Coordinates = [SW, S, N, NE] },
            _ => null,
        };
    }

    private static List<LinePaintOp> Lines(params DrawingInstruction[] instructions)
    {
        var builder = new VectorSceneBuilder { ResolveColor = static _ => new RgbaColor(0, 0, 0, 255) };
        return builder.Build(instructions, new Squares()).Ops.OfType<LinePaintOp>().ToList();
    }

    private static IReadOnlyList<(double X, double Y)> World(params GeoPosition[] positions) =>
        positions.Select(p => WebMercator.FromLonLat(p.Longitude, p.Latitude)).ToList();

    private static LineInstruction Line(string feature, int priority, bool suppression = true) => new()
    {
        FeatureReference = feature,
        LineColor = "CHBLK",
        LineWidth = 0.32,
        DrawingPriority = priority,
        Suppression = suppression,
    };

    [Fact]
    public void HigherPriorityLine_HidesTheSharedEdgeOfTheLowerOne()
    {
        var ops = Lines(Line("west", 3), Line("east", 6));

        // West keeps its other three sides, joined round the ring's start.
        var west = Assert.Single(ops, o => o.FeatureReference == "west");
        Assert.Equal(World(N, NW, SW, S), west.World);
        var east = Assert.Single(ops, o => o.FeatureReference == "east");
        Assert.Equal(World(S, SE, NE, N, S), east.World);
    }

    [Fact]
    public void LineFeatureOnABoundary_HidesTheAreaBorderThere()
    {
        var ops = Lines(Line("west", 3), Line("edge", 8));

        Assert.Equal(World(N, NW, SW, S), Assert.Single(ops, o => o.FeatureReference == "west").World);
        Assert.Equal(World(N, S), Assert.Single(ops, o => o.FeatureReference == "edge").World);
    }

    [Fact]
    public void LineUnderAHigherPriorityBorder_IsHiddenThere()
    {
        var ops = Lines(Line("west", 8), Line("edge", 3));

        Assert.DoesNotContain(ops, o => o.FeatureReference == "edge");
        Assert.Equal(World(SW, S, N, NW, SW), Assert.Single(ops, o => o.FeatureReference == "west").World);
    }

    [Fact]
    public void EqualPriority_TheLaterLineWins()
    {
        var ops = Lines(Line("east", 5), Line("west", 5));

        Assert.Equal(World(SW, S, N, NW, SW), Assert.Single(ops, o => o.FeatureReference == "west").World);
        Assert.Equal(World(S, SE, NE, N), Assert.Single(ops, o => o.FeatureReference == "east").World);
    }

    [Fact]
    public void UnsuppressedLine_NeitherSuppressesNorIsSuppressed()
    {
        var ops = Lines(Line("west", 3), Line("edge", 8, suppression: false), Line("east", 6, suppression: false));

        Assert.Equal(World(SW, S, N, NW, SW), Assert.Single(ops, o => o.FeatureReference == "west").World);
        Assert.Equal(World(S, SE, NE, N, S), Assert.Single(ops, o => o.FeatureReference == "east").World);
        Assert.Single(ops, o => o.FeatureReference == "edge");
    }

    [Fact]
    public void LinesOfOneFeature_DoNotSuppressEachOther()
    {
        // An outline then a narrower colour on the same geometry.
        var ops = Lines(Line("west", 3), Line("west", 3));

        Assert.Equal(2, ops.Count);
        Assert.All(ops, o => Assert.Equal(World(SW, S, N, NW, SW), o.World));
    }

    [Fact]
    public void SpatialReferenceLine_SuppressesTheMatchingEdge()
    {
        // A line drawn on one curve of the boundary, as SpatialReference does.
        var contour = new LineInstruction
        {
            FeatureReference = "east",
            LineColor = "DEPSC",
            DrawingPriority = 6,
            CoordinatesOverride = [S, N],
        };

        var ops = Lines(Line("west", 3), contour);

        Assert.Equal(World(N, NW, SW, S), Assert.Single(ops, o => o.FeatureReference == "west").World);
    }

    [Fact]
    public void SuppressingLineHiddenAtSomeScales_LeavesTheLowerLineDrawn()
    {
        var narrow = new LineInstruction
        {
            FeatureReference = "edge",
            LineColor = "CHBLK",
            DrawingPriority = 8,
            ScaleMinimum = 22_000,
        };

        var ops = Lines(Line("west", 3), narrow);

        Assert.Equal(World(SW, S, N, NW, SW), Assert.Single(ops, o => o.FeatureReference == "west").World);
    }

    [Fact]
    public void SuppressedMidLine_SplitsTheLineIntoRuns()
    {
        var ops = Lines(Line("path", 3), Line("edge", 8));

        var path = ops.Where(o => o.FeatureReference == "path").Select(o => o.World).ToList();
        Assert.Equal([World(SW, S), World(N, NE)], path);
    }
}
