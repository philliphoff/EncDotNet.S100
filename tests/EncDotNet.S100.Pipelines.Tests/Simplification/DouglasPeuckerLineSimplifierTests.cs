using EncDotNet.S100.Renderers.Mapsui.Simplification;
using Mapsui.Nts;
using Mapsui.Styles;
using NetTopologySuite.Geometries;
using Xunit;

namespace EncDotNet.S100.Pipelines.Tests.Simplification;

/// <summary>
/// Behavioural tests for <see cref="DouglasPeuckerLineSimplifier"/>:
/// reduces vertex count on dense lines, respects the metric tolerance,
/// passes short or non-line geometries through unchanged, and copies
/// fields + style references onto the simplified clone.
/// </summary>
public sealed class DouglasPeuckerLineSimplifierTests
{
    private static readonly GeometryFactory _gf = new();
    private static readonly SimplificationOptions _opts = new();

    [Fact]
    public void ReducesVertexCount_OnDenseZigzagLine()
    {
        // 1000-point zigzag with sub-tolerance jitter — DP should
        // collapse it almost entirely.
        var coords = new Coordinate[1000];
        for (int i = 0; i < coords.Length; i++)
        {
            double y = (i % 2 == 0) ? 0.5 : -0.5;
            coords[i] = new Coordinate(i, y);
        }
        var line = _gf.CreateLineString(coords);
        var orig = new GeometryFeature(line);

        var result = DouglasPeuckerLineSimplifier.Instance.Simplify(orig, toleranceMetres: 10.0, _opts);

        Assert.True(result.WasSimplified);
        Assert.True(result.SimplifiedCoordinateCount < result.OriginalCoordinateCount / 10,
            $"Expected ≥10× reduction; got {result.OriginalCoordinateCount}→{result.SimplifiedCoordinateCount}");
        Assert.Equal(1000, result.OriginalCoordinateCount);
    }

    [Fact]
    public void RespectsMetricTolerance()
    {
        // A spike of 5 metres at index 5 of an otherwise-straight line.
        // Tolerance of 1 m must KEEP the spike; tolerance of 10 m must DROP it.
        var coords = new Coordinate[100];
        for (int i = 0; i < coords.Length; i++) coords[i] = new Coordinate(i, 0);
        coords[50] = new Coordinate(50, 5);
        var line = _gf.CreateLineString(coords);
        var orig = new GeometryFeature(line);

        var keep = DouglasPeuckerLineSimplifier.Instance.Simplify(orig, 1.0, _opts);
        Assert.True(keep.WasSimplified);
        var keepCoords = ((LineString)((GeometryFeature)keep.Feature).Geometry!).Coordinates;
        Assert.Contains(keepCoords, c => c.Y >= 5.0 - 1e-9);

        var drop = DouglasPeuckerLineSimplifier.Instance.Simplify(orig, 10.0, _opts);
        Assert.True(drop.WasSimplified);
        var dropCoords = ((LineString)((GeometryFeature)drop.Feature).Geometry!).Coordinates;
        Assert.DoesNotContain(dropCoords, c => c.Y >= 5.0 - 1e-9);
    }

    [Fact]
    public void ShortLine_BelowMinVertexCount_PassesThrough()
    {
        var coords = new Coordinate[]
        {
            new(0, 0), new(1, 1), new(2, 2), new(3, 3),
        };
        var orig = new GeometryFeature(_gf.CreateLineString(coords));
        var result = DouglasPeuckerLineSimplifier.Instance.Simplify(orig, 0.1, _opts);
        Assert.False(result.WasSimplified);
        Assert.Same(orig, result.Feature);
    }

    [Fact]
    public void Polygon_PassesThroughUnchanged()
    {
        // Polygons are out of scope in v1.
        var ring = _gf.CreateLinearRing(new Coordinate[]
        {
            new(0, 0), new(100, 0), new(100, 100), new(0, 100), new(0, 0),
        });
        var poly = _gf.CreatePolygon(ring);
        var orig = new GeometryFeature(poly);
        var result = DouglasPeuckerLineSimplifier.Instance.Simplify(orig, 1.0, _opts);
        Assert.False(result.WasSimplified);
        Assert.Same(orig, result.Feature);
    }

    [Fact]
    public void Point_PassesThroughUnchanged()
    {
        var orig = new GeometryFeature(_gf.CreatePoint(new Coordinate(1, 2)));
        var result = DouglasPeuckerLineSimplifier.Instance.Simplify(orig, 1.0, _opts);
        Assert.False(result.WasSimplified);
        Assert.Same(orig, result.Feature);
    }

    [Fact]
    public void Simplified_Feature_ShareStyleInstancesAndCarriesBackReference()
    {
        var coords = new Coordinate[200];
        for (int i = 0; i < coords.Length; i++) coords[i] = new Coordinate(i, (i % 3) * 0.1);
        var orig = new GeometryFeature(_gf.CreateLineString(coords));
        var style = new VectorStyle();
        orig.Styles.Add(style);
        orig["S100.FeatureRef"] = "F42";

        var result = DouglasPeuckerLineSimplifier.Instance.Simplify(orig, 5.0, _opts);

        Assert.True(result.WasSimplified);
        // Copied field
        Assert.Equal("F42", result.Feature["S100.FeatureRef"]);
        // Back-reference present
        Assert.Same(orig, EncDotNet.S100.Renderers.Mapsui.Simplification.Simplification.GetOriginal(result.Feature));
        // Same style instance (avoids re-rendering style state)
        bool foundSame = false;
        foreach (var s in result.Feature.Styles)
            if (ReferenceEquals(s, style)) { foundSame = true; break; }
        Assert.True(foundSame, "Simplified clone must share the style instance from the original.");
    }

    [Fact]
    public void NonPositiveTolerance_PassesThrough()
    {
        var coords = new Coordinate[200];
        for (int i = 0; i < coords.Length; i++) coords[i] = new Coordinate(i, 0);
        var orig = new GeometryFeature(_gf.CreateLineString(coords));
        var zero = DouglasPeuckerLineSimplifier.Instance.Simplify(orig, 0.0, _opts);
        Assert.False(zero.WasSimplified);
        var neg = DouglasPeuckerLineSimplifier.Instance.Simplify(orig, -1.0, _opts);
        Assert.False(neg.WasSimplified);
    }
}
