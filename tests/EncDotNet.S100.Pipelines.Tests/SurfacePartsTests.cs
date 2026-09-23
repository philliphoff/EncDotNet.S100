using EncDotNet.S100.DataModel;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Rendering.Scene;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// A feature with several surfaces is filled, outlined and labelled surface by
/// surface, each with its own holes, not as one joined ring (issue #643).
/// </summary>
public sealed class SurfacePartsTests
{
    private static GeoPosition P(double lat, double lon) => new(lat, lon);

    // A: a 2°×2° square with a hole; B: a small square 5° east.
    private static readonly GeoPosition[] RingA = [P(0, 0), P(2, 0), P(2, 2), P(0, 2), P(0, 0)];
    private static readonly GeoPosition[] HoleA = [P(0.5, 0.5), P(1, 0.5), P(1, 1), P(0.5, 1), P(0.5, 0.5)];
    private static readonly GeoPosition[] RingB = [P(0, 5), P(1, 5), P(1, 6), P(0, 6), P(0, 5)];

    private static Feature TwoSurfaces() => new()
    {
        Id = 1,
        FeatureType = "SeaAreaNamedWaterArea",
        GeometryType = GeometryType.Surface,
        Coordinates = [.. RingA, .. RingB],
        InteriorRings = [HoleA],
        SurfaceParts = [new SurfacePart(RingA, [HoleA]), new SurfacePart(RingB, [])],
        Attributes = new Dictionary<string, object?>(),
    };

    private static IFeatureGeometryProvider Provider() => new FeatureGeometryProvider<Feature>([TwoSurfaces()]);

    [Fact]
    public void Provider_ExposesEachSurfaceWithItsOwnHoles()
    {
        var geometry = Provider().GetGeometry("1")!;

        Assert.Equal(new[] { RingA, RingB }, geometry.Parts.Select(p => p.ToArray()));
        Assert.Single(geometry.PartInteriorRings[0]);
        Assert.Empty(geometry.PartInteriorRings[1]);
    }

    [Fact]
    public void Surfaces_DefaultsToOneSurfaceForASingleSurfaceFeature()
    {
        IS100Feature single = new Feature
        {
            Id = 2,
            FeatureType = "DepthArea",
            GeometryType = GeometryType.Surface,
            Coordinates = RingA,
            InteriorRings = [HoleA],
            Attributes = new Dictionary<string, object?>(),
        };

        var surface = Assert.Single(single.Surfaces);
        Assert.Same(RingA, surface.ExteriorRing);
        Assert.Single(surface.InteriorRings);
    }

    [Fact]
    public void SceneBuilder_FillsEachSurfaceWithItsOwnHoles()
    {
        var scene = Builder().Build(
            [new AreaInstruction { FeatureReference = "1", FillColor = "DEPVS" }],
            Provider());

        var areas = scene.Ops.OfType<AreaPaintOp>().ToList();
        Assert.Equal(2, areas.Count);
        Assert.Equal(RingA.Length, areas[0].WorldShell.Count);
        Assert.Single(areas[0].WorldHoles);
        Assert.Equal(RingB.Length, areas[1].WorldShell.Count);
        Assert.Empty(areas[1].WorldHoles);
    }

    [Fact]
    public void SceneBuilder_OutlinesEachSurfaceOnItsOwn()
    {
        var scene = Builder().Build(
            [new LineInstruction { FeatureReference = "1", LineColor = "CHGRD", LineWidth = 0.32 }],
            Provider());

        var lines = scene.Ops.OfType<LinePaintOp>().ToList();
        Assert.Equal(new[] { RingA.Length, RingB.Length }, lines.Select(l => l.World.Count));
    }

    [Fact]
    public void SceneBuilder_LabelsTheLargestSurface()
    {
        var scene = Builder().Build(
            [new TextInstruction { FeatureReference = "1", Text = "Sound" }],
            Provider());

        // Ring A's vertex centroid, (1, 1); the joined rings' would be
        // (0.6, 3.1), between the two surfaces.
        var text = Assert.IsType<TextPaintOp>(Assert.Single(scene.Ops));
        var expected = WebMercator.FromLonLat(1, 1);
        Assert.Equal(expected.X, text.World.X, 6);
        Assert.Equal(expected.Y, text.World.Y, 6);
    }

    private static VectorSceneBuilder Builder() => new()
    {
        ResolveColor = static _ => new RgbaColor(0, 0, 0, 255),
    };
}
