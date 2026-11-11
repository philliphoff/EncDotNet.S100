using System.Diagnostics;
using EncDotNet.S100.Diagnostics;

namespace EncDotNet.S100.Pipelines.Vector.Spatial;

/// <summary>
/// STR-packed (Sort-Tile-Recurse) R-tree over feature MBRs. Built once
/// from a fully-materialised feature list; nodes are immutable and
/// queries are stateless, so instances are thread-safe.
/// </summary>
/// <remarks>
/// <para>
/// STR (Leutenegger, Edgington, Lopez, "STR: A Simple and Efficient
/// Algorithm for R-Tree Packing", 1997) bulk-loads a balanced tree in
/// <c>O(n log n)</c> by recursively slicing the input on centroid X and
/// centroid Y. It produces a fully-packed leaf level with no wasted
/// slots and near-optimal query performance for the static
/// bounding-box workload this codebase has (dataset is loaded once,
/// features never move).
/// </para>
/// <para>
/// Fanout <c>M</c> is fixed at 16 — the classical sweet spot for
/// disk-friendly node sizes; for an in-memory tree over feature MBRs
/// it also happens to be well below the point where linear DFS per
/// node dominates the intersect test. No public knob: if a measured
/// workload ever demands tuning, the constant becomes a parameter
/// then, not before (per issue #490 review guidance).
/// </para>
/// </remarks>
internal sealed class StrRTree : IVectorSpatialIndex
{
    private const int Fanout = 16;

    /// <summary>
    /// Datasets with at most this many features skip the tree build
    /// entirely — a linear scan across a handful of MBRs beats tree
    /// descent overhead. Set equal to <see cref="Fanout"/> so a
    /// build-worthy tree always has at least two leaves.
    /// </summary>
    private const int SmallDatasetThreshold = Fanout;

    private readonly Feature[] _features;
    private readonly FeatureMbr[] _mbrs;
    private readonly Node _root;
    private readonly string? _productTag;

    private StrRTree(Feature[] features, FeatureMbr[] mbrs, Node root, string? productTag)
    {
        _features = features;
        _mbrs = mbrs;
        _root = root;
        _productTag = productTag;
    }

    public int Count => _features.Length;

    public BoundingBox? Extent =>
        _features.Length == 0 ? null : _root.Mbr.ToBoundingBox();

    public IReadOnlyList<Feature> All() => _features;

    /// <summary>
    /// Builds an <see cref="IVectorSpatialIndex"/> over <paramref name="features"/>.
    /// Features whose <see cref="FeatureMbr.Compute"/> returns
    /// <see langword="null"/> (empty geometry) are dropped — matching
    /// the pre-existing linear scan in
    /// <c>S101VectorSource.GetFeatures</c>, which skips features whose
    /// resolved geometry has zero coordinates.
    /// </summary>
    /// <param name="features">Feature list to bulk-load into the tree.</param>
    /// <param name="productTag">
    /// Optional value for the <see cref="TelemetryTags.Product"/> tag
    /// attached to the build and per-query metrics. When
    /// <see langword="null"/> the metrics are still emitted but without
    /// a product dimension.
    /// </param>
    public static IVectorSpatialIndex Build(IReadOnlyList<Feature> features, string? productTag = null)
    {
        ArgumentNullException.ThrowIfNull(features);

        using var buildActivity = Telemetry.ActivitySource.StartActivity(
            "s100.vector.index.build",
            ActivityKind.Internal);
        if (productTag is not null)
        {
            buildActivity?.SetTag(TelemetryTags.Product, productTag);
        }

        var buildStart = Stopwatch.GetTimestamp();

        var validFeatures = new List<Feature>(features.Count);
        var validMbrs = new List<FeatureMbr>(features.Count);
        for (var i = 0; i < features.Count; i++)
        {
            var f = features[i];
            var mbr = FeatureMbr.Compute(f);
            if (mbr.HasValue)
            {
                validFeatures.Add(f);
                validMbrs.Add(mbr.Value);
            }
        }

        IVectorSpatialIndex result;
        if (validFeatures.Count == 0)
        {
            result = new EmptyIndex();
        }
        else
        {
            var featureArray = validFeatures.ToArray();
            var mbrArray = validMbrs.ToArray();
            var root = BuildTree(featureArray, mbrArray);
            result = new StrRTree(featureArray, mbrArray, root, productTag);
        }

        var elapsedMs = Stopwatch.GetElapsedTime(buildStart).TotalMilliseconds;
        var buildTags = new TagList();
        if (productTag is not null)
        {
            buildTags.Add(TelemetryTags.Product, productTag);
        }
        PipelineMetrics.VectorIndexBuildDuration.Record(elapsedMs, buildTags);
        PipelineMetrics.VectorIndexFeatureCount.Record(result.Count, buildTags);
        buildActivity?.SetTag("s100.vector.index.features", result.Count);
        buildActivity?.SetTag("s100.vector.index.build.ms", elapsedMs);

        return result;
    }

    public IReadOnlyList<Feature> Query(BoundingBox extent)
    {
        ArgumentNullException.ThrowIfNull(extent);

        var start = Stopwatch.GetTimestamp();

        IReadOnlyList<Feature> results;
        if (_features.Length == 0)
        {
            results = [];
        }
        else
        {
            // Fast path for extent covering the whole dataset.
            var rootMbr = _root.Mbr;
            if (extent.SouthLatitude <= rootMbr.MinLat
                && extent.WestLongitude <= rootMbr.MinLon
                && extent.NorthLatitude >= rootMbr.MaxLat
                && extent.EastLongitude >= rootMbr.MaxLon)
            {
                results = _features;
            }
            else
            {
                var collected = new List<Feature>();
                Descend(_root, extent, collected);
                results = collected;
            }
        }

        var elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        var tags = new TagList
        {
            { TelemetryTags.Result, results.Count > 0 ? "hit" : "empty" },
        };
        if (_productTag is not null)
        {
            tags.Add(TelemetryTags.Product, _productTag);
        }
        PipelineMetrics.VectorIndexQueryDuration.Record(elapsedMs, tags);
        PipelineMetrics.VectorIndexReturnedCount.Record(results.Count, tags);

        return results;
    }

    private static void Descend(Node node, BoundingBox extent, List<Feature> results)
    {
        if (!node.Mbr.Intersects(extent))
        {
            return;
        }

        if (node.Leaves is { } leaves)
        {
            var leafMbrs = node.LeafMbrs!;
            for (var i = 0; i < leaves.Length; i++)
            {
                if (leafMbrs[i].Intersects(extent))
                {
                    results.Add(leaves[i]);
                }
            }
            return;
        }

        var children = node.Children!;
        for (var i = 0; i < children.Length; i++)
        {
            Descend(children[i], extent, results);
        }
    }

    // ── STR bulk-load ─────────────────────────────────────────────────

    private static Node BuildTree(Feature[] features, FeatureMbr[] mbrs)
    {
        var n = features.Length;

        // Small datasets: a single leaf node holding every feature. The
        // MBR test loop is faster than any tree descent below this
        // threshold, and we still need a Node to serve as root.
        if (n <= SmallDatasetThreshold)
        {
            return NewLeaf(features, mbrs);
        }

        // Level 0: pack leaves. Sort by X-centroid, tile into ceil(sqrt(n/M))
        // vertical slices, then sort each slice by Y-centroid and slice
        // into leaves of size <= M.
        var indices = new int[n];
        for (var i = 0; i < n; i++) indices[i] = i;

        var leaves = PackLeaves(features, mbrs, indices);

        // Levels 1..k: repeatedly pack the current level of nodes into
        // parent nodes using the same slice-then-slice recipe on the
        // nodes' MBR centroids.
        var currentLevel = leaves;
        while (currentLevel.Length > 1)
        {
            currentLevel = PackLevel(currentLevel);
        }

        return currentLevel[0];
    }

    private static Node[] PackLeaves(Feature[] features, FeatureMbr[] mbrs, int[] indices)
    {
        var n = indices.Length;
        var leafCount = (n + Fanout - 1) / Fanout;
        var sliceCount = (int)Math.Max(1, Math.Ceiling(Math.Sqrt((double)leafCount)));
        var sliceSize = (int)Math.Ceiling((double)n / sliceCount);

        // Sort by X-centroid (i.e. centre longitude).
        Array.Sort(indices, (a, b) => mbrs[a].CenterLon.CompareTo(mbrs[b].CenterLon));

        var leaves = new List<Node>(leafCount);
        for (var s = 0; s < sliceCount; s++)
        {
            var sliceStart = s * sliceSize;
            if (sliceStart >= n) break;
            var sliceEnd = Math.Min(sliceStart + sliceSize, n);
            var sliceLen = sliceEnd - sliceStart;

            // Sort this slice by Y-centroid (centre latitude).
            Array.Sort(indices, sliceStart, sliceLen,
                Comparer<int>.Create((a, b) => mbrs[a].CenterLat.CompareTo(mbrs[b].CenterLat)));

            for (var i = sliceStart; i < sliceEnd; i += Fanout)
            {
                var runEnd = Math.Min(i + Fanout, sliceEnd);
                var runLen = runEnd - i;
                var runFeatures = new Feature[runLen];
                var runMbrs = new FeatureMbr[runLen];
                for (var k = 0; k < runLen; k++)
                {
                    runFeatures[k] = features[indices[i + k]];
                    runMbrs[k] = mbrs[indices[i + k]];
                }
                leaves.Add(NewLeaf(runFeatures, runMbrs));
            }
        }

        return leaves.ToArray();
    }

    private static Node[] PackLevel(Node[] nodes)
    {
        var n = nodes.Length;
        var parentCount = (n + Fanout - 1) / Fanout;
        var sliceCount = (int)Math.Max(1, Math.Ceiling(Math.Sqrt((double)parentCount)));
        var sliceSize = (int)Math.Ceiling((double)n / sliceCount);

        Array.Sort(nodes, (a, b) => a.Mbr.CenterLon.CompareTo(b.Mbr.CenterLon));

        var parents = new List<Node>(parentCount);
        for (var s = 0; s < sliceCount; s++)
        {
            var sliceStart = s * sliceSize;
            if (sliceStart >= n) break;
            var sliceEnd = Math.Min(sliceStart + sliceSize, n);
            var sliceLen = sliceEnd - sliceStart;

            Array.Sort(nodes, sliceStart, sliceLen,
                Comparer<Node>.Create((a, b) => a.Mbr.CenterLat.CompareTo(b.Mbr.CenterLat)));

            for (var i = sliceStart; i < sliceEnd; i += Fanout)
            {
                var runEnd = Math.Min(i + Fanout, sliceEnd);
                var runLen = runEnd - i;
                var children = new Node[runLen];
                Array.Copy(nodes, i, children, 0, runLen);
                parents.Add(NewInternal(children));
            }
        }

        return parents.ToArray();
    }

    private static Node NewLeaf(Feature[] features, FeatureMbr[] mbrs)
    {
        var union = mbrs[0];
        for (var i = 1; i < mbrs.Length; i++)
        {
            union = FeatureMbr.Union(union, mbrs[i]);
        }
        return new Node(union, null, features, mbrs);
    }

    private static Node NewInternal(Node[] children)
    {
        var union = children[0].Mbr;
        for (var i = 1; i < children.Length; i++)
        {
            union = FeatureMbr.Union(union, children[i].Mbr);
        }
        return new Node(union, children, null, null);
    }

    /// <summary>
    /// Tree node. Either a leaf (<see cref="Leaves"/> and
    /// <see cref="LeafMbrs"/> non-null) or an internal node
    /// (<see cref="Children"/> non-null).
    /// </summary>
    private sealed class Node
    {
        public FeatureMbr Mbr { get; }
        public Node[]? Children { get; }
        public Feature[]? Leaves { get; }
        public FeatureMbr[]? LeafMbrs { get; }

        public Node(FeatureMbr mbr, Node[]? children, Feature[]? leaves, FeatureMbr[]? leafMbrs)
        {
            Mbr = mbr;
            Children = children;
            Leaves = leaves;
            LeafMbrs = leafMbrs;
        }
    }

    private sealed class EmptyIndex : IVectorSpatialIndex
    {
        public int Count => 0;
        public BoundingBox? Extent => null;
        public IReadOnlyList<Feature> Query(BoundingBox extent) => [];
        public IReadOnlyList<Feature> All() => [];
    }
}
