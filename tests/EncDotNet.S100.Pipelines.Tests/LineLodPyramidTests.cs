using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines.Vector.Caching;
using EncDotNet.S100.Renderers.Mapsui;
using Mapsui.Projections;

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
    /// <see cref="LineLodLevel.ToleranceMetres"/> on each non-passthrough
    /// level records the Mercator-metre tolerance so
    /// <see cref="LineLodPyramid.SelectLevelIndex"/> can compare a Mercator
    /// budget to a Mercator tolerance apples-to-apples.
    /// </summary>
    [Fact]
    public void BuildForMercatorSelection_LevelToleranceMetresRecordsInputLadder()
    {
        var line = MakeSyntheticLine(midLatDegrees: 45.0, vertexCount: 200);
        var pyramid = LineLodPyramid.BuildForMercatorSelection(
            line, LineLodTolerances.HalfOctaveDefault);

        for (var i = 0; i < LineLodTolerances.HalfOctaveDefault.Count; i++)
        {
            Assert.Equal(
                LineLodTolerances.HalfOctaveDefault[i],
                pyramid.Levels[i].ToleranceMetres);
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
    /// EVIDENCE-GATHERING form (coordinator §2 review): quantify the actual
    /// PR-2 vs PR-3 kept-vertex deviation on a BOUNDED S-101 feature (~0.05°
    /// lat, ~0.1° lon span) at 30/45/54/60°N. Reports the max Mercator-metre
    /// positional residual per band via <see cref="Assert.True"/> failure
    /// messages when the "0-pixel bar" (10 merc-m) is exceeded, so we can
    /// see what the residual actually is instead of claiming exactness.
    ///
    /// The coordinator's bar: EXACT match at every latitude → merges as
    /// "0-pixel at bounded feature" evidence. Anything else needs re-visit.
    /// </summary>
    /// <summary>
    /// Cross-frame equivalence gate: <see cref="LineLodPyramid.BuildForMercatorSelection"/>
    /// must produce bit-identical kept-vertex sets to the renderer's pre-#489
    /// inline Cartesian pyramid (PR-2's algorithm). Since both paths now
    /// project each vertex via
    /// <c>Mapsui.Projections.SphericalMercator.FromLonLat</c> and run the
    /// same Douglas-Peucker inner loop, this is a strict equality bar — any
    /// deviation would indicate a drift between the two projection formulas
    /// (a bug we must fix, not paper over).
    /// </summary>
    [Theory]
    [InlineData(30.0)]
    [InlineData(45.0)]
    [InlineData(54.0)]
    [InlineData(60.0)]
    public void BuildForMercatorSelection_CrossFrame_ShortLine_IsBitIdenticalToPr2InlineDp(
        double midLatDegrees)
    {
        var line = MakeBoundedFeatureLine(midLatDegrees, vertexCount: 200);
        var mercatorInput = ProjectToMercator(line);

        foreach (var tolerance in LineLodTolerances.HalfOctaveDefault)
        {
            var pr2Kept = CartesianDouglasPeucker.Simplify(mercatorInput, tolerance);
            var pr3Pyramid = LineLodPyramid.BuildForMercatorSelection(
                line, new[] { tolerance });
            var pr3KeptMerc = pr3Pyramid.Levels[0].Coordinates
                .Select(g => ProjectSingle(g))
                .ToArray();

            Assert.Equal(pr2Kept.Length, pr3KeptMerc.Length);
            for (var i = 0; i < pr2Kept.Length; i++)
            {
                Assert.Equal(pr2Kept[i].X, pr3KeptMerc[i].X);
                Assert.Equal(pr2Kept[i].Y, pr3KeptMerc[i].Y);
            }
        }
    }

    /// <summary>
    /// Wide-latitude-span stress case: proves the fix holds at scale — a
    /// 1°-latitude-span line at 30°N and 54°N still produces bit-identical
    /// output between PR-2's inline Cartesian DP and PR-3's open-hook
    /// pre-build. The v2 <c>×cos φ</c> approach failed this case badly
    /// (equirect DP dropped down to 2 vertices at band 1 where the
    /// Mercator DP kept 7–11); this v3 test locks the fix.
    /// </summary>
    [Theory]
    [InlineData(30.0)]
    [InlineData(54.0)]
    public void BuildForMercatorSelection_CrossFrame_WideLine_IsBitIdenticalToPr2InlineDp(
        double midLatDegrees)
    {
        var line = MakeWideLatitudeSpanLine(midLatDegrees, vertexCount: 300);
        var mercatorInput = ProjectToMercator(line);

        foreach (var tolerance in LineLodTolerances.HalfOctaveDefault)
        {
            var pr2Kept = CartesianDouglasPeucker.Simplify(mercatorInput, tolerance);
            var pr3Pyramid = LineLodPyramid.BuildForMercatorSelection(
                line, new[] { tolerance });
            var pr3KeptMerc = pr3Pyramid.Levels[0].Coordinates
                .Select(g => ProjectSingle(g))
                .ToArray();

            Assert.Equal(pr2Kept.Length, pr3KeptMerc.Length);
            for (var i = 0; i < pr2Kept.Length; i++)
            {
                Assert.Equal(pr2Kept[i].X, pr3KeptMerc[i].X);
                Assert.Equal(pr2Kept[i].Y, pr3KeptMerc[i].Y);
            }
        }
    }

    /// <summary>
    /// Project a list of <see cref="GeoPosition"/> to
    /// <see cref="CartesianPoint"/> in EPSG:3857 metres via
    /// <see cref="SphericalMercator.FromLonLat"/>. This is the SAME
    /// projection the renderer applies on the render-thread path (per
    /// <see cref="CachedVectorStyleRenderer.ProjectLevelToWebMercator"/>).
    /// </summary>
    private static CartesianPoint[] ProjectToMercator(IReadOnlyList<GeoPosition> coords)
    {
        var result = new CartesianPoint[coords.Count];
        for (var i = 0; i < coords.Count; i++)
        {
            var (x, y) = SphericalMercator.FromLonLat(coords[i].Longitude, coords[i].Latitude);
            result[i] = new CartesianPoint(x, y);
        }
        return result;
    }

    private static CartesianPoint ProjectSingle(GeoPosition g)
    {
        var (x, y) = SphericalMercator.FromLonLat(g.Longitude, g.Latitude);
        return new CartesianPoint(x, y);
    }

    private static double MaxNearestNeighbourDistance(
        CartesianPoint[] a, CartesianPoint[] b)
    {
        var max = 0.0;
        foreach (var bp in b)
        {
            var best = double.MaxValue;
            foreach (var ap in a)
            {
                var dx = ap.X - bp.X;
                var dy = ap.Y - bp.Y;
                var d2 = dx * dx + dy * dy;
                if (d2 < best) best = d2;
            }
            var d = Math.Sqrt(best);
            if (d > max) max = d;
        }
        return max;
    }

    /// <summary>
    /// Diagnostic (skipped in normal runs — re-enable when reviewing residual)
    /// that prints the actual PR-2 vs PR-3 kept-vertex deviation across all
    /// four parity latitudes and both geometry families. Use with
    /// <c>dotnet test --filter FullyQualifiedName~DiagnosticDump</c> and
    /// <c>-v n</c> to see the table.
    /// </summary>
    [Fact(Skip = "diagnostic dump - re-enable to inspect residuals")]
    public void DiagnosticDump_CrossFrameResidualTable()
    {
        var lines = new (string kind, IReadOnlyList<GeoPosition> line, double lat)[]
        {
            ("bounded", MakeBoundedFeatureLine(30.0, 200), 30.0),
            ("bounded", MakeBoundedFeatureLine(45.0, 200), 45.0),
            ("bounded", MakeBoundedFeatureLine(54.0, 200), 54.0),
            ("bounded", MakeBoundedFeatureLine(60.0, 200), 60.0),
            ("wide", MakeWideLatitudeSpanLine(30.0, 300), 30.0),
            ("wide", MakeWideLatitudeSpanLine(54.0, 300), 54.0),
        };

        foreach (var (kind, line, lat) in lines)
        {
            var merc = ProjectToMercator(line);
            foreach (var tol in LineLodTolerances.HalfOctaveDefault)
            {
                var aPoints = CartesianDouglasPeucker.Simplify(merc, tol);
                var b = LineLodPyramid.BuildForMercatorSelection(line, new[] { tol })
                    .Levels[0].Coordinates.Select(ProjectSingle).ToArray();

                // Map both kept-vertex arrays back to original input indices.
                var aIdx = KeptIndicesFromMercator(merc, aPoints);
                var bIdx = KeptIndicesFromMercator(merc, b);

                var aSet = new HashSet<int>(aIdx);
                var bSet = new HashSet<int>(bIdx);
                var onlyA = aSet.Except(bSet).OrderBy(i => i).ToArray();
                var onlyB = bSet.Except(aSet).OrderBy(i => i).ToArray();
                var maxDev = MaxNearestNeighbourDistance(aPoints, b);

                Console.WriteLine(
                    $"{kind,-8} · lat={lat,4:F1}°N · tol={tol,5:F0} merc-m " +
                    $"| A={aPoints.Length,4} B={b.Length,4} · " +
                    $"onlyA=[{string.Join(",", onlyA.Take(8))}{(onlyA.Length > 8 ? "…" : "")}] " +
                    $"onlyB=[{string.Join(",", onlyB.Take(8))}{(onlyB.Length > 8 ? "…" : "")}] " +
                    $"· maxDev={maxDev,7:F2} m");
            }
        }
    }

    private static List<int> KeptIndicesFromMercator(
        CartesianPoint[] input, CartesianPoint[] kept)
    {
        var result = new List<int>(kept.Length);
        var j = 0;
        for (var i = 0; i < input.Length && j < kept.Length; i++)
        {
            if (input[i].X == kept[j].X && input[i].Y == kept[j].Y)
            {
                result.Add(i);
                j++;
            }
        }
        return result;
    }

    /// <summary>
    /// Bounded-extent (~0.05° lat, ~0.1° lon) synthetic line at
    /// <paramref name="midLatDegrees"/> with a controlled sinusoidal wiggle
    /// so DP has real work at every ladder band. Represents a typical S-101
    /// line-feature envelope.
    /// </summary>
    private static IReadOnlyList<GeoPosition> MakeBoundedFeatureLine(
        double midLatDegrees, int vertexCount)
    {
        var coords = new List<GeoPosition>(vertexCount);
        for (var i = 0; i < vertexCount; i++)
        {
            var t = i / (double)(vertexCount - 1);
            var lat = midLatDegrees + Math.Sin(t * Math.PI * 12) * 0.0009;
            var lon = -1.0 + t * 0.1;
            coords.Add(new GeoPosition(lat, lon));
        }
        return coords;
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
