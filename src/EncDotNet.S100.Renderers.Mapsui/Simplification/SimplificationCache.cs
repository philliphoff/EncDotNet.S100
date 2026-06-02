using System;
using System.Collections.Generic;
using System.Diagnostics;
using Mapsui;
using S100Diag = EncDotNet.S100.Renderers.Mapsui.Diagnostics;

namespace EncDotNet.S100.Renderers.Mapsui.Simplification;

/// <summary>
/// Per-layer cache of simplified <see cref="IFeature"/>s keyed by
/// <c>(original-feature reference, zoom-bucket)</c>.  Owned by an
/// <see cref="InstrumentedMemoryLayer"/> and consulted on every
/// <c>GetFeatures</c> call.
/// </summary>
/// <remarks>
/// <para>
/// <b>Bucketing.</b>  Resolution (m/px) is mapped to a half-octave
/// bucket index — <c>round(log2(resolution) × 2)</c> — so each bucket
/// spans a √2 zoom range. Tolerance for the bucket is
/// <c>options.PixelTolerance × 2^(bucket / 2)</c> metres.  Resolution
/// is clamped to <c>[1e-3, 1e9]</c> before bucketing to guard against
/// zero / negative / NaN inputs.
/// </para>
/// <para>
/// <b>Eviction.</b>  When the active bucket changes, entries from
/// buckets outside <c>[active − 1, active + 1]</c> are dropped.  If
/// the cache's tracked coordinate count still exceeds
/// <see cref="SimplificationOptions.MaxCachedCoordinates"/>, buckets
/// farthest from the active one are evicted next, until the budget
/// is satisfied.  The budget is on coordinate count rather than
/// entry count because one 10 000-vertex polyline costs the same as
/// 10 000 small features.
/// </para>
/// <para>
/// <b>Thread safety.</b>  Mapsui invokes <c>GetFeatures</c> on the
/// render thread, so the cache is single-threaded by contract; an
/// internal lock is taken anyway so simultaneous renders on
/// multiple maps sharing a layer remain correct.
/// </para>
/// </remarks>
public sealed class SimplificationCache
{
    /// <summary>Lower bound on bucketed resolution to guard against
    /// zero / NaN / negative inputs from Mapsui.</summary>
    private const double MinResolution = 1e-3;

    /// <summary>Upper bound — anything beyond ~1&#160;Gm/px is global
    /// scale and a degenerate input.</summary>
    private const double MaxResolution = 1e9;

    private readonly IFeatureSimplifier _simplifier;
    private readonly SimplificationOptions _options;
    private readonly KeyValuePair<string, object?>[] _tags;
    private readonly object _gate = new();

    // Outer key: bucket index. Inner key: original-feature reference.
    // Reference equality is correct because MemoryLayer.Features is
    // built once at Render() time and never mutated.
    private readonly Dictionary<int, Dictionary<IFeature, CacheEntry>> _byBucket = new();
    private long _cachedCoordinates;
    private int? _activeBucket;

    /// <summary>
    /// Creates a cache that delegates simplification to
    /// <paramref name="simplifier"/> and uses
    /// <paramref name="options"/> to size, threshold, and bound itself.
    /// <paramref name="product"/> is attached as the
    /// <c>s100.product</c> dimension on emitted metrics.
    /// </summary>
    public SimplificationCache(
        IFeatureSimplifier simplifier,
        SimplificationOptions options,
        string? product = null)
    {
        ArgumentNullException.ThrowIfNull(simplifier);
        ArgumentNullException.ThrowIfNull(options);

        _simplifier = simplifier;
        _options = options;
        _tags = product is null
            ? Array.Empty<KeyValuePair<string, object?>>()
            : new[] { new KeyValuePair<string, object?>("s100.product", product) };
    }

    /// <summary>
    /// Total number of simplified coordinates currently retained,
    /// summed across all buckets. Surfaced for tests + the
    /// <c>s100.simplify.cache.coords.tracked</c> gauge.
    /// </summary>
    public long CachedCoordinateCount
    {
        get { lock (_gate) return _cachedCoordinates; }
    }

    /// <summary>
    /// Total number of cached entries across all buckets. Useful for
    /// tests; not otherwise surfaced.
    /// </summary>
    public int CachedEntryCount
    {
        get
        {
            lock (_gate)
            {
                int n = 0;
                foreach (var bucket in _byBucket.Values) n += bucket.Count;
                return n;
            }
        }
    }

    /// <summary>
    /// Computes the half-octave bucket index for
    /// <paramref name="resolution"/>.  Exposed for tests and for the
    /// pre-warm path.
    /// </summary>
    public static int BucketFor(double resolution)
    {
        if (double.IsNaN(resolution) || resolution <= 0)
            resolution = MinResolution;
        if (resolution < MinResolution) resolution = MinResolution;
        if (resolution > MaxResolution) resolution = MaxResolution;
        return (int)Math.Round(Math.Log2(resolution) * 2.0);
    }

    /// <summary>
    /// Computes the simplification tolerance in metres for the
    /// supplied bucket index, given the configured pixel tolerance.
    /// </summary>
    public double ToleranceFor(int bucket)
        => _options.PixelTolerance * Math.Pow(2.0, bucket / 2.0);

    /// <summary>
    /// Returns a feature suitable for rendering at the given
    /// <paramref name="resolution"/>: the simplified clone if one is
    /// available (or just produced), or <paramref name="feature"/>
    /// itself if the simplifier passed it through.
    /// </summary>
    public IFeature GetOrSimplify(IFeature feature, double resolution)
    {
        ArgumentNullException.ThrowIfNull(feature);

        var bucket = BucketFor(resolution);

        lock (_gate)
        {
            if (_activeBucket != bucket)
            {
                _activeBucket = bucket;
                EvictForBucketShift(bucket);
            }

            if (!_byBucket.TryGetValue(bucket, out var entries))
            {
                entries = new Dictionary<IFeature, CacheEntry>(ReferenceEqualityComparer.Instance);
                _byBucket[bucket] = entries;
            }

            if (entries.TryGetValue(feature, out var existing))
            {
                S100Diag.Telemetry.SimplifyCacheHit.Add(1, _tags);
                return existing.Feature;
            }
        }

        // Simplify outside the lock: NTS work is the slow path and
        // shouldn't block other layers' GetFeatures calls.
        var tolerance = ToleranceFor(bucket);
        var sw = Stopwatch.StartNew();
        var result = _simplifier.Simplify(feature, tolerance, _options);
        sw.Stop();

        S100Diag.Telemetry.SimplifyCacheMiss.Add(1, _tags);
        S100Diag.Telemetry.SimplifyDuration.Record(sw.Elapsed.TotalMilliseconds, _tags);
        if (result.OriginalCoordinateCount > 0)
        {
            S100Diag.Telemetry.SimplifyCoordsIn.Record(result.OriginalCoordinateCount, _tags);
            S100Diag.Telemetry.SimplifyCoordsOut.Record(result.SimplifiedCoordinateCount, _tags);
        }

        // Cost of caching: we also store identity passthroughs so
        // we don't re-test (e.g. count vertices, query NTS) on every
        // frame for short or unsupported geometries.
        var entry = new CacheEntry(result.Feature, result.SimplifiedCoordinateCount);

        lock (_gate)
        {
            // Re-fetch entries dict in case eviction nuked it between
            // the unlock and re-lock above.
            if (!_byBucket.TryGetValue(bucket, out var entries))
            {
                entries = new Dictionary<IFeature, CacheEntry>(ReferenceEqualityComparer.Instance);
                _byBucket[bucket] = entries;
            }

            if (!entries.TryAdd(feature, entry))
            {
                // Another thread populated the same key. Use theirs;
                // reuse keeps object identity stable for downstream
                // hit-tests.
                S100Diag.Telemetry.SimplifyCacheCoordsTracked.Add(0, _tags);
                return entries[feature].Feature;
            }

            _cachedCoordinates += entry.CoordCount;
            S100Diag.Telemetry.SimplifyCacheCoordsTracked.Add(entry.CoordCount, _tags);

            EvictForBudget(bucket);

            return result.Feature;
        }
    }

    /// <summary>
    /// Drops every cached entry. Used when a layer is being torn
    /// down or the simplifier is reconfigured.
    /// </summary>
    public void Clear()
    {
        lock (_gate)
        {
            if (_cachedCoordinates > 0)
                S100Diag.Telemetry.SimplifyCacheCoordsTracked.Add(-_cachedCoordinates, _tags);
            _byBucket.Clear();
            _cachedCoordinates = 0;
            _activeBucket = null;
        }
    }

    private void EvictForBucketShift(int activeBucket)
    {
        // Drop everything outside [active-1, active+1].
        if (_byBucket.Count == 0) return;
        List<int>? toDrop = null;
        foreach (var b in _byBucket.Keys)
        {
            if (Math.Abs(b - activeBucket) > 1)
                (toDrop ??= new List<int>()).Add(b);
        }
        if (toDrop is null) return;
        foreach (var b in toDrop) DropBucket(b);
    }

    private void EvictForBudget(int activeBucket)
    {
        if (_cachedCoordinates <= _options.MaxCachedCoordinates) return;
        if (_byBucket.Count <= 1) return;

        // Sort buckets by distance from the active one (descending),
        // then by index. Drop until under budget or only the active
        // bucket remains.
        var ordered = new List<int>(_byBucket.Keys);
        ordered.Sort((a, b) =>
        {
            int da = Math.Abs(a - activeBucket);
            int db = Math.Abs(b - activeBucket);
            return db.CompareTo(da);
        });

        foreach (var b in ordered)
        {
            if (b == activeBucket) continue;
            DropBucket(b);
            if (_cachedCoordinates <= _options.MaxCachedCoordinates) return;
        }
    }

    private void DropBucket(int bucket)
    {
        if (!_byBucket.TryGetValue(bucket, out var entries)) return;
        long delta = 0;
        foreach (var e in entries.Values) delta += e.CoordCount;
        _byBucket.Remove(bucket);
        _cachedCoordinates -= delta;
        if (delta > 0)
            S100Diag.Telemetry.SimplifyCacheCoordsTracked.Add(-delta, _tags);
    }

    private readonly struct CacheEntry
    {
        public CacheEntry(IFeature feature, long coordCount)
        {
            Feature = feature;
            CoordCount = coordCount;
        }
        public IFeature Feature { get; }
        public long CoordCount { get; }
    }
}
