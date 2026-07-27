using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines.Vector.Caching;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Behaviour of <see cref="DouglasPeuckerLineSimplifier"/>: endpoint
/// preservation, monotonicity of vertex count with respect to tolerance,
/// ring-closure preservation, degenerate-input handling, and argument
/// validation.
/// </summary>
public class DouglasPeuckerLineSimplifierTests
{
    private static IReadOnlyList<GeoPosition> Line()
    {
        var coords = new List<GeoPosition>();
        for (var i = 0; i <= 100; i++)
        {
            var t = i / 100.0;
            var lat = 50.0 + t * 0.1;
            var lon = -1.0 + t * 0.1 + Math.Sin(t * Math.PI * 10) * 0.0001;
            coords.Add(new GeoPosition(lat, lon));
        }
        return coords;
    }

    [Fact]
    public void EndpointsArePreserved()
    {
        var input = Line();

        var simplified = DouglasPeuckerLineSimplifier.Simplify(input, toleranceMetres: 1000);

        Assert.Equal(input[0], simplified[0]);
        Assert.Equal(input[^1], simplified[^1]);
    }

    [Fact]
    public void FinerToleranceKeepsAtLeastAsManyVertices()
    {
        var input = Line();

        var coarse = DouglasPeuckerLineSimplifier.Simplify(input, toleranceMetres: 256);
        var medium = DouglasPeuckerLineSimplifier.Simplify(input, toleranceMetres: 64);
        var fine = DouglasPeuckerLineSimplifier.Simplify(input, toleranceMetres: 16);

        Assert.True(coarse.Count <= medium.Count,
            $"coarse ({coarse.Count}) should have <= vertices than medium ({medium.Count}).");
        Assert.True(medium.Count <= fine.Count,
            $"medium ({medium.Count}) should have <= vertices than fine ({fine.Count}).");
        Assert.True(fine.Count <= input.Count);
    }

    [Fact]
    public void ClosedLineKeepsClosure()
    {
        // A closed ring: first point equals last point.
        var input = new List<GeoPosition>
        {
            new(50.0, -1.0),
            new(50.0, -0.9),
            new(50.05, -0.85),
            new(50.1, -0.9),
            new(50.1, -1.0),
            new(50.05, -1.05),
            new(50.0, -1.0),
        };

        var simplified = DouglasPeuckerLineSimplifier.Simplify(input, toleranceMetres: 100);

        Assert.Equal(input[0], simplified[0]);
        Assert.Equal(input[^1], simplified[^1]);
    }

    [Fact]
    public void ShortInputIsReturnedUnchanged()
    {
        var input = new List<GeoPosition> { new(50.0, -1.0), new(50.1, -0.9) };

        var simplified = DouglasPeuckerLineSimplifier.Simplify(input, toleranceMetres: 1.0);

        Assert.Same(input, simplified);
    }

    [Fact]
    public void SingleVertexInputIsReturnedUnchanged()
    {
        var input = new List<GeoPosition> { new(50.0, -1.0) };

        var simplified = DouglasPeuckerLineSimplifier.Simplify(input, toleranceMetres: 1.0);

        Assert.Same(input, simplified);
    }

    [Fact]
    public void NullInputThrows()
    {
        Assert.Throws<ArgumentNullException>(() =>
            DouglasPeuckerLineSimplifier.Simplify(null!, toleranceMetres: 1.0));
    }

    [Fact]
    public void NonPositiveToleranceThrows()
    {
        var input = Line();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DouglasPeuckerLineSimplifier.Simplify(input, toleranceMetres: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DouglasPeuckerLineSimplifier.Simplify(input, toleranceMetres: -1));
    }

    [Fact]
    public void StraightLineCollapsesToEndpoints()
    {
        var input = new List<GeoPosition>();
        for (var i = 0; i <= 10; i++)
        {
            input.Add(new GeoPosition(50.0, -1.0 + i * 0.01));
        }

        var simplified = DouglasPeuckerLineSimplifier.Simplify(input, toleranceMetres: 100);

        Assert.Equal(2, simplified.Count);
        Assert.Equal(input[0], simplified[0]);
        Assert.Equal(input[^1], simplified[^1]);
    }

    [Fact]
    public void ExtremelyLargeToleranceCollapsesToEndpoints()
    {
        var input = Line();

        var simplified = DouglasPeuckerLineSimplifier.Simplify(input, toleranceMetres: 1_000_000);

        Assert.Equal(2, simplified.Count);
    }
}
