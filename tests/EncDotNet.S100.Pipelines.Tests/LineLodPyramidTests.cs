using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines.Vector.Caching;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Behaviour of <see cref="LineLodPyramid"/>: level ordering (coarsest
/// first), passthrough level appended last, argument validation on the
/// tolerance ladder, degenerate-input handling, and
/// <see cref="LineLodPyramid.SelectLevel"/> across the resolution range.
/// </summary>
public class LineLodPyramidTests
{
    private static IReadOnlyList<GeoPosition> Line()
    {
        var coords = new List<GeoPosition>();
        for (var i = 0; i <= 200; i++)
        {
            var t = i / 200.0;
            coords.Add(new GeoPosition(
                50.0 + t * 0.2,
                -1.0 + t * 0.2 + Math.Sin(t * Math.PI * 8) * 0.001));
        }
        return coords;
    }

    [Fact]
    public void BuildProducesLevelsPlusPassthrough()
    {
        var pyramid = LineLodPyramid.Build(Line(), LineLodTolerances.HalfOctaveDefault);

        Assert.Equal(LineLodTolerances.HalfOctaveDefault.Count + 1, pyramid.Levels.Count);
        Assert.True(pyramid.Levels[^1].IsPassthrough);
        Assert.False(pyramid.Levels[0].IsPassthrough);
    }

    [Fact]
    public void LevelsAreCoarsestFirstAndFinerLevelsHaveAtLeastAsManyVertices()
    {
        var pyramid = LineLodPyramid.Build(Line(), LineLodTolerances.HalfOctaveDefault);

        for (var i = 1; i < pyramid.Levels.Count - 1; i++)
        {
            Assert.True(pyramid.Levels[i].ToleranceMetres < pyramid.Levels[i - 1].ToleranceMetres);
            Assert.True(pyramid.Levels[i].Coordinates.Count >= pyramid.Levels[i - 1].Coordinates.Count);
        }
    }

    [Fact]
    public void PassthroughLevelCarriesInputCoordinates()
    {
        var input = Line();
        var pyramid = LineLodPyramid.Build(input, LineLodTolerances.HalfOctaveDefault);

        var passthrough = pyramid.Levels[^1];
        Assert.Equal(input.Count, passthrough.Coordinates.Count);
        Assert.Equal(input.Count, pyramid.InputVertexCount);
        Assert.Equal(0.0, passthrough.ToleranceMetres);
    }

    [Fact]
    public void DegenerateInputYieldsPassthroughOnly()
    {
        var input = new List<GeoPosition> { new(50.0, -1.0), new(50.1, -0.9) };

        var pyramid = LineLodPyramid.Build(input, LineLodTolerances.HalfOctaveDefault);

        Assert.Single(pyramid.Levels);
        Assert.True(pyramid.Levels[0].IsPassthrough);
        Assert.Equal(2, pyramid.InputVertexCount);
    }

    [Fact]
    public void EmptyTolerancesThrows()
    {
        Assert.Throws<ArgumentException>(() =>
            LineLodPyramid.Build(Line(), Array.Empty<double>()));
    }

    [Fact]
    public void NonDescendingTolerancesThrows()
    {
        Assert.Throws<ArgumentException>(() =>
            LineLodPyramid.Build(Line(), [16.0, 64.0, 256.0]));
    }

    [Fact]
    public void ZeroToleranceThrows()
    {
        Assert.Throws<ArgumentException>(() =>
            LineLodPyramid.Build(Line(), [256.0, 0.0]));
    }

    [Fact]
    public void SelectLevelPicksCoarsestBelowBudget()
    {
        var pyramid = LineLodPyramid.Build(Line(), [256.0, 64.0, 16.0]);

        // At 1000 m/px * 0.5 px = 500 m budget -> even the 256 m level is
        // below budget so the coarsest is picked.
        var coarseSelection = pyramid.SelectLevel(1000.0);
        Assert.Equal(256.0, coarseSelection.ToleranceMetres);

        // At 200 m/px * 0.5 = 100 m budget -> 256 above budget, 64 below
        // -> the 64 m level is picked.
        var midSelection = pyramid.SelectLevel(200.0);
        Assert.Equal(64.0, midSelection.ToleranceMetres);

        // At 20 m/px * 0.5 = 10 m budget -> all real levels above budget,
        // fall through to passthrough.
        var fineSelection = pyramid.SelectLevel(20.0);
        Assert.True(fineSelection.IsPassthrough);
    }

    [Fact]
    public void SelectLevelExactBoundaryPicksLevelAtOrBelowBudget()
    {
        var pyramid = LineLodPyramid.Build(Line(), [256.0, 64.0, 16.0]);

        // Budget exactly 64 m -> the 64 m level satisfies "tolerance <= budget".
        var selection = pyramid.SelectLevel(128.0);
        Assert.Equal(64.0, selection.ToleranceMetres);
    }

    [Fact]
    public void SelectLevelNonPositiveResolutionThrows()
    {
        var pyramid = LineLodPyramid.Build(Line(), LineLodTolerances.HalfOctaveDefault);

        Assert.Throws<ArgumentOutOfRangeException>(() => pyramid.SelectLevel(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => pyramid.SelectLevel(-1.0));
    }

    [Fact]
    public void SelectLevelNonPositiveTargetPixelsThrows()
    {
        var pyramid = LineLodPyramid.Build(Line(), LineLodTolerances.HalfOctaveDefault);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            pyramid.SelectLevel(100.0, targetPixels: 0));
    }
}
