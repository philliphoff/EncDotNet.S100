using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Rendering.Scene;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// <see cref="LineEdges"/> (#1509): line instructions handed out per edge,
/// as geographic data, with S-100 Part 9 line suppression already applied.
/// </summary>
public sealed class LineEdgesTests
{
    private static readonly GeoPosition SW = new(50, -1);
    private static readonly GeoPosition S = new(50, -0.9);
    private static readonly GeoPosition SE = new(50, -0.8);
    private static readonly GeoPosition NE = new(50.1, -0.8);
    private static readonly GeoPosition N = new(50.1, -0.9);
    private static readonly GeoPosition NW = new(50.1, -1);

    /// <summary>
    /// Two squares, "west" and "east", sharing the edge S–N; "edge" is a line
    /// feature along it; "split" is a two-part curve.
    /// </summary>
    private sealed class Squares : IFeatureGeometryProvider
    {
        public FeatureGeometry? GetGeometry(string featureReference) => featureReference switch
        {
            "west" => new() { Type = GeometryType.Surface, Coordinates = [SW, S, N, NW, SW] },
            "east" => new() { Type = GeometryType.Surface, Coordinates = [S, SE, NE, N, S] },
            "edge" => new() { Type = GeometryType.Curve, Coordinates = [N, S] },
            "split" => new()
            {
                Type = GeometryType.Curve,
                Coordinates = [SW, S, N, NE],
                Parts = [[SW, S], [N, NE]],
            },
            _ => null,
        };
    }

    private static LineInstruction Dashed(string feature, int priority, bool suppression = true) => new()
    {
        FeatureReference = feature,
        LineColor = "CHGRD",
        LineWidth = 0.32,
        Dashes = [(0, 3.6)],
        DashIntervalLength = 5.4,
        DrawingPriority = priority,
        Suppression = suppression,
    };

    private static IReadOnlyList<LineEdge> Resolve(params DrawingInstruction[] instructions) =>
        LineEdges.Resolve(instructions, new Squares());

    private static int Count(IEnumerable<LineEdge> edges, GeoPosition a, GeoPosition b) =>
        edges.Sum(e => e.Coordinates.Zip(e.Coordinates.Skip(1))
            .Count(s => (s.First == a && s.Second == b) || (s.First == b && s.Second == a)));

    [Fact]
    public void AdjacentAreasSharingADashedBoundary_YieldOneEdgeInstructionThere()
    {
        var west = Dashed("west", 3);
        var east = Dashed("east", 6);

        var edges = Resolve(west, east);

        // The shared side S–N is handed out once, by the higher priority.
        Assert.Equal(1, Count(edges, S, N));
        var shared = Assert.Single(edges, e => Count([e], S, N) == 1);
        Assert.Same(east, shared.Instruction);

        // West keeps its other three sides, as one run joined round the ring.
        var westEdge = Assert.Single(edges, e => ReferenceEquals(e.Instruction, west));
        Assert.Equal([N, NW, SW, S], westEdge.Coordinates);
        var eastEdge = Assert.Single(edges, e => ReferenceEquals(e.Instruction, east));
        Assert.Equal([S, SE, NE, N, S], eastEdge.Coordinates);
    }

    [Fact]
    public void Edges_AreInDrawOrder()
    {
        var edges = Resolve(Dashed("east", 6), Dashed("west", 3));

        Assert.Equal(["west", "east"], edges.Select(e => e.Instruction.FeatureReference));
    }

    [Fact]
    public void LineFeatureOnABoundary_TakesTheEdgeFromBothAreas()
    {
        var edge = Dashed("edge", 8);

        var edges = Resolve(Dashed("west", 3), Dashed("east", 6), edge);

        Assert.Equal(1, Count(edges, S, N));
        Assert.Same(edge, Assert.Single(edges, e => Count([e], S, N) == 1).Instruction);
    }

    [Fact]
    public void UnsuppressedLine_IsHandedOutWhole()
    {
        var edges = Resolve(Dashed("west", 3, suppression: false), Dashed("east", 6));

        Assert.Equal(2, Count(edges, S, N));
    }

    [Fact]
    public void FullySuppressedLine_GivesNoEdge()
    {
        var edges = Resolve(Dashed("edge", 3), Dashed("west", 8));

        Assert.DoesNotContain(edges, e => e.Instruction.FeatureReference == "edge");
    }

    [Fact]
    public void MultiPartCurve_GivesOneEdgePerPart()
    {
        var edges = Resolve(Dashed("split", 3));

        Assert.Equal(2, edges.Count);
        Assert.Equal([SW, S], edges[0].Coordinates);
        Assert.Equal([N, NE], edges[1].Coordinates);
    }

    [Fact]
    public void NonLineInstructionsAndMissingGeometry_AreLeftOut()
    {
        var edges = Resolve(
            new AreaInstruction { FeatureReference = "west", FillColor = "DEPDW" },
            Dashed("nowhere", 3));

        Assert.Empty(edges);
    }

    [Fact]
    public void Edges_MatchTheScenesLineOps()
    {
        DrawingInstruction[] instructions = [Dashed("west", 3), Dashed("east", 6), Dashed("edge", 4)];
        var builder = new VectorSceneBuilder { ResolveColor = static _ => new RgbaColor(0, 0, 0, 255) };
        var sceneLines = builder.Build(instructions, new Squares()).Ops.OfType<LinePaintOp>()
            .Select(o => o.World)
            .ToList();

        var edges = LineEdges.Resolve(instructions, new Squares())
            .Select(e => e.Coordinates.Select(p => WebMercator.FromLonLat(p.Longitude, p.Latitude)).ToList())
            .ToList();

        Assert.Equal(sceneLines.Count, edges.Count);
        for (int i = 0; i < edges.Count; i++)
            Assert.Equal(sceneLines[i], edges[i]);
    }
}
