using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Rendering.Scene;
using NetTopologySuite.Geometries;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// <see cref="PatternRegions"/> (#1510): area patterns handed out as data,
/// with S-100 Part 9 pattern priority clipping already applied.
/// </summary>
public sealed class PatternRegionsTests
{
    private static readonly GeometryFactory Factory = new();

    /// <summary>
    /// Surfaces in a 10° square: "quality" covers its left half, "diamond"
    /// the whole square, "land" its bottom fifth, "corner" the top-left
    /// quarter, "pair" the right half as two parts.
    /// </summary>
    private sealed class Areas : IFeatureGeometryProvider
    {
        public FeatureGeometry? GetGeometry(string featureReference) => featureReference switch
        {
            "quality" => Surface(Rectangle(0, 0, 5, 10)),
            "diamond" => Surface(Rectangle(0, 0, 10, 10)),
            "land" => Surface(Rectangle(0, 0, 10, 2)),
            "corner" => Surface(Rectangle(0, 5, 5, 10)),
            "pair" => new()
            {
                Type = GeometryType.Surface,
                Coordinates = [.. Rectangle(5, 0, 10, 5), .. Rectangle(5, 5, 10, 10)],
                Parts = [Rectangle(5, 0, 10, 5), Rectangle(5, 5, 10, 10)],
            },
            _ => null,
        };

        private static FeatureGeometry Surface(IReadOnlyList<GeoPosition> ring) =>
            new() { Type = GeometryType.Surface, Coordinates = ring };
    }

    private static GeoPosition[] Rectangle(double lon0, double lat0, double lon1, double lat1) =>
        [new(lat0, lon0), new(lat0, lon1), new(lat1, lon1), new(lat1, lon0), new(lat0, lon0)];

    private static AreaInstruction Pattern(string feature, string pattern, int priority) => new()
    {
        FeatureReference = feature,
        AreaFillReference = pattern,
        DrawingPriority = priority,
    };

    private static AreaInstruction Fill(string feature, int priority, double? transparency = null) => new()
    {
        FeatureReference = feature,
        FillColor = "LANDA",
        Transparency = transparency,
        DrawingPriority = priority,
    };

    private static IReadOnlyList<PatternRegion> Resolve(params DrawingInstruction[] instructions) =>
        PatternRegions.Resolve(instructions, new Areas());

    private static Point At(double lon, double lat)
    {
        var (x, y) = WebMercator.FromLonLat(lon, lat);
        return Factory.CreatePoint(new Coordinate(x, y));
    }

    private static Geometry Square(double lon0, double lat0, double lon1, double lat1)
    {
        var (x0, y0) = WebMercator.FromLonLat(lon0, lat0);
        var (x1, y1) = WebMercator.FromLonLat(lon1, lat1);
        return Factory.ToGeometry(new Envelope(x0, x1, y0, y1));
    }

    private static PatternRegion Region(IEnumerable<PatternRegion> regions, string pattern) =>
        Assert.Single(regions, r => r.PatternReference == pattern);

    [Fact]
    public void HigherPriorityPattern_WinsWhereTheyOverlap()
    {
        var regions = Resolve(Pattern("diamond", "DIAMOND1", 3), Pattern("quality", "DQUALB01", 6));

        var diamond = Region(regions, "DIAMOND1");
        var quality = Region(regions, "DQUALB01");

        // DIAMOND1 keeps only the right half; the left half is DQUALB01's.
        Assert.True(diamond.Area.EqualsTopologically(Square(5, 0, 10, 10)));
        Assert.True(quality.Area.EqualsTopologically(Square(0, 0, 5, 10)));
        Assert.Equal(0, diamond.Area.Intersection(quality.Area).Area, 6);
    }

    [Fact]
    public void Regions_AreInAscendingPriority_WithTheirInstructions()
    {
        var quality = Pattern("quality", "DQUALB01", 6);
        var diamond = Pattern("diamond", "DIAMOND1", 3);

        var regions = Resolve(quality, diamond);

        Assert.Equal(["DIAMOND1", "DQUALB01"], regions.Select(r => r.PatternReference));
        Assert.Equal([3, 6], regions.Select(r => r.DrawingPriority));
        Assert.Same(diamond, Assert.Single(regions[0].Instructions));
        Assert.Same(quality, Assert.Single(regions[1].Instructions));
    }

    [Fact]
    public void OpaqueColourFill_HidesPatterns()
    {
        var diamond = Region(Resolve(Fill("land", 1), Pattern("diamond", "DIAMOND1", 3)), "DIAMOND1");

        Assert.True(diamond.Area.EqualsTopologically(Square(0, 2, 10, 10)));
    }

    [Fact]
    public void TranslucentColourFill_DoesNotHidePatterns()
    {
        var diamond = Region(Resolve(Fill("land", 1, transparency: 0.5), Pattern("diamond", "DIAMOND1", 3)), "DIAMOND1");

        Assert.True(diamond.Area.EqualsTopologically(Square(0, 0, 10, 10)));
    }

    [Fact]
    public void ColourFillOfAPatternedFeature_DoesNotHideItsPattern()
    {
        var diamond = Region(Resolve(Fill("diamond", 1), Pattern("diamond", "DIAMOND1", 3)), "DIAMOND1");

        Assert.True(diamond.Area.EqualsTopologically(Square(0, 0, 10, 10)));
    }

    [Fact]
    public void FullyHiddenPattern_GivesNoRegion()
    {
        var regions = Resolve(Pattern("corner", "DIAMOND1", 3), Pattern("quality", "DQUALB01", 6));

        Assert.Equal("DQUALB01", Assert.Single(regions).PatternReference);
    }

    [Fact]
    public void SamePatternAndPriority_IsOneRegion_ListingEachInstructionOnce()
    {
        var quality = Pattern("quality", "DQUALB01", 6);
        var pair = Pattern("pair", "dqualb01", 6);

        var region = Assert.Single(Resolve(quality, pair));

        Assert.Equal([quality, pair], region.Instructions);
        Assert.True(region.Area.EqualsTopologically(Square(0, 0, 10, 10)));
    }

    [Fact]
    public void OverlappingAreasOfOneGroup_AreStillClipped()
    {
        // "corner" lies inside "diamond": their DIAMOND1 group overlaps itself,
        // which once left the whole group unclipped under DQUALB01 and land.
        var regions = Resolve(
            Fill("land", 1),
            Pattern("diamond", "DIAMOND1", 3),
            Pattern("corner", "DIAMOND1", 3),
            Pattern("quality", "DQUALB01", 6));

        var diamond = Region(regions, "DIAMOND1");
        Assert.True(diamond.Area.EqualsTopologically(Square(5, 2, 10, 10)));
        Assert.True(diamond.Area.IsValid);
        Assert.Equal(2, diamond.Instructions.Count);
    }

    [Fact]
    public void SamePatternAtDifferentPriorities_IsClippedLikeAnyOtherPattern()
    {
        var regions = Resolve(Pattern("diamond", "DQUALB01", 3), Pattern("quality", "DQUALB01", 6));

        Assert.Equal(2, regions.Count);
        Assert.True(regions[0].Area.EqualsTopologically(Square(5, 0, 10, 10)));
        Assert.True(regions[1].Area.EqualsTopologically(Square(0, 0, 5, 10)));
    }

    [Fact]
    public void OtherInstructionsAndMissingGeometry_AreLeftOut()
    {
        var regions = Resolve(
            new LineInstruction { FeatureReference = "quality", LineColor = "CHBLK", LineWidth = 0.32, DrawingPriority = 9 },
            new PointInstruction { FeatureReference = "quality", SymbolReference = "BOYCAN01", DrawingPriority = 9 },
            Pattern("missing", "DIAMOND1", 9),
            Pattern("quality", "DQUALB01", 6));

        Assert.Equal("DQUALB01", Assert.Single(regions).PatternReference);
    }

    [Fact]
    public void Regions_MatchTheScenesPatternOps()
    {
        DrawingInstruction[] instructions =
        [
            Fill("land", 1),
            Fill("diamond", 2),
            Pattern("diamond", "DIAMOND1", 3),
            Pattern("corner", "DIAMOND1", 3),
            Pattern("pair", "DQUALA1", 4),
            Pattern("quality", "DQUALB01", 6),
            new LineInstruction { FeatureReference = "quality", LineColor = "CHBLK", LineWidth = 0.32, DrawingPriority = 6 },
        ];
        var builder = new VectorSceneBuilder
        {
            ResolveColor = static _ => new RgbaColor(200, 200, 200, 255),
            PatternResolver = static _ => [1, 2, 3, 4],
        };
        var scenePatterns = builder.Build(instructions, new Areas()).Ops.OfType<PatternAreaPaintOp>().ToList();

        var regions = PatternRegions.Resolve(instructions, new Areas());

        Assert.Equal(
            scenePatterns.Select(o => o.PatternReference).Distinct(),
            regions.Select(r => r.PatternReference));
        foreach (var region in regions)
        {
            var drawn = Factory.BuildGeometry(scenePatterns
                .Where(o => o.PatternReference == region.PatternReference)
                .Select(ToPolygon));
            Assert.True(drawn.Union().EqualsTopologically(region.Area.Union()), region.PatternReference);
        }

        // Nothing is drawn under land, and no two patterns share a spot.
        Assert.All(regions, r => Assert.False(r.Area.Contains(At(7, 1))));
        for (int i = 0; i < regions.Count; i++)
        {
            for (int j = i + 1; j < regions.Count; j++)
                Assert.Equal(0, regions[i].Area.Intersection(regions[j].Area).Area, 6);
        }
    }

    private static Polygon ToPolygon(PatternAreaPaintOp op)
    {
        LinearRing Ring(IReadOnlyList<(double X, double Y)> ring) =>
            Factory.CreateLinearRing([.. ring.Select(p => new Coordinate(p.X, p.Y)), new Coordinate(ring[0].X, ring[0].Y)]);
        return Factory.CreatePolygon(Ring(op.WorldShell), [.. op.WorldHoles.Select(Ring)]);
    }
}
