using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Pipelines.Vector.Spatial;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Integration tests for the spatial-index wiring on
/// <see cref="S101VectorSource"/>. The Core-level index correctness is
/// covered by <see cref="VectorSpatialIndexTests"/>; here we pin the
/// S-101-specific behaviours introduced by issue #490:
/// <list type="bullet">
///   <item>The source implements <see cref="IVectorSourceWithIndex"/>.</item>
///   <item><see cref="S101VectorSource.GetFeatures(BoundingBox?)"/> with a
///     non-null extent returns the same set as a brute-force MBR scan
///     over the null-extent enumeration.</item>
///   <item>Multiple <see cref="S101VectorSource"/> instances sharing the
///     same <see cref="S101Dataset"/> share the cached feature list and
///     index (the identify-path optimisation).</item>
/// </list>
/// </summary>
public sealed class S101VectorSourceIndexTests
{
    private const string FixtureFile = "101AA00DS0016.000";

    [SkippableFact]
    public void S101VectorSource_ImplementsIndexInterface_AndExposesIndex()
    {
        var dataset = OpenFixture();
        var source = new S101VectorSource(dataset);

        Assert.IsAssignableFrom<IVectorSourceWithIndex>(source);

        var indexed = (IVectorSourceWithIndex)source;
        var all = source.GetFeatures();
        Assert.Equal(all.Count, indexed.Index.Count);
    }

    [SkippableFact]
    public void GetFeatures_WithExtent_MatchesBruteForceMbrScan()
    {
        var dataset = OpenFixture();
        var source = new S101VectorSource(dataset);
        var all = source.GetFeatures();
        Skip.If(all.Count == 0, "Fixture yielded no features.");

        var full = source.Metadata.Extent;
        var midLat = (full.SouthLatitude + full.NorthLatitude) / 2.0;
        var midLon = (full.WestLongitude + full.EastLongitude) / 2.0;

        var extents = new[]
        {
            full, // full
            new BoundingBox(full.SouthLatitude, full.WestLongitude, midLat, midLon), // SW quadrant
            new BoundingBox(midLat, midLon, full.NorthLatitude, full.EastLongitude), // NE quadrant
            new BoundingBox(midLat - 1e-4, midLon - 1e-4, midLat + 1e-4, midLon + 1e-4), // pick-box near centre
            new BoundingBox(full.NorthLatitude + 1, full.EastLongitude + 1, // far outside
                            full.NorthLatitude + 2, full.EastLongitude + 2),
        };

        foreach (var extent in extents)
        {
            var expected = all.Where(f => IntersectsMbr(f, extent)).Select(f => f.Id).ToHashSet();
            var actual = source.GetFeatures(extent).Select(f => f.Id).ToHashSet();

            Assert.True(
                expected.SetEquals(actual),
                $"Extent [{extent.SouthLatitude:F4},{extent.WestLongitude:F4},{extent.NorthLatitude:F4},{extent.EastLongitude:F4}]: " +
                $"expected {expected.Count} features, got {actual.Count}; " +
                $"missing=[{string.Join(",", expected.Except(actual).Take(5))}] " +
                $"extra=[{string.Join(",", actual.Except(expected).Take(5))}]");
        }
    }

    [SkippableFact]
    public void SecondVectorSource_OverSameDataset_ReusesCachedFeatureListAndIndex()
    {
        // The identify path builds `new S101VectorSource(dataset)` per
        // request. Post-#490, the per-dataset ConditionalWeakTable cache
        // means both sources share the same materialised feature list and
        // spatial index.
        var dataset = OpenFixture();

        var first = new S101VectorSource(dataset);
        var featuresA = first.GetFeatures();
        var indexA = ((IVectorSourceWithIndex)first).Index;

        var second = new S101VectorSource(dataset);
        var featuresB = second.GetFeatures();
        var indexB = ((IVectorSourceWithIndex)second).Index;

        // The cached list is the same instance across the two sources.
        Assert.Same(featuresA, featuresB);
        Assert.Same(indexA, indexB);
    }

    private static S101Dataset OpenFixture()
    {
        var path = ResolveFixturePath(FixtureFile);
        Skip.IfNot(File.Exists(path), $"S-101 fixture not found: {path}");
        return S101Dataset.Open(path);
    }

    private static bool IntersectsMbr(Feature f, BoundingBox extent)
    {
        var minLat = double.PositiveInfinity;
        var minLon = double.PositiveInfinity;
        var maxLat = double.NegativeInfinity;
        var maxLon = double.NegativeInfinity;
        var any = false;

        static void Fold(
            IReadOnlyList<DataModel.GeoPosition> ring,
            ref double minLat, ref double minLon,
            ref double maxLat, ref double maxLon, ref bool any)
        {
            foreach (var p in ring)
            {
                if (p.Latitude < minLat) minLat = p.Latitude;
                if (p.Latitude > maxLat) maxLat = p.Latitude;
                if (p.Longitude < minLon) minLon = p.Longitude;
                if (p.Longitude > maxLon) maxLon = p.Longitude;
                any = true;
            }
        }

        Fold(f.Coordinates, ref minLat, ref minLon, ref maxLat, ref maxLon, ref any);
        foreach (var hole in f.InteriorRings)
        {
            Fold(hole, ref minLat, ref minLon, ref maxLat, ref maxLon, ref any);
        }

        if (!any) return false;
        if (maxLat < extent.SouthLatitude) return false;
        if (minLat > extent.NorthLatitude) return false;
        if (maxLon < extent.WestLongitude) return false;
        if (minLon > extent.EastLongitude) return false;
        return true;
    }

    /// <summary>Walks up from the test assembly to find the committed S-101 fixture directory.</summary>
    private static string ResolveFixturePath(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "datasets", "S101", "S-101", "DATASET_FILES", fileName);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return Path.Combine("tests", "datasets", "S101", "S-101", "DATASET_FILES", fileName);
    }
}
