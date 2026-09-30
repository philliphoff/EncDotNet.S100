using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using EncDotNet.S100.Core.Caching;
using SkiaSharp;
using S100Diag = EncDotNet.S100.Renderers.Mapsui.Diagnostics.Telemetry;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// A persistent, on-disk <b>warm</b> cache of rasterised base-plane tiles
/// (S-100 render subsystem, Phase&#160;4, design §3.4): PNG-encoded tile images
/// keyed by <c>(namespace, band, x, y)</c>, where the <i>namespace</i> folds the
/// product/layer-set identity and a <c>styleStateHash</c> so a tile rendered
/// under one mariner/palette state can <b>never</b> be served for a different
/// one. It survives layer rebuilds (a palette flip-back re-uses the warm tiles
/// instead of re-rasterising) and process restarts.
/// </summary>
/// <remarks>
/// <para>
/// The in-memory <see cref="TileCache"/> is the hot tier (native-byte LRU, fresh
/// per layer); this is the warm tier shared across every layer and session. A
/// tile missing from the hot cache is looked up here on the worker thread before
/// a re-rasterise; a freshly rasterised tile is written here for future reuse.
/// </para>
/// <para>
/// <b>Correctness.</b> The cache is correct only because the namespace fully
/// captures the style state: the caller passes <c>(productLayerSet,
/// styleStateHash)</c> to <see cref="NamespaceFor"/>, and the renderer derives
/// <c>styleStateHash</c> from the resolved drawing instructions plus the palette
/// and symbol/text scales (see <c>MapsuiDisplayListRenderer</c>). A change to any
/// of those yields a different namespace — old tiles are simply orphaned and
/// reclaimed by the byte-budget LRU sweep, never served stale.
/// </para>
/// <para>
/// <b>Robustness</b> mirrors <c>DiskPortrayalInstructionCache</c>: any IO error,
/// truncated/corrupt file, or codec failure is treated as a miss; failures never
/// propagate. Writes are atomic (temp file + move). The total on-disk size is
/// bounded by <see cref="MaxBytes"/> with least-recently-used eviction from an
/// in-memory index (<see cref="DiskCacheBudget"/>), so a write costs the same
/// however many tiles the cache holds. Directories left by an older
/// <see cref="FormatVersion"/> are deleted once, in the background, at startup.
/// Reads are concurrent; a bounded write-behind queue deduplicates persistence
/// requests and one low-priority writer owns encoding, final-path mutation, and
/// eviction. Deferred requests may carry a relevance predicate so obsolete
/// viewport work is discarded before snapshot, PNG encoding, and file commit.
/// </para>
/// </remarks>
internal sealed class TileDiskCache : IDisposable
{
    /// <summary>
    /// On-disk layout version. Bump whenever the tile-image encoding or the
    /// namespace/filename scheme changes, so a stale layout is ignored (a miss)
    /// rather than mis-decoded.
    /// </summary>
    /// <remarks>
    /// v2: point symbols and point-anchored text (soundings) moved out of the
    /// tiled base plane into a live screen-space overlay, so base tiles no
    /// longer contain symbol/text pixels. Reusing a v1 tile (symbols baked in)
    /// alongside the new overlay would double-draw every symbol.
    /// </remarks>
    public const int FormatVersion = 2;

    private const string FileExtension = ".png";

    private readonly string _rootDirectory;
    private readonly DiskCacheBudget _budget;

    private const int DefaultWriteQueueCapacity = 64;

    private readonly BlockingCollection<WriteRequest> _writeQueue;
    private readonly HashSet<WriteKey> _pendingWrites = [];
    private readonly object _pendingWritesGate = new();
    private readonly ManualResetEventSlim _writeQueueIdle = new(initialState: true);
    private readonly Action? _beforeWrite;
    private readonly Thread _writeThread;
    private readonly string _parentDirectory;
    private bool _disposed;

    internal enum WriteEnqueueResult
    {
        Queued,
        Duplicate,
        Full,
        Stopped,
        SnapshotFailed,
    }

    /// <summary>Soft upper bound, in bytes, on the total size of all tile files.</summary>
    public long MaxBytes => _budget.MaxBytes;

    /// <summary>The root directory under which per-namespace tile subdirectories live.</summary>
    public string RootDirectory => _rootDirectory;

    /// <summary>
    /// Creates a disk tile cache rooted at <paramref name="rootDirectory"/> with
    /// the given soft byte budget. The directory is created on first write.
    /// </summary>
    /// <param name="rootDirectory">
    /// Private cache root: every tile under it counts toward the budget, and
    /// <c>v&lt;n&gt;</c> subdirectories of other format versions are deleted.
    /// </param>
    /// <param name="maxBytes">Soft total-size cap; must be positive.</param>
    /// <param name="writeQueueCapacity">Maximum accepted persistence requests awaiting the writer.</param>
    /// <param name="beforeWrite">Optional callback invoked by the writer before snapshot and persistence.</param>
    /// <exception cref="ArgumentException"><paramref name="rootDirectory"/> is null or empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="maxBytes"/> or <paramref name="writeQueueCapacity"/> is not positive.
    /// </exception>
    public TileDiskCache(
        string rootDirectory,
        long maxBytes,
        int writeQueueCapacity = DefaultWriteQueueCapacity,
        Action? beforeWrite = null)
    {
        if (string.IsNullOrEmpty(rootDirectory))
        {
            throw new ArgumentException("Cache root directory must be provided.", nameof(rootDirectory));
        }

        if (maxBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes), maxBytes, "Budget must be positive.");
        }
        if (writeQueueCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(writeQueueCapacity),
                writeQueueCapacity,
                "Write queue capacity must be positive.");
        }

        _parentDirectory = rootDirectory;
        _rootDirectory = Path.Combine(rootDirectory, VersionDirectoryName(FormatVersion));
        _budget = new DiskCacheBudget(_rootDirectory, FileExtension, maxBytes, recursive: true);
        _writeQueue = new BlockingCollection<WriteRequest>(
            new ConcurrentQueue<WriteRequest>(),
            writeQueueCapacity);
        _beforeWrite = beforeWrite;
        _writeThread = new Thread(ProcessWriteQueue)
        {
            IsBackground = true,
            Name = "S100 tile cache writer",
            Priority = ThreadPriority.BelowNormal,
        };
        _writeThread.Start();
    }

    /// <summary>
    /// Computes the cache namespace (a safe, fixed-length subdirectory name) for
    /// a product/layer-set identity and its style-state hash. Folds both into a
    /// single SHA-256 so two different style states never collide on one
    /// namespace.
    /// </summary>
    public static string NamespaceFor(string productLayerSet, string styleStateHash)
    {
        ArgumentNullException.ThrowIfNull(productLayerSet);
        ArgumentNullException.ThrowIfNull(styleStateHash);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(productLayerSet + "|" + styleStateHash));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Reads and decodes the warm tile for <paramref name="key"/> in
    /// <paramref name="ns"/>, or <see langword="null"/> on any miss (absent,
    /// unreadable, or undecodable). A hit marks the tile most recently used.
    /// </summary>
    public SKImage? TryRead(string ns, TileKey key)
    {
        if (string.IsNullOrEmpty(ns))
        {
            return null;
        }

        var bytes = _budget.TryRead(EntryPath(ns, key));
        if (bytes is null)
        {
            return null;
        }

        try
        {
            using var data = SKData.CreateCopy(bytes);
            return SKImage.FromEncodedData(data);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Encodes and atomically writes <paramref name="image"/> as the warm tile
    /// for <paramref name="key"/> in <paramref name="ns"/>, evicting
    /// least-recently-used tiles if the byte budget is exceeded. Best-effort:
    /// all failures are swallowed.
    /// </summary>
    public void Write(string ns, TileKey key, SKImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (string.IsNullOrEmpty(ns))
        {
            return;
        }

        WriteCore(ns, key, image);
    }

    internal WriteEnqueueResult TryQueueWrite(string ns, TileKey key, SKImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var snapshot = CreateSnapshot(image);
        if (snapshot is null)
        {
            S100Diag.TileDiskWriteQueueDiscarded.Add(
                1,
                new KeyValuePair<string, object?>("reason", "snapshot"));
            return WriteEnqueueResult.SnapshotFailed;
        }

        return TryQueueSnapshot(ns, key, snapshot);
    }

    internal WriteEnqueueResult TryQueueSnapshot(
        string ns,
        TileKey key,
        SKImage snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return TryQueueRequest(
            ns,
            key,
            new WriteRequest(
                default,
                snapshot,
                SnapshotFactory: null,
                IsRelevant: null));
    }

    internal WriteEnqueueResult TryQueueDeferredSnapshot(
        string ns,
        TileKey key,
        Func<SKImage?> snapshotFactory,
        Func<bool>? isRelevant = null)
    {
        ArgumentNullException.ThrowIfNull(snapshotFactory);
        return TryQueueRequest(
            ns,
            key,
            new WriteRequest(default, Image: null, snapshotFactory, isRelevant));
    }

    private WriteEnqueueResult TryQueueRequest(
        string ns,
        TileKey key,
        WriteRequest request)
    {
        if (string.IsNullOrEmpty(ns))
        {
            request.Image?.Dispose();
            return WriteEnqueueResult.Stopped;
        }

        var writeKey = new WriteKey(ns, key);
        lock (_pendingWritesGate)
        {
            if (_disposed)
            {
                request.Image?.Dispose();
                return WriteEnqueueResult.Stopped;
            }
            if (!_pendingWrites.Add(writeKey))
            {
                request.Image?.Dispose();
                S100Diag.TileDiskWriteQueueDiscarded.Add(
                    1,
                    new KeyValuePair<string, object?>("reason", "duplicate"));
                return WriteEnqueueResult.Duplicate;
            }

            _writeQueueIdle.Reset();
            request = request with { Key = writeKey };
            if (_writeQueue.TryAdd(request))
            {
                S100Diag.TileDiskWriteQueueDepth.Record(_writeQueue.Count);
                return WriteEnqueueResult.Queued;
            }

            _pendingWrites.Remove(writeKey);
            if (_pendingWrites.Count == 0)
            {
                _writeQueueIdle.Set();
            }
        }

        request.Image?.Dispose();
        S100Diag.TileDiskWriteQueueDiscarded.Add(
            1,
            new KeyValuePair<string, object?>("reason", "full"));
        return WriteEnqueueResult.Full;
    }

    internal static SKImage? CreateSnapshot(SKImage image)
    {
        using var pixels = CopyPixels(image);
        return pixels is null ? null : CreateSnapshot(pixels);
    }

    internal static SKBitmap? CopyPixels(SKImage image)
    {
        var info = new SKImageInfo(
            image.Width,
            image.Height,
            SKColorType.Rgba8888,
            SKAlphaType.Premul);
        var bitmap = new SKBitmap(info);
        if (!image.ReadPixels(
                bitmap.Info,
                bitmap.GetPixels(),
                bitmap.RowBytes,
                0,
                0))
        {
            bitmap.Dispose();
            return null;
        }

        return bitmap;
    }

    internal static SKImage CreateSnapshot(SKBitmap bitmap)
    {
        return SKImage.FromPixelCopy(
            bitmap.Info,
            bitmap.GetPixels(),
            bitmap.RowBytes);
    }

    internal bool WaitForWriteQueueIdle(TimeSpan timeout) =>
        _writeQueueIdle.Wait(timeout);

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_pendingWritesGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _writeQueue.CompleteAdding();
        }

        _writeThread.Join();
        _writeQueue.Dispose();
        _writeQueueIdle.Dispose();
    }

    private bool WriteCore(
        string ns,
        TileKey key,
        SKImage image,
        Func<bool>? isRelevant = null)
    {
        if (!IsWriteRelevant(isRelevant))
        {
            RecordStaleWriteDiscard();
            return false;
        }

        using var persistActivity = S100Diag.ActivitySource.StartActivity(
            "s100.render.tile.cache.persist", ActivityKind.Internal);
        persistActivity?.SetTag("s100.render.tile.key", $"{key.Band}/{key.X}/{key.Y}");

        byte[] encoded;
        using (S100Diag.ActivitySource.StartActivity(
                   "s100.render.tile.cache.encode", ActivityKind.Internal))
        {
            try
            {
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                if (data is null)
                {
                    return false;
                }

                encoded = data.ToArray();
            }
            catch
            {
                return false;
            }
        }

        if (!IsWriteRelevant(isRelevant))
        {
            RecordStaleWriteDiscard();
            return false;
        }

        bool committed;
        using (S100Diag.ActivitySource.StartActivity(
                   "s100.render.tile.cache.file_write", ActivityKind.Internal))
        {
            committed = _budget.TryWrite(
                EntryPath(ns, key),
                encoded,
                () => IsWriteRelevant(isRelevant));
        }

        if (!committed)
        {
            if (!IsWriteRelevant(isRelevant))
            {
                RecordStaleWriteDiscard();
            }

            return false;
        }

        persistActivity?.SetTag("s100.render.tile.cache.encoded_bytes", encoded.Length);
        return true;
    }

    private void ProcessWriteQueue()
    {
        DeleteStaleFormatDirectories();

        foreach (var request in _writeQueue.GetConsumingEnumerable())
        {
            SKImage? image = null;
            try
            {
                _beforeWrite?.Invoke();
                if (!IsWriteRelevant(request.IsRelevant))
                {
                    RecordStaleWriteDiscard();
                }
                else
                {
                    image = request.Image ?? request.SnapshotFactory?.Invoke();
                    if (image is null)
                    {
                        S100Diag.TileDiskWriteQueueDiscarded.Add(
                            1,
                            new KeyValuePair<string, object?>("reason", "snapshot"));
                    }
                    else if (WriteCore(
                                 request.Key.Namespace,
                                 request.Key.Tile,
                                 image,
                                 request.IsRelevant))
                    {
                        S100Diag.TileDiskWrites.Add(1);
                    }
                }
            }
            catch (Exception)
            {
                S100Diag.TileDiskWriteQueueDiscarded.Add(
                    1,
                    new KeyValuePair<string, object?>("reason", "error"));
            }
            finally
            {
                image?.Dispose();
                RemovePending(request.Key);
                S100Diag.TileDiskWriteQueueDepth.Record(_writeQueue.Count);
            }
        }
    }

    /// <summary>
    /// Deletes <c>v&lt;n&gt;</c> directories under the cache root left by other
    /// <see cref="FormatVersion"/>s. Their tiles can never be served, and the
    /// budget covers only the current version's directory, so without this they
    /// would stay on disk forever. Best-effort.
    /// </summary>
    private void DeleteStaleFormatDirectories()
    {
        var current = VersionDirectoryName(FormatVersion);
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(_parentDirectory, "v*"))
            {
                var name = Path.GetFileName(dir);
                if (name.Length > 1
                    && name.AsSpan(1).IndexOfAnyExceptInRange('0', '9') < 0
                    && !string.Equals(name, current, StringComparison.Ordinal))
                {
                    try
                    {
                        Directory.Delete(dir, recursive: true);
                    }
                    catch
                    {
                        // Retried on the next start.
                    }
                }
            }
        }
        catch
        {
            // The root may not exist yet; nothing to reclaim.
        }
    }

    private static string VersionDirectoryName(int version) =>
        "v" + version.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static bool IsWriteRelevant(Func<bool>? isRelevant) =>
        isRelevant?.Invoke() ?? true;

    private static void RecordStaleWriteDiscard() =>
        S100Diag.TileDiskWriteQueueDiscarded.Add(
            1,
            new KeyValuePair<string, object?>("reason", "stale"));

    private void RemovePending(WriteKey key)
    {
        lock (_pendingWritesGate)
        {
            _pendingWrites.Remove(key);
            if (_pendingWrites.Count == 0)
            {
                _writeQueueIdle.Set();
            }
        }
    }

    /// <summary>Maps a tile key to its file name within a namespace directory.</summary>
    private static string FileName(TileKey key) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"{key.Band}_{key.X}_{key.Y}{FileExtension}");

    private string EntryPath(string ns, TileKey key) =>
        Path.Combine(_rootDirectory, ns, FileName(key));

    private readonly record struct WriteKey(string Namespace, TileKey Tile);

    private sealed record WriteRequest(
        WriteKey Key,
        SKImage? Image,
        Func<SKImage?>? SnapshotFactory,
        Func<bool>? IsRelevant);
}
