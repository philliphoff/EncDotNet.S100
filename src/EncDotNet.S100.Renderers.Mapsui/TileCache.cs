using SkiaSharp;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// A thread-safe, least-recently-used cache of rasterised base-plane tiles
/// (S-100 render subsystem, Phase&#160;2), bounded by a hard <b>native-byte
/// budget</b> rather than an entry count — decoded <see cref="SKImage"/> pixels
/// live in native memory, which is the out-of-memory risk the design calls out
/// (§3.4). When a <see cref="Put"/> pushes the resident total over budget the
/// least-recently-used tiles are evicted (and disposed) until it fits.
/// </summary>
/// <remarks>
/// Both the UI/compositor thread (via <see cref="TryGet"/> /
/// <see cref="SnapshotKeys"/>) and the worker thread (via <see cref="Put"/>)
/// touch the cache, so every operation takes the internal lock. Eviction
/// disposes the <see cref="SKImage"/>; callers must therefore only use an image
/// returned by <see cref="TryGet"/> while they continue to reference it on the
/// UI thread within the same composite pass (the compositor does, and a tile in
/// use is also the most-recently-used, so it is never the eviction victim).
/// </remarks>
internal sealed class TileCache : IDisposable
{
    private readonly object _sync = new();
    private readonly Dictionary<TileKey, LinkedListNode<Entry>> _map = new();
    private readonly LinkedList<Entry> _lru = new();
    private readonly HashSet<TileKey> _protected = new();
    private readonly bool _deferDisposal;
    private List<SKImage>? _pendingDisposal;
    private long _residentBytes;
    private bool _disposed;

    private sealed record Entry(TileKey Key, SKImage Image, long Bytes);

    /// <summary>
    /// Creates a cache with the given native-byte budget. Values ≤ 0 are
    /// clamped to a 1-tile floor so a single tile can always reside.
    /// </summary>
    /// <param name="budgetBytes">The native-byte eviction budget.</param>
    /// <param name="deferDisposal">
    /// When <see langword="true"/>, images evicted or cleared from the cache are
    /// <b>not</b> disposed inline; they are held until <see cref="DrainPendingDisposals"/>
    /// is called. This is required for a cache of <b>GPU-backed</b>
    /// <see cref="SKImage"/>s drawn through a deferred canvas (Phase&#160;5
    /// residency): <c>SKCanvas.DrawImage</c> only records the draw, and the GPU
    /// flush happens after the render method returns, so a texture freed in the
    /// same frame it was drawn would be a use-after-free in the native GPU
    /// backend. Deferring disposal to the start of the next frame — after the
    /// previous frame has flushed — makes eviction safe regardless of budget.
    /// The raster cache leaves this <see langword="false"/> (CPU images are safe
    /// to free inline).
    /// </param>
    public TileCache(long budgetBytes, bool deferDisposal = false)
    {
        BudgetBytes = Math.Max(budgetBytes, MinBudgetBytes);
        _deferDisposal = deferDisposal;
    }

    /// <summary>A floor so at least one reasonably-sized tile always fits.</summary>
    public const long MinBudgetBytes = 4L * 1024 * 1024;

    /// <summary>The native-byte budget; eviction keeps the resident total at or under this.</summary>
    public long BudgetBytes { get; }

    /// <summary>The current resident native-byte total.</summary>
    public long ResidentBytes
    {
        get { lock (_sync) { return _residentBytes; } }
    }

    /// <summary>The current number of resident tiles.</summary>
    public int Count
    {
        get { lock (_sync) { return _map.Count; } }
    }

    /// <summary>The native bytes a decoded RGBA image of this pixel size occupies.</summary>
    public static long BytesFor(SKImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return (long)image.Width * image.Height * 4;
    }

    /// <summary>
    /// Returns the cached image for <paramref name="key"/> and marks it
    /// most-recently-used, or <see langword="null"/> when absent.
    /// </summary>
    public SKImage? TryGet(TileKey key)
    {
        lock (_sync)
        {
            if (_disposed || !_map.TryGetValue(key, out var node))
            {
                return null;
            }

            _lru.Remove(node);
            _lru.AddFirst(node);
            return node.Value.Image;
        }
    }

    /// <summary>True when a tile for <paramref name="key"/> is resident.</summary>
    public bool Contains(TileKey key)
    {
        lock (_sync)
        {
            return !_disposed && _map.ContainsKey(key);
        }
    }

    /// <summary>
    /// Inserts (or replaces) the image for <paramref name="key"/> as
    /// most-recently-used, then evicts least-recently-used tiles until the
    /// resident total is within <see cref="BudgetBytes"/>. Replacing an existing
    /// key disposes the prior image. If the cache is disposed the image is
    /// disposed immediately.
    /// </summary>
    public void Put(TileKey key, SKImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        List<SKImage>? evicted = null;
        lock (_sync)
        {
            if (_disposed)
            {
                image.Dispose();
                return;
            }

            if (_map.TryGetValue(key, out var existing))
            {
                _residentBytes -= existing.Value.Bytes;
                _lru.Remove(existing);
                RetireImage(existing.Value.Image, ref evicted);
                _map.Remove(key);
            }

            var bytes = BytesFor(image);
            var node = _lru.AddFirst(new Entry(key, image, bytes));
            _map[key] = node;
            _residentBytes += bytes;

            // Evict least-recently-used tiles to fit the budget, but never the
            // pinned (currently-visible) tiles or the just-inserted node: a tile
            // in use must survive regardless of budget, or the compositor would
            // blink between a rendered and a blank tile when the working set
            // exceeds the cache size. Walk from the LRU end skipping protected
            // keys; stop once the only remaining candidates are pinned.
            var node2 = _lru.Last;
            while (_residentBytes > BudgetBytes && node2 is not null)
            {
                var prev = node2.Previous;
                if (node2 != node && !_protected.Contains(node2.Value.Key))
                {
                    _lru.Remove(node2);
                    _map.Remove(node2.Value.Key);
                    _residentBytes -= node2.Value.Bytes;
                    RetireImage(node2.Value.Image, ref evicted);
                }

                node2 = prev;
            }
        }

        if (evicted is not null)
        {
            foreach (var img in evicted)
            {
                img.Dispose();
            }
        }
    }

    /// <summary>
    /// Routes an image removed from the cache to inline disposal, or — when
    /// <see cref="_deferDisposal"/> is set — to the pending-disposal list drained
    /// by <see cref="DrainPendingDisposals"/>. Must be called under <c>_sync</c>.
    /// </summary>
    private void RetireImage(SKImage image, ref List<SKImage>? evictedInline)
    {
        if (_deferDisposal)
        {
            (_pendingDisposal ??= new List<SKImage>()).Add(image);
        }
        else
        {
            (evictedInline ??= new List<SKImage>()).Add(image);
        }
    }

    /// <summary>
    /// Disposes images that were evicted/cleared since the last drain. For a
    /// deferred-disposal (GPU) cache the caller invokes this at the <b>start of a
    /// frame, before any draw is recorded</b>, so the images — last referenced by
    /// the previous (already-flushed) frame — are freed without risking a
    /// use-after-free in the current frame's deferred draws. A no-op for an
    /// inline-disposal cache.
    /// </summary>
    public void DrainPendingDisposals()
    {
        List<SKImage>? pending;
        lock (_sync)
        {
            pending = _pendingDisposal;
            _pendingDisposal = null;
        }

        if (pending is not null)
        {
            foreach (var img in pending)
            {
                img.Dispose();
            }
        }
    }

    /// <summary>A snapshot of the currently-resident keys (no LRU reorder).</summary>
    public IReadOnlyList<TileKey> SnapshotKeys()
    {
        lock (_sync)
        {
            return new List<TileKey>(_map.Keys);
        }
    }

    /// <summary>
    /// Replaces the set of <b>pinned</b> keys that <see cref="Put"/> must never
    /// evict, regardless of budget. The compositor passes the current visible
    /// (target-band) tiles each frame so a tile in active use cannot be evicted
    /// by speculative/predicted inserts, which would otherwise flicker the tile
    /// blank when the working set exceeds the cache size. Keys not currently
    /// resident are harmless; they simply protect the tile once it lands.
    /// </summary>
    public void Protect(IReadOnlyCollection<TileKey> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        lock (_sync)
        {
            _protected.Clear();
            foreach (var k in keys)
            {
                _protected.Add(k);
            }
        }
    }

    /// <summary>
    /// Removes every resident tile. Images are disposed inline, or — for a
    /// deferred-disposal (GPU) cache — held for the next
    /// <see cref="DrainPendingDisposals"/> so a tile still referenced by the
    /// in-flight frame's deferred draws is not freed early.
    /// </summary>
    public void Clear()
    {
        List<SKImage>? inline = null;
        lock (_sync)
        {
            foreach (var node in _map.Values)
            {
                RetireImage(node.Value.Image, ref inline);
            }

            _map.Clear();
            _lru.Clear();
            _protected.Clear();
            _residentBytes = 0;
        }

        if (inline is not null)
        {
            foreach (var img in inline)
            {
                img.Dispose();
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
        }

        Clear();
        // Teardown frees everything now (the render thread owns the GPU context
        // at this point and no draw for this cache is in flight): drain any
        // images Clear() may have deferred.
        DrainPendingDisposals();
    }
}
