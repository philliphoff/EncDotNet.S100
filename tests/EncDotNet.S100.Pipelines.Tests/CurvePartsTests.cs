using EncDotNet.S100.DataModel;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Rendering.Scene;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// A curve feature made of parts that do not meet is drawn and queried part by
/// part, not joined into one polyline with a segment across each gap (issue #643).
/// </summary>
public sealed class CurvePartsTests
{
    private static GeoPosition P(double lat, double lon) => new(lat, lon);

    // Two parts: (0,0)→(0,1)→(0,2) chained from two curves that meet at (0,1),
    // then a separate (5,0)→(5,1) that does not meet the first.
    private static readonly IReadOnlyList<IReadOnlyList<GeoPosition>> TwoPartCurves =
    [
        [P(0, 0), P(0, 1)],
        [P(0, 1), P(0, 2)],
        [P(5, 0), P(5, 1)],
    ];

    [Fact]
    public void Join_ChainsCurvesThatMeet_AndSplitsWhereTheyDoNot()
    {
        var parts = CurveParts.Join(TwoPartCurves);

        Assert.Equal(2, parts.Count);
        Assert.Equal(new[] { P(0, 0), P(0, 1), P(0, 2) }, parts[0]);
        Assert.Equal(new[] { P(5, 0), P(5, 1) }, parts[1]);
    }

    [Fact]
    public void Join_SkipsEmptyCurves()
    {
        var parts = CurveParts.Join([[], [P(0, 0), P(0, 1)], [], [P(0, 1), P(1, 1)]]);

        Assert.Equal(new[] { P(0, 0), P(0, 1), P(1, 1) }, Assert.Single(parts));
    }

    [Fact]
    public void Provider_DisconnectedCurves_ExposeParts_AndKeepTheJoinedCoordinates()
    {
        var geometry = new FeatureGeometryProvider<Feature>([CurveFeature(TwoPartCurves)]).GetGeometry("1")!;

        Assert.Equal(2, geometry.Parts.Count);
        Assert.Equal(TwoPartCurves.SelectMany(c => c), geometry.Coordinates);
    }

    [Fact]
    public void Provider_ConnectedCurves_HaveNoParts()
    {
        var geometry = new FeatureGeometryProvider<Feature>([CurveFeature(TwoPartCurves.Take(2).ToList())]).GetGeometry("1")!;

        Assert.Empty(geometry.Parts);
    }

    [Fact]
    public void Feature_Curves_AreWhatTheQueryToolsSee()
    {
        IS100Feature parted = CurveFeature(TwoPartCurves);
        IS100Feature single = new Feature
        {
            Id = 2,
            FeatureType = "DepthContour",
            GeometryType = GeometryType.Curve,
            Coordinates = [P(0, 0), P(0, 1)],
            Attributes = new Dictionary<string, object?>(),
        };

        Assert.Equal(3, parted.Curves.Count);
        Assert.Equal(new[] { P(0, 0), P(0, 1) }, Assert.Single(single.Curves));
    }

    [Fact]
    public void SceneBuilder_DrawsEachPartAsItsOwnLine()
    {
        var scene = Builder().Build(
            [new LineInstruction { FeatureReference = "1", LineColor = "DEPCN", LineWidth = 0.32 }],
            new FeatureGeometryProvider<Feature>([CurveFeature(TwoPartCurves)]));

        var lines = scene.Ops.OfType<LinePaintOp>().ToList();
        Assert.Equal(2, lines.Count);
        Assert.Equal(3, lines[0].World.Count);
        Assert.Equal(2, lines[1].World.Count);
        // No line reaches from the first part's end to the second part's start.
        var gapStart = WebMercator.FromLonLat(2, 0);
        var gapEnd = WebMercator.FromLonLat(0, 5);
        Assert.DoesNotContain(lines, l => l.World.Contains(gapStart) && l.World.Contains(gapEnd));
    }

    [Fact]
    public void SceneBuilder_PlacesTextAlongTheLongestPart()
    {
        // The longest part (0,0)→(0,2) holds the midpoint at (0,1); the joined
        // coordinates' midpoint would fall on the gap.
        var scene = Builder().Build(
            [new TextInstruction { FeatureReference = "1", Text = "10", LinePlacementPosition = 0.5 }],
            new FeatureGeometryProvider<Feature>([CurveFeature(TwoPartCurves)]));

        var text = Assert.IsType<TextPaintOp>(Assert.Single(scene.Ops));
        var expected = WebMercator.FromLonLat(1, 0);
        Assert.Equal(expected.X, text.World.X, 6);
        Assert.Equal(expected.Y, text.World.Y, 6);
    }

    private static VectorSceneBuilder Builder() => new()
    {
        ResolveColor = static _ => new RgbaColor(0, 0, 0, 255),
    };

    private static Feature CurveFeature(IReadOnlyList<IReadOnlyList<GeoPosition>> curves) => new()
    {
        Id = 1,
        FeatureType = "DepthContour",
        GeometryType = GeometryType.Curve,
        Coordinates = curves.SelectMany(c => c).ToList(),
        Curves = curves,
        Attributes = new Dictionary<string, object?>(),
    };
}
