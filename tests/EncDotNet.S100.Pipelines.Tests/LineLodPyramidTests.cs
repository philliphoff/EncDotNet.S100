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

    /// <summary>
    /// At latitude 0 the equirectangular-real-metre DP and a
    /// Web-Mercator-metre DP produce the same tolerance in real terms
    /// (<c>cos(0) = 1</c>), so <see cref="LineLodPyramid.BuildForMercatorSelection"/>
    /// should degenerate to the same output as
    /// <see cref="LineLodPyramid.Build"/> at the same numerical tolerance.
    /// This is the sanity anchor for the higher-latitude parity property.
    /// </summary>
    [Fact]
    public void BuildForMercatorSelection_AtEquator_MatchesBuildVertexCounts()
    {
        var line = MakeSyntheticLine(midLatDegrees: 0.0, vertexCount: 200);

        var mercatorPyramid = LineLodPyramid.BuildForMercatorSelection(
            line, LineLodTolerances.HalfOctaveDefault);
        var realPyramid = LineLodPyramid.Build(
            line, LineLodTolerances.HalfOctaveDefault);

        Assert.Equal(realPyramid.Levels.Count, mercatorPyramid.Levels.Count);
        for (var i = 0; i < realPyramid.Levels.Count; i++)
        {
            Assert.Equal(
                realPyramid.Levels[i].Coordinates.Count,
                mercatorPyramid.Levels[i].Coordinates.Count);
        }
    }

    /// <summary>
    /// Core parity property (#489, PR-3 tolerance-unit fix): at any
    /// non-equatorial latitude, running Douglas-Peucker over the WGS-84
    /// coordinates via <see cref="LineLodPyramid.BuildForMercatorSelection"/>
    /// at Mercator tolerance <c>T</c> must produce the same kept vertex
    /// set as running it over the equirectangular-projected coordinates at
    /// real-metre tolerance <c>T · cos(midLat)</c>. This is what makes the
    /// consumed pyramid pixel-parity-equivalent with the renderer's
    /// Cartesian-DP fallback path (PR-2 baseline) at that feature's
    /// latitude.
    /// </summary>
    [Theory]
    [InlineData(30.0)]
    [InlineData(45.0)]
    [InlineData(54.0)]
    [InlineData(60.0)]
    public void BuildForMercatorSelection_KeptVertexCountMatchesCosScaledRealBuild(
        double midLatDegrees)
    {
        var line = MakeSyntheticLine(midLatDegrees, vertexCount: 200);
        var cosMidLat = Math.Cos(midLatDegrees * Math.PI / 180.0);
        var scaledRealLadder = LineLodTolerances.HalfOctaveDefault
            .Select(t => t * cosMidLat)
            .ToArray();

        var mercatorPyramid = LineLodPyramid.BuildForMercatorSelection(
            line, LineLodTolerances.HalfOctaveDefault);
        var scaledRealPyramid = LineLodPyramid.Build(line, scaledRealLadder);

        // Same kept vertex SET per level -> DP outcome is bit-identical.
        // GeoPosition is a value-equatable record, so exact coordinate
        // equality is the strictest possible algorithmic-equivalence bar.
        Assert.Equal(scaledRealPyramid.Levels.Count, mercatorPyramid.Levels.Count);
        for (var i = 0; i < scaledRealPyramid.Levels.Count; i++)
        {
            Assert.Equal(
                scaledRealPyramid.Levels[i].Coordinates,
                mercatorPyramid.Levels[i].Coordinates);
        }

        // ToleranceMetres on each non-passthrough level records the
        // ORIGINAL Mercator tolerance so SelectLevelIndex(mercatorBudget)
        // is apples-to-apples.
        for (var i = 0; i < LineLodTolerances.HalfOctaveDefault.Count; i++)
        {
            Assert.Equal(
                LineLodTolerances.HalfOctaveDefault[i],
                mercatorPyramid.Levels[i].ToleranceMetres);
        }
    }

    /// <summary>
    /// Ladder-validation must reject the same inputs on the new overload
    /// as on <see cref="LineLodPyramid.Build"/>: empty ladders, non-positive
    /// values, and non-strictly-descending ladders.
    /// </summary>
    [Fact]
    public void BuildForMercatorSelection_RejectsBadLadder()
    {
        var line = MakeSyntheticLine(midLatDegrees: 45.0, vertexCount: 100);

        Assert.Throws<ArgumentException>(() =>
            LineLodPyramid.BuildForMercatorSelection(line, Array.Empty<double>()));
        Assert.Throws<ArgumentException>(() =>
            LineLodPyramid.BuildForMercatorSelection(line, [64.0, 0.0]));
        Assert.Throws<ArgumentException>(() =>
            LineLodPyramid.BuildForMercatorSelection(line, [64.0, 64.0]));
        Assert.Throws<ArgumentException>(() =>
            LineLodPyramid.BuildForMercatorSelection(line, [16.0, 64.0]));
    }

    /// <summary>
    /// Degenerate short input is passed straight through as a single
    /// passthrough level, mirroring
    /// <see cref="LineLodPyramid.Build"/>'s behaviour.
    /// </summary>
    [Fact]
    public void BuildForMercatorSelection_DegenerateInputReturnsPassthroughOnly()
    {
        var shortLine = new List<GeoPosition>
        {
            new(45.0, -0.5),
            new(45.001, -0.499),
        };

        var pyramid = LineLodPyramid.BuildForMercatorSelection(
            shortLine, LineLodTolerances.HalfOctaveDefault);

        Assert.Single(pyramid.Levels);
        Assert.True(pyramid.Levels[0].IsPassthrough);
    }

    /// <summary>
    /// Documents the equirectangular mid-latitude anchor residual bound
    /// (per coordinator §2 review). A wide-latitude-span line at 54°N
    /// (spanning ~1° of latitude, well outside the "typical S-101 line
    /// feature" envelope) can in principle expose the DP-in-equirect
    /// vs DP-in-Mercator difference because Mercator scale varies
    /// along the line while the anchor is fixed at midLat. This test
    /// asserts kept-vertex counts stay within ±1 per level rather than
    /// exact — documenting the residual bound instead of over-asserting.
    /// If this ever fails, it means the anchor residual grew large
    /// enough at some latitude/span to flip more than one DP tie-break
    /// per level, which would warrant re-visiting the fixed-anchor
    /// approximation in <see cref="DouglasPeuckerLineSimplifier"/>.
    /// </summary>
    [Fact]
    public void BuildForMercatorSelection_WideLatitudeSpan_KeptCountResidualBounded()
    {
        var line = MakeWideLatitudeSpanLine(midLatDegrees: 54.0, vertexCount: 300);
        var cosMidLat = Math.Cos(54.0 * Math.PI / 180.0);
        var scaledRealLadder = LineLodTolerances.HalfOctaveDefault
            .Select(t => t * cosMidLat)
            .ToArray();

        var mercatorPyramid = LineLodPyramid.BuildForMercatorSelection(
            line, LineLodTolerances.HalfOctaveDefault);
        var scaledRealPyramid = LineLodPyramid.Build(line, scaledRealLadder);

        Assert.Equal(scaledRealPyramid.Levels.Count, mercatorPyramid.Levels.Count);
        for (var i = 0; i < scaledRealPyramid.Levels.Count; i++)
        {
            var delta = Math.Abs(
                scaledRealPyramid.Levels[i].Coordinates.Count -
                mercatorPyramid.Levels[i].Coordinates.Count);
            Assert.True(
                delta <= 1,
                $"Level {i} kept-count residual {delta} > 1 " +
                $"(scaledReal={scaledRealPyramid.Levels[i].Coordinates.Count}, " +
                $"mercatorEquiv={mercatorPyramid.Levels[i].Coordinates.Count}).");
        }
    }

    /// <summary>
    /// Wide-span (~1° latitude) synthetic line for the residual-bound
    /// test above. Intentionally outside the typical S-101 line-feature
    /// envelope (which is usually well under 0.1° of latitude).
    /// </summary>
    private static IReadOnlyList<GeoPosition> MakeWideLatitudeSpanLine(
        double midLatDegrees, int vertexCount)
    {
        var coords = new List<GeoPosition>(vertexCount);
        for (var i = 0; i < vertexCount; i++)
        {
            var t = i / (double)(vertexCount - 1);
            // Sweep ~1° of latitude with a slower longitude sweep and a
            // moderate sinusoidal wiggle so DP still has real work at
            // every ladder level.
            var lat = (midLatDegrees - 0.5) + t * 1.0
                      + Math.Sin(t * Math.PI * 12) * 0.0008;
            var lon = -1.0 + t * 0.5;
            coords.Add(new GeoPosition(lat, lon));
        }
        return coords;
    }

    /// <summary>
    /// A wiggly synthetic line centred on <paramref name="midLatDegrees"/>
    /// with a controlled amplitude so Douglas-Peucker at the default
    /// tolerance ladder actually drops vertices at every level.
    /// </summary>
    private static IReadOnlyList<GeoPosition> MakeSyntheticLine(
        double midLatDegrees, int vertexCount)
    {
        var coords = new List<GeoPosition>(vertexCount);
        for (var i = 0; i < vertexCount; i++)
        {
            var t = i / (double)(vertexCount - 1);
            // 0.2° of longitude span (~22 km near equator, less at higher
            // latitudes) with a ~150 m amplitude sinusoidal wiggle in
            // latitude so DP has real work to do at 16/64/256 m tolerances.
            var lat = midLatDegrees + Math.Sin(t * Math.PI * 8) * 0.0015;
            var lon = -1.0 + t * 0.2;
            coords.Add(new GeoPosition(lat, lon));
        }
        return coords;
    }
}
