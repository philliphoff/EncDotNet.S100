using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
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
/// bounded by <see cref="MaxBytes"/> with least-recently-accessed eviction.
/// Reads are concurrent; a bounded write-behind queue deduplicates persistence
/// requests and one low-priority writer owns encoding, final-path mutation, and
/// budget sweeps. Deferred requests may carry a relevance predicate so obsolete
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
    private readonly object _gate = new();

    // EnforceSizeCap enumerates the whole tree, so it is throttled to run only
    // every Nth write rather than on every tile (writes happen in bursts during
    // a pan). The budget is a soft cap, so a brief overshoot between sweeps is
    // acceptable.
    private const int CapSweepInterval = 32;
    private const int DefaultWriteQueueCapacity = 64;

    private readonly BlockingCollection<WriteRequest> _writeQueue;
    private readonly HashSet<WriteKey> _pendingWrites = [];
    private readonly object _pendingWritesGate = new();
    private readonly ManualResetEventSlim _writeQueueIdle = new(initialState: true);
    private readonly Action? _beforeWrite;
    private readonly Thread _writeThread;
    private int _writesSinceSweep;
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
    public long MaxBytes { get; }

    /// <summary>The root directory under which per-namespace tile subdirectories live.</summary>
    public string RootDirectory => _rootDirectory;

    /// <summary>
    /// Creates a disk tile cache rooted at <paramref name="rootDirectory"/> with
    /// the given soft byte budget. The directory is created on first write.
    /// </summary>
    /// <param name="rootDirectory">Private cache root (the LRU sweep enumerates every tile under it).</param>
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

        _rootDirectory = Path.Combine(rootDirectory, "v" + FormatVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        MaxBytes = maxBytes;
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
    /// unreadable, or undecodable). On a hit the file's access time is stamped so
    /// the LRU sweep treats it as most-recently-used.
    /// </summary>
    public SKImage? TryRead(string ns, TileKey key)
    {
        if (string.IsNullOrEmpty(ns))
        {
            return null;
        }

        var path = EntryPath(ns, key);
        byte[] bytes;
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            bytes = File.ReadAllBytes(path);
            TouchAccessTime(path);
        }
        catch
        {
            return null;
        }

        // Decode outside the lock so workers do not serialise on the codec.
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
    /// for <paramref name="key"/> in <paramref name="ns"/>, then enforces the
    /// byte budget. Best-effort: all failures are swallowed.
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

        // Encode outside the lock (the expensive part) so the persistence
        // worker does not block cache reads while running the PNG codec.
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

        var dir = Path.Combine(_rootDirectory, ns);
        var path = Path.Combine(dir, FileName(key));
        var temp = Path.Combine(dir, Path.GetRandomFileName() + ".tmp");
        bool sweep;
        var lockStart = Stopwatch.GetTimestamp();
        using (S100Diag.ActivitySource.StartActivity(
                   "s100.render.tile.cache.file_write", ActivityKind.Internal))
        {
            lock (_gate)
            {
                persistActivity?.SetTag(
                    "s100.render.tile.cache.lock_wait_ms",
                    Stopwatch.GetElapsedTime(lockStart).TotalMilliseconds);
                try
                {
                    Directory.CreateDirectory(dir);
                    File.WriteAllBytes(temp, encoded);
                    if (!IsWriteRelevant(isRelevant))
                    {
                        TryDelete(temp);
                        RecordStaleWriteDiscard();
                        return false;
                    }

                    File.Move(temp, path, overwrite: true);
                    TouchAccessTime(path);
                }
                catch
                {
                    TryDelete(temp);
                    return false;
                }

                sweep = ++_writesSinceSweep >= CapSweepInterval;
                if (sweep)
                {
                    _writesSinceSweep = 0;
                }
            }
        }

        if (sweep)
        {
            using (S100Diag.ActivitySource.StartActivity(
                       "s100.render.tile.cache.sweep", ActivityKind.Internal))
            {
                lock (_gate)
                {
                    EnforceSizeCap();
                }
            }
        }

        persistActivity?.SetTag("s100.render.tile.cache.encoded_bytes", encoded.Length);
        return true;
    }

    private void ProcessWriteQueue()
    {
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

    private static void TouchAccessTime(string path)
    {
        try
        {
            File.SetLastAccessTimeUtc(path, DateTime.UtcNow);
        }
        catch
        {
            // Non-fatal: a missed touch only affects eviction ordering.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Non-fatal.
        }
    }

    /// <summary>
    /// Evicts least-recently-accessed tile files across every namespace until the
    /// total size is at or below <see cref="MaxBytes"/>, and sweeps orphaned
    /// <c>*.tmp</c> files. Called under the lock.
    /// </summary>
    private void EnforceSizeCap()
    {
        FileInfo[] files;
        try
        {
            var dir = new DirectoryInfo(_rootDirectory);
            if (!dir.Exists)
            {
                return;
            }

            foreach (var tmp in dir.GetFiles("*.tmp", SearchOption.AllDirectories))
            {
                TryDelete(tmp.FullName);
            }

            files = dir.GetFiles("*" + FileExtension, SearchOption.AllDirectories);
        }
        catch
        {
            return;
        }

        long total = 0;
        foreach (var f in files)
        {
            total += f.Length;
        }

        if (total <= MaxBytes)
        {
            return;
        }

        Array.Sort(files, static (a, b) => a.LastAccessTimeUtc.CompareTo(b.LastAccessTimeUtc));

        foreach (var f in files)
        {
            if (total <= MaxBytes)
            {
                break;
            }

            var len = f.Length;
            try
            {
                f.Delete();
                total -= len;
            }
            catch
            {
                // Skip a file we cannot delete; the next sweep retries.
            }
        }
    }

    private readonly record struct WriteKey(string Namespace, TileKey Tile);

    private sealed record WriteRequest(
        WriteKey Key,
        SKImage? Image,
        Func<SKImage?>? SnapshotFactory,
        Func<bool>? IsRelevant);
}
