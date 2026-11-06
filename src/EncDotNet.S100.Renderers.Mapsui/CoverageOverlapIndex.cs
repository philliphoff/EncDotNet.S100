using EncDotNet.S100.DataModel;
using EncDotNet.S100.Rendering.Engine;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Quadtree;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Finds the finer, overlapping coverages of every cell in a chart stack
/// incrementally (map rendering slice 6): the same answer as
/// <see cref="OverlapSuppression.CollectFinerCoverages"/> for each cell, but
/// through a dynamic R-tree of coverage envelopes and a cache of exact
/// <see cref="Geometry.Intersects"/> results. A cell that lands tests its
/// coverage only against the cells whose envelopes it overlaps, once; every
/// later compose reuses those answers, so composing the stack no longer runs an
/// O(n²) NTS pass.
/// </summary>
/// <remarks>
/// Coverages are keyed by reference: a cell's coverage geometry is immutable
/// once rendered, and a re-render brings a new instance. Call
/// <see cref="Retain"/> with every coverage still loaded so evicted ones drop
/// out of the tree and the cache. Not thread-safe: the caller serialises
/// <see cref="Collect"/> and <see cref="Retain"/>.
/// </remarks>
internal sealed class CoverageOverlapIndex
{
    private readonly Quadtree<Geometry> _tree = new();
    private readonly Dictionary<Geometry, Dictionary<Geometry, bool>> _relations =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>Exact intersection tests run so far (a test seam: each pair is tested once).</summary>
    public long IntersectionTests { get; private set; }

    /// <summary>The coverages in the tree.</summary>
    public int Count => _relations.Count;

    /// <summary>
    /// Collects the finer coverages that clip each of <paramref name="cells"/>,
    /// in the order <see cref="OverlapSuppression.CollectFinerCoverages"/>
    /// returns them, or null for a cell that nothing finer overlaps.
    /// </summary>
    /// <param name="cells">The stack's cells, in stack order.</param>
    /// <returns>One entry per cell.</returns>
    public FinerCoverage[]?[] Collect(IReadOnlyList<OverlapSuppressionCell> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);

        // The cells that can clip or be clipped, by coverage. Two cells may
        // share one coverage instance; each is its own candidate.
        var byCoverage = new Dictionary<Geometry, List<int>>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < cells.Count; i++)
        {
            if (cells[i].Coverage is not { IsEmpty: false } coverage || cells[i].ScaleDenominator is null)
                continue;
            Add(coverage);
            if (!byCoverage.TryGetValue(coverage, out var list))
                byCoverage.Add(coverage, list = []);
            list.Add(i);
        }

        var result = new FinerCoverage[]?[cells.Count];
        var finer = new List<int>();
        foreach (var (coverage, owners) in byCoverage)
        {
            var envelope = coverage.EnvelopeInternal;
            finer.Clear();
            foreach (var candidate in _tree.Query(envelope))
            {
                if (!byCoverage.TryGetValue(candidate, out var others)
                    || !envelope.Intersects(candidate.EnvelopeInternal)
                    || !Intersects(coverage, candidate))
                {
                    continue;
                }

                finer.AddRange(others);
            }

            if (finer.Count == 0)
                continue;
            finer.Sort();
            foreach (var i in owners)
                result[i] = FinerOf(i, finer, cells);
        }

        return result;
    }

    /// <summary>
    /// Drops every coverage not in <paramref name="live"/> from the tree and
    /// the cache.
    /// </summary>
    /// <param name="live">The coverages of every loaded cell, drawing or not.</param>
    public void Retain(IEnumerable<Geometry?> live)
    {
        ArgumentNullException.ThrowIfNull(live);
        var keep = new HashSet<Geometry>(ReferenceEqualityComparer.Instance);
        foreach (var geometry in live)
        {
            if (geometry is not null)
                keep.Add(geometry);
        }

        List<Geometry>? stale = null;
        foreach (var geometry in _relations.Keys)
        {
            if (!keep.Contains(geometry))
                (stale ??= []).Add(geometry);
        }

        if (stale is null)
            return;
        foreach (var geometry in stale)
        {
            _tree.Remove(geometry.EnvelopeInternal, geometry);
            foreach (var partner in _relations[geometry].Keys)
            {
                if (!ReferenceEquals(partner, geometry) && _relations.TryGetValue(partner, out var back))
                    back.Remove(geometry);
            }

            _relations.Remove(geometry);
        }
    }

    // The same filters, in the same order, as
    // OverlapSuppression.CollectFinerCoverages; candidates arrive sorted by
    // stack index, the order that loop visits them.
    private static FinerCoverage[]? FinerOf(int index, List<int> overlapping, IReadOnlyList<OverlapSuppressionCell> cells)
    {
        var cell = cells[index];
        var denominator = cell.ScaleDenominator!.Value;
        List<FinerCoverage>? list = null;
        foreach (var j in overlapping)
        {
            if (j == index)
                continue;
            var other = cells[j];
            var otherDenominator = other.ScaleDenominator!.Value;
            if (!CellScaleRanking.IsFiner(otherDenominator, denominator))
                continue;
            var otherCoverage = other.Coverage!;
            var cutoff = other.CutoffScaleDenominator ?? otherDenominator;
            (list ??= []).Add(new FinerCoverage(
                otherCoverage,
                OverlapSuppression.ContentCutoffResolution(cutoff, otherCoverage)));
        }

        return list is null ? null : [.. list];
    }

    private void Add(Geometry coverage)
    {
        if (_relations.ContainsKey(coverage))
            return;
        _relations.Add(coverage, new Dictionary<Geometry, bool>(ReferenceEqualityComparer.Instance));
        _tree.Insert(coverage.EnvelopeInternal, coverage);
    }

    private bool Intersects(Geometry a, Geometry b)
    {
        if (ReferenceEquals(a, b))
            return true;
        var forA = _relations[a];
        if (forA.TryGetValue(b, out var known))
            return known;
        IntersectionTests++;
        var intersects = a.Intersects(b);
        forA[b] = intersects;
        _relations[b][a] = intersects;
        return intersects;
    }
}
