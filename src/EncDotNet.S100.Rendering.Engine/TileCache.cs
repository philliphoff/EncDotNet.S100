using SkiaSharp;

namespace EncDotNet.S100.Rendering.Engine;

/// <summary>
/// A thread-safe cache of rasterised base-plane tiles for one layer, whose
/// native bytes are charged to a shared <see cref="TileMemoryPool"/>. The pool
/// keeps one least-recently-used order across all its caches and evicts from it
/// when the total passes the pool's limit (map rendering slice&#160;4), so the
/// tiles nobody has drawn for longest go first, whichever layer holds them.
/// Decoded <see cref="SKImage"/> pixels live in native memory, which is the
/// out-of-memory risk the design calls out (§3.4).
/// </summary>
/// <remarks>
/// Both the UI/compositor thread (via <see cref="TryGet"/> /
/// <see cref="SnapshotKeys"/>) and the worker threads (via <see cref="Put"/>)
/// touch the cache, so every operation takes the pool's lock. Eviction
/// disposes the <see cref="SKImage"/>; callers must therefore only use an image
/// returned by <see cref="TryGet"/> while they continue to reference it on the
/// UI thread within the same composite pass (the compositor does, and a tile in
/// use is also among the most-recently-used and pinned, so it is never the
/// eviction victim).
/// </remarks>
internal sealed class TileCache : IDisposable
{
    private readonly TileMemoryPool _pool;
    private readonly object _sync;
    private readonly Dictionary<TileKey, LinkedListNode<Entry>> _map = new();
    private readonly HashSet<TileKey> _protected = new();
    private readonly WeakReference? _owner;
    private readonly bool _deferDisposal;
    private long _protectedAt = long.MinValue;
    private long _generation;
    private long _residentBytes;
    private bool _disposed;

    /// <summary>One resident tile, linked into its pool's recency list.</summary>
    internal sealed record Entry(TileCache Owner, TileKey Key, SKImage Image, long Bytes)
    {
        /// <summary>Snapshots copying this tile's pixels outside the lock. Guarded by the pool's lock.</summary>
        public int Readers;

        /// <summary>Removed from the cache while a snapshot was reading it; the last reader disposes it.</summary>
        public bool Retired;
    }

    /// <summary>
    /// Creates a cache with a pool of its own, bounded by
    /// <paramref name="budgetBytes"/>. Values ≤ 0 are clamped to a 1-tile floor
    /// so a single tile can always reside.
    /// </summary>
    /// <param name="budgetBytes">The native-byte eviction budget.</param>
    /// <param name="deferDisposal">See <see cref="TileCache(TileMemoryPool, object?, bool)"/>.</param>
    public TileCache(long budgetBytes, bool deferDisposal = false)
        : this(new TileMemoryPool(() => budgetBytes, "private"), owner: null, deferDisposal)
    {
    }

    /// <summary>Creates a cache that charges its tiles to <paramref name="pool"/>.</summary>
    /// <param name="pool">The shared account the tiles are charged to and evicted from.</param>
    /// <param name="owner">
    /// The object whose lifetime the cache shares, or <see langword="null"/>.
    /// The pool holds the cache's tiles strongly, so once the owner has been
    /// collected the pool releases them itself (within about a second of its
    /// next insert) rather than leaving them to age out.
    /// </param>
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
    public TileCache(TileMemoryPool pool, object? owner, bool deferDisposal = false)
    {
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
        _sync = pool.Sync;
        _owner = owner is null ? null : new WeakReference(owner);
        _deferDisposal = deferDisposal;
        lock (_sync)
        {
            pool.Join(this);
        }
    }

    /// <summary>A floor so at least one reasonably-sized tile always fits.</summary>
    public const long MinBudgetBytes = 4L * 1024 * 1024;

    /// <summary>The pool this cache's tiles are charged to.</summary>
    public TileMemoryPool Pool => _pool;

    /// <summary>The pool's native-byte limit.</summary>
    public long BudgetBytes => _pool.LimitBytes;

    /// <summary>The native bytes this cache holds.</summary>
    public long ResidentBytes
    {
        get { lock (_sync) { return _residentBytes; } }
    }

    /// <summary>The current number of resident tiles.</summary>
    public int Count
    {
        get { lock (_sync) { return _map.Count; } }
    }

    /// <summary>Whether the owner passed at construction is still alive (always, without one).</summary>
    internal bool OwnerAlive => _owner is null || _owner.IsAlive;

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

            _pool.Touch(node);
            return node.Value.Image;
        }
    }

    /// <summary>
    /// Copies the resident tile for <paramref name="key"/> into an independent
    /// raster snapshot (for a deferred disk write), or returns
    /// <see langword="null"/> when the cache has been cleared since
    /// <paramref name="expectedGeneration"/>, the tile is absent, or it is no
    /// longer <paramref name="expectedPixelSize"/> pixels wide (it was replaced
    /// at another raster scale, so it would land in the wrong disk namespace).
    /// </summary>
    internal SKImage? TryCreateSnapshot(TileKey key, long expectedGeneration, int expectedPixelSize)
    {
        Entry entry;
        lock (_sync)
        {
            if (_disposed
                || _generation != expectedGeneration
                || !_map.TryGetValue(key, out var node)
                || node.Value.Image.Width != expectedPixelSize)
            {
                return null;
            }

            _pool.Touch(node);
            entry = node.Value;
            entry.Readers++;
        }

        // Copy outside the pool's lock (a 2x tile is about 1 MB), so the
        // compositor and other layers aren't held up; an eviction meanwhile
        // leaves the image to this reader to dispose.
        SKBitmap? pixels;
        try
        {
            pixels = TileDiskCache.CopyPixels(entry.Image);
        }
        finally
        {
            bool dispose;
            lock (_sync)
            {
                entry.Readers--;
                dispose = entry.Retired && entry.Readers == 0;
            }

            if (dispose)
            {
                entry.Image.Dispose();
            }
        }

        if (pixels is null)
        {
            return null;
        }

        using (pixels)
        {
            return TileDiskCache.CreateSnapshot(pixels);
        }
    }

    /// <summary>
    /// True when a tile for <paramref name="key"/> is resident, at any raster
    /// scale. Use this to decide whether something is <i>drawable</i>; use
    /// <see cref="Contains(TileKey, int)"/> to decide whether a frame's tile is
    /// already warm.
    /// </summary>
    public bool Contains(TileKey key)
    {
        lock (_sync)
        {
            return !_disposed && _map.ContainsKey(key);
        }
    }

    /// <summary>
    /// True when a tile for <paramref name="key"/> is resident <b>and</b> was
    /// rasterised at <paramref name="pixelSize"/> device pixels per edge.
    /// </summary>
    /// <remarks>
    /// A tile's pixel width is its raster-scale tag (see
    /// <c>S100VectorTileRenderer.TilePixelSize</c>): a 1x tile is a miss for a
    /// 2x frame, so the frame schedules a replacement instead of blitting an
    /// upscaled, soft tile forever. The mismatched tile stays resident (and
    /// drawable) until the replacement's <see cref="Put"/> supersedes it.
    /// </remarks>
    public bool Contains(TileKey key, int pixelSize)
    {
        lock (_sync)
        {
            return !_disposed
                && _map.TryGetValue(key, out var node)
                && node.Value.Image.Width == pixelSize;
        }
    }

    /// <summary>
    /// Inserts (or replaces) the image for <paramref name="key"/> as
    /// most-recently-used, then evicts the pool's least-recently-used tiles —
    /// from any member cache — until the pool is within its limit. Replacing an
    /// existing key disposes the prior image. If the cache is disposed the image
    /// is disposed immediately.
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
                Evict(existing.Value, ref evicted);
            }

            var entry = new Entry(this, key, image, BytesFor(image));
            var node = _pool.AddFirst(entry);
            _map[key] = node;
            _residentBytes += entry.Bytes;

            // Evict least-recently-used tiles to fit the budget, but never a
            // cache's pinned (currently-visible) tiles or the just-inserted node:
            // a tile in use must survive regardless of budget, or the compositor
            // would blink between a rendered and a blank tile when the working
            // set exceeds the budget.
            _pool.Trim(node, ref evicted);
        }

        DisposeAll(evicted);
    }

    /// <summary>
    /// Whether <paramref name="key"/> is pinned by this cache's latest
    /// <see cref="Protect"/> call and that pin still holds (see
    /// <see cref="TileMemoryPool.PinLapse"/>). Must be called under the pool's lock.
    /// </summary>
    internal bool IsPinned(TileKey key) =>
        _protected.Count > 0 && _protected.Contains(key) && _pool.PinHolds(_protectedAt);

    /// <summary>
    /// Removes <paramref name="entry"/> from this cache and the pool, retiring
    /// its image. Must be called under the pool's lock.
    /// </summary>
    internal void Evict(Entry entry, ref List<SKImage>? inline)
    {
        if (!_map.Remove(entry.Key, out var node))
        {
            return;
        }

        _pool.Remove(node);
        _residentBytes -= entry.Bytes;
        RetireImage(entry, ref inline);
    }

    /// <summary>
    /// Removes every resident tile from the pool, retiring the images. Must be
    /// called under the pool's lock.
    /// </summary>
    internal void ReleaseAll(ref List<SKImage>? inline)
    {
        _generation++;
        foreach (var node in _map.Values)
        {
            _pool.Remove(node);
            RetireImage(node.Value, ref inline);
        }

        _map.Clear();
        _protected.Clear();
        _residentBytes = 0;
    }

    /// <summary>
    /// Routes an image removed from the cache to inline disposal, or — when
    /// <see cref="_deferDisposal"/> is set — to the pool's pending-disposal list
    /// drained by <see cref="DrainPendingDisposals"/>. Must be called under the
    /// pool's lock.
    /// </summary>
    private void RetireImage(Entry entry, ref List<SKImage>? evictedInline)
    {
        var image = entry.Image;
        if (entry.Readers > 0)
        {
            // A snapshot is copying it outside the lock; it disposes the image.
            entry.Retired = true;
        }
        else if (_deferDisposal)
        {
            _pool.Retire(this, image);
        }
        else
        {
            (evictedInline ??= new List<SKImage>()).Add(image);
        }
    }

    /// <summary>
    /// Disposes images that were evicted/cleared from this cache since the last
    /// drain, plus any other member's that have waited past
    /// <see cref="TileMemoryPool.OrphanDisposalDelay"/>. For a deferred-disposal
    /// (GPU) cache the caller invokes this at the <b>start of a frame, before any
    /// draw is recorded</b>, so the images — last referenced by the previous
    /// (already-flushed) frame — are freed without risking a use-after-free in
    /// the current frame's deferred draws. Another layer's eviction can retire
    /// this cache's texture after it was drawn in the current frame; it is still
    /// freed only at this cache's next frame, or once the delay has passed. A
    /// no-op for an inline-disposal cache.
    /// </summary>
    public void DrainPendingDisposals()
    {
        List<SKImage>? pending;
        lock (_sync)
        {
            pending = _pool.TakeDisposable(this);
        }

        DisposeAll(pending);
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
    /// Replaces the set of <b>pinned</b> keys that eviction must never take,
    /// regardless of budget. The compositor passes the current visible
    /// (target-band) tiles each frame so a tile in active use cannot be evicted
    /// by speculative/predicted inserts, which would otherwise flicker the tile
    /// blank when the working set exceeds the budget. Keys not currently
    /// resident are harmless; they simply protect the tile once it lands. The
    /// pins lapse once other caches in the pool have renewed theirs for
    /// <see cref="TileMemoryPool.PinLapse"/> without this one doing so.
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

            _protectedAt = _pool.Now();
            _pool.NotePin(_protectedAt);
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
            ReleaseAll(ref inline);
        }

        DisposeAll(inline);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        List<SKImage>? inline = null;
        lock (_sync)
        {
            _disposed = true;
            ReleaseAll(ref inline);
            _pool.Leave(this);
        }

        DisposeAll(inline);

        // Teardown frees everything now (the render thread owns the GPU context
        // at this point and no draw for this cache is in flight): drain any
        // images Clear() may have deferred.
        DrainPendingDisposals();
    }

    private static void DisposeAll(List<SKImage>? images)
    {
        if (images is null)
        {
            return;
        }

        foreach (var img in images)
        {
            img.Dispose();
        }
    }
}
