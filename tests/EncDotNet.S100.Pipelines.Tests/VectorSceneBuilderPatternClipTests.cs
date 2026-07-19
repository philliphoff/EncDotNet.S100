using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Rendering.Scene;
using NetTopologySuite.Geometries;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Verifies that <see cref="VectorSceneBuilder"/> priority-clips the pattern
/// area fills it lowers into the <see cref="VectorScene"/> IR — the same
/// clipping the Mapsui feature path performs via
/// <see cref="PatternPriorityClipper"/>, now shared so the headless Skia backend
/// and the Mapsui TiledScene subsystem clip identically (S-100 Part 9 §11.3).
/// A lower-priority pattern must be clipped away where a higher-priority pattern
/// or an opaque non-patterned solid colour fill covers it.
/// </summary>
public sealed class VectorSceneBuilderPatternClipTests
{
    private static readonly GeometryFactory Factory = new();

    private sealed class DictionaryGeometryProvider : IFeatureGeometryProvider
    {
        private readonly Dictionary<string, FeatureGeometry> _geometries = new(StringComparer.Ordinal);

        public void Add(string featureReference, params (double Lon, double Lat)[] ring)
        {
            _geometries[featureReference] = new FeatureGeometry
            {
                Type = GeometryType.Surface,
                Coordinates = [.. ring.Select(p => new GeoPosition(p.Lat, p.Lon))],
            };
        }

        public FeatureGeometry? GetGeometry(string featureReference) =>
            _geometries.TryGetValue(featureReference, out var g) ? g : null;
    }

    private static (double Lon, double Lat)[] Rectangle(double lon0, double lat0, double lon1, double lat1) =>
        [(lon0, lat0), (lon1, lat0), (lon1, lat1), (lon0, lat1)];

    private static VectorSceneBuilder NewBuilder() => new()
    {
        ResolveColor = static _ => new RgbaColor(200, 200, 200, 255),
        PatternResolver = static _ => [1, 2, 3, 4],
    };

    /// <summary>Reconstructs an NTS polygon from a lowered pattern op's world rings.</summary>
    private static Geometry ToGeometry(PatternAreaPaintOp op)
    {
        var shell = Factory.CreateLinearRing(CloseRing(op.WorldShell));
        if (op.WorldHoles.Count == 0)
            return Factory.CreatePolygon(shell);

        var holes = op.WorldHoles.Select(h => Factory.CreateLinearRing(CloseRing(h))).ToArray();
        return Factory.CreatePolygon(shell, holes);
    }

    private static Coordinate[] CloseRing(IReadOnlyList<(double X, double Y)> ring)
    {
        var coords = new List<Coordinate>(ring.Count + 1);
        foreach (var (x, y) in ring)
            coords.Add(new Coordinate(x, y));
        if (coords.Count > 0 && !coords[0].Equals2D(coords[^1]))
            coords.Add(new Coordinate(coords[0].X, coords[0].Y));
        return [.. coords];
    }

    private static Point At(double lon, double lat)
    {
        var (x, y) = WebMercator.FromLonLat(lon, lat);
        return Factory.CreatePoint(new Coordinate(x, y));
    }

    private static PatternAreaPaintOp SinglePatternOp(VectorScene scene, string patternReference) =>
        Assert.Single(scene.Ops.OfType<PatternAreaPaintOp>(), o => o.PatternReference == patternReference);

    [Fact]
    public void HigherPriorityPattern_ClipsLowerPriorityPattern()
    {
        var geometry = new DictionaryGeometryProvider();
        geometry.Add("low", Rectangle(0, 0, 10, 10));
        geometry.Add("high", Rectangle(0, 0, 5, 10));

        DrawingInstruction[] instructions =
        [
            new AreaInstruction { FeatureReference = "low", AreaFillReference = "PATTERN_LOW", DrawingPriority = 5 },
            new AreaInstruction { FeatureReference = "high", AreaFillReference = "PATTERN_HIGH", DrawingPriority = 10 },
        ];

        var scene = NewBuilder().Build(instructions, geometry);

        var lowGeometry = ToGeometry(SinglePatternOp(scene, "PATTERN_LOW"));
        // The overlap with the higher-priority pattern (left half) is clipped away…
        Assert.False(lowGeometry.Contains(At(2, 5)));
        // …while the non-overlapping right half remains.
        Assert.True(lowGeometry.Contains(At(8, 5)));

        // The higher-priority pattern is not clipped by anything below it.
        var highGeometry = ToGeometry(SinglePatternOp(scene, "PATTERN_HIGH"));
        Assert.True(highGeometry.Contains(At(2, 5)));
    }

    [Fact]
    public void OpaqueSolidFill_ClipsPattern()
    {
        var geometry = new DictionaryGeometryProvider();
        geometry.Add("pattern", Rectangle(0, 0, 10, 10));
        geometry.Add("land", Rectangle(0, 0, 5, 10));

        DrawingInstruction[] instructions =
        [
            new AreaInstruction { FeatureReference = "land", FillColor = "LANDA", DrawingPriority = 1 },
            new AreaInstruction { FeatureReference = "pattern", AreaFillReference = "PATTERN", DrawingPriority = 5 },
        ];

        var scene = NewBuilder().Build(instructions, geometry);

        var patternGeometry = ToGeometry(SinglePatternOp(scene, "PATTERN"));
        // The pattern must not bleed over the opaque solid (land) fill.
        Assert.False(patternGeometry.Contains(At(2, 5)));
        Assert.True(patternGeometry.Contains(At(8, 5)));
    }

    [Fact]
    public void FeatureOwnSolidFill_DoesNotClipItsOwnPattern()
    {
        // A feature carrying both a solid fill and a pattern must not have its own
        // solid fill treated as an exclusion area (mirrors the Mapsui feature path).
        var geometry = new DictionaryGeometryProvider();
        geometry.Add("both", Rectangle(0, 0, 10, 10));

        DrawingInstruction[] instructions =
        [
            new AreaInstruction { FeatureReference = "both", FillColor = "DEPMS", DrawingPriority = 5 },
            new AreaInstruction { FeatureReference = "both", AreaFillReference = "PATTERN", DrawingPriority = 5 },
        ];

        var scene = NewBuilder().Build(instructions, geometry);

        var patternGeometry = ToGeometry(SinglePatternOp(scene, "PATTERN"));
        Assert.True(patternGeometry.Contains(At(5, 5)));
    }

    [Fact]
    public void NoPatternResolver_EmitsNoPatternOps()
    {
        var geometry = new DictionaryGeometryProvider();
        geometry.Add("area", Rectangle(0, 0, 10, 10));

        DrawingInstruction[] instructions =
        [
            new AreaInstruction { FeatureReference = "area", AreaFillReference = "PATTERN", DrawingPriority = 5 },
        ];

        // No PatternResolver: the Mapsui feature path drives its own pattern phase.
        var builder = new VectorSceneBuilder { ResolveColor = static _ => new RgbaColor(0, 0, 0, 255) };
        var scene = builder.Build(instructions, geometry);

        Assert.Empty(scene.Ops.OfType<PatternAreaPaintOp>());
    }

    [Fact]
    public void PatternClipCache_ResultIsUsedInsteadOfRecomputing()
    {
        // The builder must build its pattern ops from the memoizer's returned
        // geometry, not from a fresh clip. A memoizer that returns an empty clip
        // result therefore yields a scene with no pattern ops.
        var geometry = new DictionaryGeometryProvider();
        geometry.Add("pattern", Rectangle(0, 0, 10, 10));

        DrawingInstruction[] instructions =
        [
            new AreaInstruction { FeatureReference = "pattern", AreaFillReference = "PATTERN", DrawingPriority = 5 },
        ];

        int computeCount = 0;
        var builder = new VectorSceneBuilder
        {
            ResolveColor = static _ => new RgbaColor(200, 200, 200, 255),
            PatternResolver = static _ => [1, 2, 3, 4],
            PatternClipCache = compute =>
            {
                computeCount++;
                _ = compute();
                return [];
            },
        };

        var scene = builder.Build(instructions, geometry);

        Assert.Equal(1, computeCount);
        Assert.Empty(scene.Ops.OfType<PatternAreaPaintOp>());
    }

    [Fact]
    public void PatternClipCache_MemoizesClipAcrossRebuilds()
    {
        // Simulates the palette-switch case: two consecutive layer builds sharing
        // one cache must run the expensive clip only once, and the second build
        // must still produce the correctly clipped geometry from the cache.
        var geometry = new DictionaryGeometryProvider();
        geometry.Add("low", Rectangle(0, 0, 10, 10));
        geometry.Add("high", Rectangle(0, 0, 5, 10));

        DrawingInstruction[] instructions =
        [
            new AreaInstruction { FeatureReference = "low", AreaFillReference = "PATTERN_LOW", DrawingPriority = 5 },
            new AreaInstruction { FeatureReference = "high", AreaFillReference = "PATTERN_HIGH", DrawingPriority = 10 },
        ];

        IReadOnlyList<PatternPriorityClipper.ClippedPattern>? cached = null;
        int computeCount = 0;
        PatternClipMemoizer memoizer = compute =>
        {
            if (cached is null)
            {
                computeCount++;
                cached = compute();
            }

            return cached;
        };

        VectorScene Build() => new VectorSceneBuilder
        {
            ResolveColor = static _ => new RgbaColor(200, 200, 200, 255),
            PatternResolver = static _ => [1, 2, 3, 4],
            PatternClipCache = memoizer,
        }.Build(instructions, geometry);

        var first = Build();
        var second = Build();

        // The clip overlay ran exactly once despite two builds.
        Assert.Equal(1, computeCount);

        // Both builds produce the same clipped low-priority pattern: the left half
        // (under the higher-priority pattern) is removed, the right half remains.
        foreach (var scene in new[] { first, second })
        {
            var lowGeometry = ToGeometry(SinglePatternOp(scene, "PATTERN_LOW"));
            Assert.False(lowGeometry.Contains(At(2, 5)));
            Assert.True(lowGeometry.Contains(At(8, 5)));
        }
    }
}
