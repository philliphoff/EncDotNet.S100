using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;

namespace EncDotNet.S100.Rendering.Scene;

/// <summary>
/// The part of a coarser cell that finer, currently-drawing cells hide, as
/// erased by the renderer's per-coverage difference clip at the live zoom
/// (issue #691). Answers "is this rectangle wholly hidden?" so a renderer can
/// skip drawing what the clip would erase anyway.
/// </summary>
/// <remarks>
/// A rectangle counts as hidden only when a <em>single</em> active finer
/// coverage covers it, not the union of several. The renderer clips each finer
/// coverage separately with anti-aliasing, so a pixel straddling the shared
/// edge of two adjacent finer cells is only partly removed by each clip and a
/// faint hairline of the coarser cell survives there. Skipping a tile across
/// such a seam would erase that hairline and change the picture; testing
/// coverages one at a time keeps the skip pixel-identical.
/// </remarks>
public sealed class HiddenCoverage
{
    private readonly HiddenCoverageCache.Item[] _items;
    private readonly int _activeCount;

    internal HiddenCoverage(HiddenCoverageCache.Item[] items, int activeCount)
    {
        _items = items;
        _activeCount = activeCount;
    }

    /// <summary>
    /// Whether the EPSG:3857 rectangle lies wholly inside one active finer
    /// coverage (boundary contact counts as inside). Callers pad the rectangle so
    /// the clip's anti-aliased edge never leaks a partly covered pixel.
    /// </summary>
    public bool Covers(double minX, double minY, double maxX, double maxY)
    {
        for (var i = 0; i < _activeCount; i++)
        {
            if (_items[i].Covers(minX, minY, maxX, maxY))
                return true;
        }

        return false;
    }
}

/// <summary>
/// Memoises <see cref="HiddenCoverage"/> for one coarser cell's finer-coverage
/// set (see <see cref="CoverageOverlap.CollectFinerCoverages"/>). A finer
/// coverage is active while the live zoom measure is at or below its
/// <see cref="FinerCoverage.Cutoff"/>, so sorting the
/// coverages by descending cutoff makes every active set a prefix; one
/// <see cref="HiddenCoverage"/> is kept per prefix length, so it stays the same
/// instance for as long as the active set does.
/// </summary>
public sealed class HiddenCoverageCache
{
    private readonly Item[] _byCutoffDescending;
    private readonly double[] _cutoffs;
    private readonly HiddenCoverage?[] _byActiveCount;

    /// <summary>Creates a cache over one cell's finer coverages.</summary>
    /// <param name="regions">The finer coverages.</param>
    public HiddenCoverageCache(IReadOnlyList<FinerCoverage> regions)
    {
        ArgumentNullException.ThrowIfNull(regions);
        var ordered = regions
            .Where(r => !r.Coverage.IsEmpty)
            .OrderByDescending(r => r.Cutoff)
            .ToArray();
        _cutoffs = [.. ordered.Select(r => r.Cutoff)];
        _byCutoffDescending = [.. ordered.Select(r => new Item(r.Coverage))];
        _byActiveCount = new HiddenCoverage?[ordered.Length + 1];
    }

    /// <summary>
    /// Gets the hidden region at the zoom measure <paramref name="resolution"/>
    /// (in the units of <see cref="FinerCoverage.Cutoff"/>), or
    /// <see langword="null"/> when no finer coverage is active there.
    /// </summary>
    /// <param name="resolution">The live zoom measure.</param>
    /// <returns>The hidden region, or <see langword="null"/>.</returns>
    public HiddenCoverage? Get(double resolution)
    {
        // Same activity predicate as the renderers' difference clips: a
        // coverage clips while the zoom measure is <= its cutoff.
        var active = 0;
        while (active < _cutoffs.Length && !(resolution > _cutoffs[active]))
            active++;

        if (active == 0)
            return null;

        // Benign race: two threads may each build an instance for the same
        // prefix; either is correct.
        return _byActiveCount[active] ??= new HiddenCoverage(_byCutoffDescending, active);
    }

    /// <summary>
    /// One finer coverage, validated and prepared lazily (on the first
    /// rectangle that passes its envelope) for repeated covers tests.
    /// </summary>
    internal sealed class Item(Geometry coverage)
    {
        private readonly Envelope _envelope = coverage.EnvelopeInternal;
        private readonly object _sync = new();
        private IPreparedGeometry? _prepared;
        private bool _resolved;

        public bool Covers(double minX, double minY, double maxX, double maxY)
        {
            if (minX < _envelope.MinX || minY < _envelope.MinY
                || maxX > _envelope.MaxX || maxY > _envelope.MaxY)
            {
                return false;
            }

            if (Prepared() is not { } prepared)
                return false;

            var rectangle = coverage.Factory.ToGeometry(new Envelope(minX, maxX, minY, maxY));
            return prepared.Covers(rectangle);
        }

        private IPreparedGeometry? Prepared()
        {
            lock (_sync)
            {
                if (!_resolved)
                {
                    // The clip draws each coverage with the even-odd rule, which
                    // matches its NTS area only when the geometry is valid; an
                    // invalid one never hides anything, so a rectangle the clip
                    // still shows is never claimed hidden.
                    try
                    {
                        _prepared = coverage.IsValid ? PreparedGeometryFactory.Prepare(coverage) : null;
                    }
                    catch (Exception)
                    {
                        _prepared = null;
                    }

                    _resolved = true;
                }

                return _prepared;
            }
        }
    }
}
