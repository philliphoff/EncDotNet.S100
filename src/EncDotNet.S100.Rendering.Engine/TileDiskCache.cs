using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using EncDotNet.S100.Core.Caching;
using SkiaSharp;
using S100Diag = EncDotNet.S100.Rendering.Engine.Diagnostics.Telemetry;

namespace EncDotNet.S100.Rendering.Engine;

/// <summary>
/// A persistent, on-disk <b>warm</b> cache of rasterised base-plane tiles
/// (S-100 render subsystem, Phase&#160;4, design §3.4), keyed by
/// <c>(namespace, band, x, y, scale class, slice)</c>, where the
/// <i>namespace</i> folds the product/layer-set identity and a
/// <c>styleStateHash</c> so a tile rendered under one mariner/palette state can
/// <b>never</b> be served for a different one. It survives layer rebuilds (a
/// palette flip-back re-uses the warm tiles instead of re-rasterising) and
/// process restarts.
/// </summary>
/// <remarks>
/// <para>
/// The in-memory <see cref="TileCache"/> is the hot tier (native-byte LRU, fresh
/// per layer); this is the warm tier shared across every layer and session. A
/// tile missing from the hot cache is looked up here by its raster job before
/// a re-rasterise; a freshly rasterised tile is written here for future reuse.
/// </para>
/// <para>
/// <b>Correctness.</b> The cache is correct only because the namespace fully
/// captures the dataset content and style state: the caller passes
/// <c>(productLayerSet, styleStateHash)</c> to <see cref="NamespaceFor(string, string)"/>.
/// The renderer builds <c>productLayerSet</c> from the product and the
/// dataset's content scope (a hash of its bytes, since the instructions do not
/// carry geometry), and <c>styleStateHash</c> from the resolved drawing
/// instructions plus the palette and symbol/text scales (see
/// <c>MapsuiDisplayListRenderer</c>). A change to any of those yields a
/// different namespace — old tiles are simply orphaned and
/// reclaimed by the store's byte budget, never served stale. The renderer
/// further splits that namespace by the tile's raster pixel size
/// (<see cref="NamespaceFor(string, int)"/>), so a tile rasterised at one device
/// scale is never served to a frame at another. Chart tiles carry their
/// <c>StackSlice</c> in the file name, so they are content-addressed.
/// </para>
/// <para>
/// <b>Storage.</b> Tiles live in the <see cref="KindName"/> kind of a
/// <see cref="ContentStore"/>, in <see cref="TileCodec"/> format (Zstandard over
/// premultiplied pixels, not PNG). A fully transparent tile is an empty
/// marker that reads back without a decode. The kind is versioned by
/// <see cref="FormatVersion"/>, so a format change ignores every older tile at
/// once and the store deletes the old folder in the background.
/// </para>
/// <para>
/// <b>IO.</b> Writes are queued on the store: a background job takes the
/// pixel snapshot, encodes and commits it, and drops the work when the tile is
/// no longer relevant (before the snapshot, before the encode, and before the
/// file is moved into place). <see cref="TryReadAsync"/> awaits the file read,
/// so a raster job gives its worker back while it waits. Any IO error or
/// corrupt file is a miss; failures never propagate.
/// </para>
/// </remarks>
internal sealed class TileDiskCache
{
    /// <summary>
    /// On-disk layout version: the store folder <c>tiles/v&lt;n&gt;</c>. Bump
    /// whenever the tile encoding or the namespace/filename scheme changes, so
    /// a stale layout is ignored (a miss) rather than mis-decoded, and its
    /// folder is deleted.
    /// </summary>
    /// <remarks>
    /// v2: point symbols and point-anchored text (soundings) moved out of the
    /// tiled base plane into a live screen-space overlay.
    /// v3: the raster pixel size is part of the namespace.
    /// v4: a tile is rasterised no coarser than its cell's out-of-band cap (#761).
    /// v5: seam-crossing and polar surfaces changed (#760, #773).
    /// v6: a tile is keyed by its SCAMIN scale class (#774).
    /// v7: a tile stores only its core plus a 2-pixel apron (map rendering slice 4).
    /// v8: tiles are <see cref="TileCodec"/> files (Zstandard over premultiplied
    /// pixels) with empty markers, not PNG (map rendering slice 11).
    /// </remarks>
    public const int FormatVersion = 8;

    /// <summary>The store kind name, which is also its folder name.</summary>
    public const string KindName = "tiles";

    /// <summary>The options the tile kind is opened with.</summary>
    public static ContentKindOptions KindOptions { get; } = new()
    {
        Extension = ".tile",
        FormatVersion = FormatVersion,
        NestedKeys = true,
    };

    private readonly ContentKind _kind;

    internal enum WriteEnqueueResult
    {
        Queued,
        Duplicate,
        Full,
        Stopped,
        SnapshotFailed,
    }

    /// <summary>Creates a warm tile cache over <paramref name="kind"/>, normally opened as <see cref="KindName"/> with <see cref="KindOptions"/>.</summary>
    /// <param name="kind">The store kind tiles live in.</param>
    /// <exception cref="ArgumentNullException"><paramref name="kind"/> is null.</exception>
    public TileDiskCache(ContentKind kind)
    {
        ArgumentNullException.ThrowIfNull(kind);
        _kind = kind;
    }

    /// <summary>The store kind tiles live in.</summary>
    public ContentKind Kind => _kind;

    /// <summary>The tile kind's share of the store's byte budget.</summary>
    public long MaxBytes => _kind.ShareBytes;

    /// <summary>The folder (including the version folder) per-namespace tile folders live in.</summary>
    public string RootDirectory => _kind.Directory;

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
    /// Derives the per-raster-scale namespace for tiles of
    /// <paramref name="tilePixelSize"/> device pixels per edge from a
    /// <see cref="NamespaceFor(string, string)"/> namespace. A tile rasterised at
    /// one device scale is never served to a frame at another (a 384&#160;px 1x
    /// tile blitted into a 2x window is visibly soft), so each scale gets its own
    /// subdirectory; the byte budget spans them all.
    /// </summary>
    /// <param name="baseNamespace">The style-state namespace.</param>
    /// <param name="tilePixelSize">The tile's edge length in device pixels.</param>
    /// <returns>A safe subdirectory name unique to the namespace and pixel size.</returns>
    public static string NamespaceFor(string baseNamespace, int tilePixelSize)
    {
        ArgumentException.ThrowIfNullOrEmpty(baseNamespace);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tilePixelSize);

        return string.Create(CultureInfo.InvariantCulture, $"{baseNamespace}-{tilePixelSize}px");
    }

    /// <summary>
    /// Reads the warm tile for <paramref name="key"/> in <paramref name="ns"/>,
    /// or <see langword="null"/> on any miss (absent, unreadable, or
    /// undecodable). An empty marker reads back as a transparent image that
    /// shares one pixel buffer (no decode). A hit marks the tile most recently
    /// used. The file read is awaited, so a scheduler job gives its worker back
    /// while it waits.
    /// </summary>
    /// <param name="ns">The tile namespace.</param>
    /// <param name="key">The tile key.</param>
    /// <param name="tilePixelSize">The expected edge length; a tile of any other size is a miss.</param>
    /// <param name="cancellationToken">Cancels the read; a cancelled read is a miss.</param>
    public async ValueTask<SKImage?> TryReadAsync(
        string ns,
        TileKey key,
        int tilePixelSize,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(ns))
        {
            return null;
        }

        var entry = await _kind.ReadAsync(EntryKey(ns, key), cancellationToken).ConfigureAwait(false);
        return ToImage(entry, tilePixelSize);
    }

    /// <summary>
    /// Reads the warm tile for <paramref name="key"/> in <paramref name="ns"/>
    /// on the caller's thread, as <see cref="TryReadAsync"/> does.
    /// </summary>
    /// <param name="ns">The tile namespace.</param>
    /// <param name="key">The tile key.</param>
    /// <param name="tilePixelSize">The expected edge length; a tile of any other size is a miss.</param>
    public SKImage? TryRead(string ns, TileKey key, int tilePixelSize)
    {
        if (string.IsNullOrEmpty(ns))
        {
            return null;
        }

        return ToImage(_kind.Read(EntryKey(ns, key)), tilePixelSize);
    }

    /// <summary>
    /// Encodes and writes <paramref name="image"/> as the warm tile for
    /// <paramref name="key"/> in <paramref name="ns"/> on the caller's thread.
    /// Best-effort: all failures are swallowed.
    /// </summary>
    public void Write(string ns, TileKey key, SKImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (string.IsNullOrEmpty(ns))
        {
            return;
        }

        if (Encode(key, image) is { } encoded)
        {
            _kind.Write(EntryKey(ns, key), encoded);
        }
    }

    internal WriteEnqueueResult TryQueueWrite(string ns, TileKey key, SKImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var snapshot = CreateSnapshot(image);
        if (snapshot is null)
        {
            RecordDiscard("snapshot");
            return WriteEnqueueResult.SnapshotFailed;
        }

        return TryQueueSnapshot(ns, key, snapshot);
    }

    internal WriteEnqueueResult TryQueueSnapshot(string ns, TileKey key, SKImage snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var owned = snapshot;
        var result = TryQueueDeferredSnapshot(
            ns,
            key,
            () => Interlocked.Exchange(ref owned, null));
        if (result != WriteEnqueueResult.Queued)
        {
            snapshot.Dispose();
        }

        return result;
    }

    internal WriteEnqueueResult TryQueueDeferredSnapshot(
        string ns,
        TileKey key,
        Func<SKImage?> snapshotFactory,
        Func<bool>? isRelevant = null)
    {
        ArgumentNullException.ThrowIfNull(snapshotFactory);
        if (string.IsNullOrEmpty(ns))
        {
            return WriteEnqueueResult.Stopped;
        }

        var result = _kind.QueueWrite(
            EntryKey(ns, key),
            () =>
            {
                using var image = snapshotFactory();
                if (image is null)
                {
                    RecordDiscard("snapshot");
                    return null;
                }

                return Encode(key, image);
            },
            isRelevant,
            outcome =>
            {
                switch (outcome)
                {
                    case ContentWriteOutcome.Written:
                        S100Diag.TileDiskWrites.Add(1);
                        break;
                    case ContentWriteOutcome.Stale:
                        RecordDiscard("stale");
                        break;
                    case ContentWriteOutcome.Failed:
                        RecordDiscard("error");
                        break;
                }

                S100Diag.TileDiskWriteQueueDepth.Record(_kind.QueuedWrites);
            });

        switch (result)
        {
            case ContentWriteResult.Queued:
                S100Diag.TileDiskWriteQueueDepth.Record(_kind.QueuedWrites);
                return WriteEnqueueResult.Queued;
            case ContentWriteResult.Duplicate:
                RecordDiscard("duplicate");
                return WriteEnqueueResult.Duplicate;
            case ContentWriteResult.Full:
                RecordDiscard("full");
                return WriteEnqueueResult.Full;
            default:
                return WriteEnqueueResult.Stopped;
        }
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

    /// <summary>
    /// An image over <paramref name="bitmap"/>'s pixels without copying them:
    /// the bitmap is made immutable and the image shares its pixel memory, so
    /// the caller may dispose the bitmap afterwards.
    /// </summary>
    internal static SKImage CreateSnapshot(SKBitmap bitmap)
    {
        bitmap.SetImmutable();
        return SKImage.FromBitmap(bitmap);
    }

    internal bool WaitForWriteQueueIdle(TimeSpan timeout) => _kind.WaitForWritesIdle(timeout);

    private static byte[]? Encode(TileKey key, SKImage image)
    {
        using var activity = S100Diag.ActivitySource.StartActivity(
            "s100.render.tile.cache.encode", ActivityKind.Internal);
        activity?.SetTag("s100.render.tile.key", $"{key.Band}/{key.X}/{key.Y}/{key.ScaleClass}");
        try
        {
            var encoded = TileCodec.Encode(image);
            activity?.SetTag("s100.render.tile.cache.encoded_bytes", encoded?.Length);
            return encoded;
        }
        catch
        {
            return null;
        }
    }

    private static SKImage? ToImage(ContentEntry entry, int tilePixelSize)
    {
        switch (entry.State)
        {
            case ContentState.Empty:
                return TileCodec.EmptyImage(tilePixelSize, tilePixelSize);
            case ContentState.Present:
                var image = TileCodec.Decode(entry.Bytes);
                if (image is not null && (image.Width != tilePixelSize || image.Height != tilePixelSize))
                {
                    // A tile of another size (a corrupt or foreign file) is a
                    // miss, never blitted soft.
                    image.Dispose();
                    return null;
                }

                return image;
            default:
                return null;
        }
    }

    private static void RecordDiscard(string reason) =>
        S100Diag.TileDiskWriteQueueDiscarded.Add(1, new KeyValuePair<string, object?>("reason", reason));

    /// <summary>Maps a tile key to its store key: the namespace folder, then the file name.</summary>
    private static string EntryKey(string ns, TileKey key) =>
        ns + "/" + (key.Slice != UInt128.Zero
            ? string.Create(CultureInfo.InvariantCulture,
                $"{key.Band}_{key.X}_{key.Y}_s{key.ScaleClass}_{key.Slice:x32}")
            : key.ScaleClass == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{key.Band}_{key.X}_{key.Y}")
            : string.Create(CultureInfo.InvariantCulture, $"{key.Band}_{key.X}_{key.Y}_s{key.ScaleClass}"));
}
