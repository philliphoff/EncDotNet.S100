using System;
using System.Collections.Generic;
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
    private long _residentBytes;
    private bool _disposed;

    private sealed record Entry(TileKey Key, SKImage Image, long Bytes);

    /// <summary>
    /// Creates a cache with the given native-byte budget. Values ≤ 0 are
    /// clamped to a 1-tile floor so a single tile can always reside.
    /// </summary>
    public TileCache(long budgetBytes)
    {
        BudgetBytes = Math.Max(budgetBytes, MinBudgetBytes);
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
                existing.Value.Image.Dispose();
                _map.Remove(key);
            }

            var bytes = BytesFor(image);
            var node = _lru.AddFirst(new Entry(key, image, bytes));
            _map[key] = node;
            _residentBytes += bytes;

            while (_residentBytes > BudgetBytes && _lru.Last is { } last && last != node)
            {
                _lru.RemoveLast();
                _map.Remove(last.Value.Key);
                _residentBytes -= last.Value.Bytes;
                (evicted ??= new List<SKImage>()).Add(last.Value.Image);
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

    /// <summary>A snapshot of the currently-resident keys (no LRU reorder).</summary>
    public IReadOnlyList<TileKey> SnapshotKeys()
    {
        lock (_sync)
        {
            return new List<TileKey>(_map.Keys);
        }
    }

    /// <summary>Disposes and removes every resident tile.</summary>
    public void Clear()
    {
        List<SKImage> images;
        lock (_sync)
        {
            images = new List<SKImage>(_map.Count);
            foreach (var node in _map.Values)
            {
                images.Add(node.Value.Image);
            }

            _map.Clear();
            _lru.Clear();
            _residentBytes = 0;
        }

        foreach (var img in images)
        {
            img.Dispose();
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
    }
}
