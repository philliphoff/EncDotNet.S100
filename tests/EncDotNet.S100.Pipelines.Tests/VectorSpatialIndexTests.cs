using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Pipelines.Vector.Spatial;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Correctness tests for <see cref="IVectorSpatialIndex"/> — the
/// STR-packed R-tree that backs
/// <see cref="S101VectorSource.GetFeatures(BoundingBox?)"/> from issue
/// #490 onward. The acceptance gate is result-set parity vs a
/// brute-force MBR scan: for **every** extent, the index must return
/// the same set of features the naive linear scan would return, so
/// that downstream point-in-poly / distance predicates continue to
/// see the full candidate set.
/// </summary>
public class VectorSpatialIndexTests
{
    // -------------------------- Fixtures -------------------------------

    private static Feature MakePoint(long id, double lat, double lon) => new()
    {
        Id = id,
        FeatureType = "Buoy",
        GeometryType = GeometryType.Point,
        Coordinates = [new GeoPosition(lat, lon)],
        Attributes = new Dictionary<string, object?>(),
    };

    private static Feature MakeCurve(long id, params (double Lat, double Lon)[] points) => new()
    {
        Id = id,
        FeatureType = "Coastline",
        GeometryType = GeometryType.Curve,
        Coordinates = points.Select(p => new GeoPosition(p.Lat, p.Lon)).ToArray(),
        Attributes = new Dictionary<string, object?>(),
    };

    private static Feature MakeSurface(long id, GeoPosition[] exterior, GeoPosition[][]? holes = null) => new()
    {
        Id = id,
        FeatureType = "DepthArea",
        GeometryType = GeometryType.Surface,
        Coordinates = exterior,
        InteriorRings = holes?.Select(h => (IReadOnlyList<GeoPosition>)h).ToArray() ?? [],
        Attributes = new Dictionary<string, object?>(),
    };

    private static Feature MakeSquarePoint(long id, double centreLat, double centreLon)
        => MakePoint(id, centreLat, centreLon);

    /// <summary>
    /// Reference oracle: naive linear-scan MBR intersection matching
    /// the tree's closed-interval semantics. Feature MBR is the tight
    /// bbox of Coordinates + InteriorRings, matching
    /// <c>FeatureMbr.Compute</c>.
    /// </summary>
    private static HashSet<long> BruteForce(IReadOnlyList<Feature> features, BoundingBox extent)
    {
        var hits = new HashSet<long>();
        foreach (var f in features)
        {
            if (!TryFeatureMbr(f, out var minLat, out var minLon, out var maxLat, out var maxLon))
            {
                continue;
            }

            if (maxLat < extent.SouthLatitude) continue;
            if (minLat > extent.NorthLatitude) continue;
            if (maxLon < extent.WestLongitude) continue;
            if (minLon > extent.EastLongitude) continue;
            hits.Add(f.Id);
        }
        return hits;
    }

    private static bool TryFeatureMbr(
        Feature f,
        out double minLat, out double minLon,
        out double maxLat, out double maxLon)
    {
        var minLatL = double.PositiveInfinity;
        var minLonL = double.PositiveInfinity;
        var maxLatL = double.NegativeInfinity;
        var maxLonL = double.NegativeInfinity;
        var any = false;

        void Fold(IReadOnlyList<GeoPosition> ring)
        {
            foreach (var p in ring)
            {
                if (p.Latitude < minLatL) minLatL = p.Latitude;
                if (p.Latitude > maxLatL) maxLatL = p.Latitude;
                if (p.Longitude < minLonL) minLonL = p.Longitude;
                if (p.Longitude > maxLonL) maxLonL = p.Longitude;
                any = true;
            }
        }

        Fold(f.Coordinates);
        foreach (var hole in f.InteriorRings) Fold(hole);

        minLat = minLatL;
        minLon = minLonL;
        maxLat = maxLatL;
        maxLon = maxLonL;
        return any;
    }

    // -------------------------- Tests ----------------------------------

    [Fact]
    public void Empty_source_returns_no_features()
    {
        var idx = IVectorSpatialIndex.Build([], "S-101");

        Assert.Equal(0, idx.Count);

        var extent = new BoundingBox(-10, -10, 10, 10);
        Assert.Empty(idx.Query(extent));
        Assert.Empty(idx.All());
    }

    [Fact]
    public void Full_extent_returns_every_feature()
    {
        var features = SyntheticCorpus(seed: 1, count: 500);
        var idx = IVectorSpatialIndex.Build(features, "S-101");

        Assert.Equal(500, idx.Count);

        // Query with the reported extent — must yield every feature.
        Assert.NotNull(idx.Extent);
        var all = idx.Query(idx.Extent!).Select(f => f.Id).ToHashSet();
        Assert.Equal(500, all.Count);
        Assert.Equal(features.Select(f => f.Id).ToHashSet(), all);

        // All() is equivalent to a full-extent query.
        var enumerated = idx.All().Select(f => f.Id).ToHashSet();
        Assert.Equal(all, enumerated);
    }

    [Fact]
    public void Empty_extent_far_outside_returns_nothing()
    {
        var features = SyntheticCorpus(seed: 2, count: 100);
        var idx = IVectorSpatialIndex.Build(features, "S-101");

        var far = new BoundingBox(80, 170, 85, 175); // corpus is around (0,0)
        Assert.Empty(idx.Query(far));
    }

    [Fact]
    public void Boundary_touching_feature_is_included()
    {
        // Point at (5, 5) sits exactly on the NE corner of query [0..5,0..5].
        // Closed-interval intersects semantics must include it.
        var corner = MakeSquarePoint(1, 5.0, 5.0);
        var inside = MakeSquarePoint(2, 2.0, 2.0);
        var outside = MakeSquarePoint(3, 5.0001, 5.0);

        var idx = IVectorSpatialIndex.Build([corner, inside, outside], "S-101");

        var q = new BoundingBox(0, 0, 5, 5);
        var hits = idx.Query(q).Select(f => f.Id).ToHashSet();

        Assert.Contains(1L, hits);
        Assert.Contains(2L, hits);
        Assert.DoesNotContain(3L, hits);
    }

    [Fact]
    public void Curve_crossing_extent_edge_with_no_vertex_inside_is_included()
    {
        // Curve goes from (2, -10) → (2, 10): crosses the query box
        // [0..5, 0..5] horizontally but no vertex lies inside.
        // Old vertex-hit test dropped this; MBR test must keep it.
        var crossing = MakeCurve(42, (2, -10), (2, 10));
        var idx = IVectorSpatialIndex.Build([crossing], "S-101");

        var q = new BoundingBox(0, 0, 5, 5);
        var hits = idx.Query(q).ToList();
        Assert.Single(hits);
        Assert.Equal(42L, hits[0].Id);
    }

    [Fact]
    public void Result_is_stable_across_repeated_queries()
    {
        var features = SyntheticCorpus(seed: 3, count: 200);
        var idx = IVectorSpatialIndex.Build(features, "S-101");
        var q = new BoundingBox(-2, -2, 2, 2);

        var first = idx.Query(q).Select(f => f.Id).ToHashSet();
        var second = idx.Query(q).Select(f => f.Id).ToHashSet();
        var third = idx.Query(q).Select(f => f.Id).ToHashSet();

        Assert.Equal(first, second);
        Assert.Equal(first, third);
    }

    [Fact]
    public void Small_dataset_below_threshold_still_matches_brute_force()
    {
        // Fewer than the internal small-dataset threshold — exercises the
        // no-tree fast path. Must still be MBR-correct.
        var features = SyntheticCorpus(seed: 4, count: 8);
        var idx = IVectorSpatialIndex.Build(features, "S-101");

        var extents = new[]
        {
            new BoundingBox(-5, -5, 0, 0),
            new BoundingBox(0, 0, 5, 5),
            new BoundingBox(-10, -10, 10, 10),
            new BoundingBox(20, 20, 30, 30),
        };

        foreach (var e in extents)
        {
            var expected = BruteForce(features, e);
            var actual = idx.Query(e).Select(f => f.Id).ToHashSet();
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void Random_extents_match_brute_force_scan()
    {
        // The acceptance gate the reviewer mandated: many extents,
        // including boundary/empty/full/pick-box, must be set-equal to
        // the naive linear MBR scan.
        var features = SyntheticCorpus(seed: 5, count: 500);
        var idx = IVectorSpatialIndex.Build(features, "S-101");

        var rng = new Random(0x0490);
        for (var i = 0; i < 60; i++)
        {
            var cx = rng.NextDouble() * 20.0 - 10.0;
            var cy = rng.NextDouble() * 20.0 - 10.0;
            // Widths sampled log-uniformly so both pick-boxes (~0.01°)
            // and full-extent queries (~20°) are exercised.
            var wLat = Math.Pow(10, rng.NextDouble() * 3 - 2); // 0.01 .. 10
            var wLon = Math.Pow(10, rng.NextDouble() * 3 - 2);
            var extent = new BoundingBox(cx - wLat, cy - wLon, cx + wLat, cy + wLon);

            var expected = BruteForce(features, extent);
            var actual = idx.Query(extent).Select(f => f.Id).ToHashSet();

            Assert.True(
                expected.SetEquals(actual),
                $"Iteration {i} extent [{extent.SouthLatitude:F3},{extent.WestLongitude:F3},{extent.NorthLatitude:F3},{extent.EastLongitude:F3}] " +
                $"expected {expected.Count} features, got {actual.Count}; " +
                $"missing=[{string.Join(",", expected.Except(actual))}] extra=[{string.Join(",", actual.Except(expected))}]");
        }
    }

    [Fact]
    public void Surface_with_interior_rings_uses_combined_mbr()
    {
        // Feature MBR must include hole coordinates too so a query
        // targeting the hole area still discovers the parent surface.
        var exterior = new[]
        {
            new GeoPosition(0, 0), new GeoPosition(0, 10),
            new GeoPosition(10, 10), new GeoPosition(10, 0), new GeoPosition(0, 0),
        };
        var hole = new[]
        {
            new GeoPosition(4, 4), new GeoPosition(4, 6),
            new GeoPosition(6, 6), new GeoPosition(6, 4), new GeoPosition(4, 4),
        };
        var surface = MakeSurface(1, exterior, [hole]);

        var idx = IVectorSpatialIndex.Build([surface], "S-101");

        // Query fully inside the exterior — but exterior vertices lie
        // outside. Feature MBR is still (0..10, 0..10), so we hit.
        var inside = new BoundingBox(4.5, 4.5, 5.5, 5.5);
        Assert.Single(idx.Query(inside));
    }

    // -------------------------- Corpus builder -------------------------

    private static Feature[] SyntheticCorpus(int seed, int count)
    {
        var rng = new Random(seed);
        var features = new Feature[count];
        for (var i = 0; i < count; i++)
        {
            var lat = rng.NextDouble() * 20.0 - 10.0;
            var lon = rng.NextDouble() * 20.0 - 10.0;
            features[i] = (i % 3) switch
            {
                0 => MakePoint(i, lat, lon),
                1 => MakeCurve(i,
                    (lat, lon),
                    (lat + rng.NextDouble() * 0.5, lon + rng.NextDouble() * 0.5)),
                _ => MakeSurface(i,
                    [
                        new GeoPosition(lat, lon),
                        new GeoPosition(lat + 0.3, lon),
                        new GeoPosition(lat + 0.3, lon + 0.3),
                        new GeoPosition(lat, lon + 0.3),
                        new GeoPosition(lat, lon),
                    ]),
            };
        }
        return features;
    }
}
