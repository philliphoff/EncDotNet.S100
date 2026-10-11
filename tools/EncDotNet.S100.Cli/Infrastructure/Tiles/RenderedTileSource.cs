using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Rendering.Scene;

namespace EncDotNet.S100.Cli.Infrastructure.Tiles;

/// <summary>
/// Renders tiles from datasets when they are first asked for (issue #865), in
/// any of the day, dusk and night palettes, and keeps them in a memory cache.
/// </summary>
/// <remarks>
/// <para>
/// A tile that isn't cached is rendered with the rest of its block — the
/// <c>--metatile</c> × <c>--metatile</c> tiles around it, aligned as
/// <c>tiles export</c> aligns them and clipped to the tiled area — and every
/// tile of the block is cached. A web map asks for neighbouring tiles together,
/// so they are mostly served from that one render, and labels stay consistent
/// across their edges. Requests for a block already being rendered wait for
/// that render rather than starting another.
/// </para>
/// <para>
/// With <c>--cache-dir</c>, tiles are also kept on disk across runs
/// (<see cref="DiskTileCache"/>) and read from there before rendering.
/// At most <c>--parallel</c> blocks render at once. Each palette, and each
/// time step asked for with a <c>t</c> query parameter (snapped to the nearest
/// of <see cref="Times"/>), is portrayed the first time it is asked for
/// (<see cref="SceneCache"/>). Tiles outside the tiled area or its zoom
/// range, and tiles on which nothing was drawn, read as <see langword="null"/>.
/// </para>
/// </remarks>
internal sealed class RenderedTileSource : ITileSource, IDisposable
{
    /// <summary>The palettes tiles can be rendered in.</summary>
    public static readonly IReadOnlyList<string> AllPalettes = ["day", "dusk", "night"];

    private readonly TileRenderSession _session;
    private readonly TileLayout _layout;
    private readonly string _defaultPalette;
    private readonly int _blockSize;
    private readonly SemaphoreSlim _renderSlots;
    private readonly TileCache _cache;
    private readonly DiskTileCache? _diskCache;
    private readonly SceneCache _scenes;
    private readonly ConcurrentDictionary<(string Palette, DateTime? Time, TileBlock Block), Lazy<Task<IReadOnlyDictionary<(int X, int Y), byte[]>>>> _rendering = new();
    private long _blocksRendered;

    /// <param name="session">The opened datasets. The source disposes it.</param>
    /// <param name="scene">The datasets prepared in <paramref name="defaultPalette"/>.</param>
    /// <param name="layout">The area and zoom range to serve.</param>
    /// <param name="defaultPalette">The palette of the unprefixed tile URLs.</param>
    /// <param name="blockSize">The edge length of a rendered block, in tiles.</param>
    /// <param name="parallel">The most blocks rendered at once.</param>
    /// <param name="cacheBytes">The most encoded tile bytes kept in memory.</param>
    /// <param name="diskCache">Tiles kept on disk across runs, behind the memory cache; <see langword="null"/> for none.</param>
    public RenderedTileSource(
        TileRenderSession session,
        TileScene scene,
        TileLayout layout,
        string defaultPalette,
        int blockSize,
        int parallel,
        long cacheBytes,
        DiskTileCache? diskCache = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(parallel);

        _session = session;
        _layout = layout;
        _defaultPalette = defaultPalette.Trim().ToLowerInvariant();
        _blockSize = blockSize;
        _renderSlots = new SemaphoreSlim(parallel, parallel);
        _cache = new TileCache(cacheBytes);
        _diskCache = diskCache;
        _scenes = new SceneCache(session);
        _scenes.Seed(_defaultPalette, null, scene);
    }

    public string Path => $"{_session.Specs.Count} dataset(s) ({string.Join(", ", _session.Specs.Distinct())})";

    public TileImageFormat Format => _session.Format;

    public IReadOnlyList<string> Palettes => AllPalettes;

    public IReadOnlyList<DateTime> Times => _session.Times;

    /// <summary>The area and zoom range served.</summary>
    public TileLayout Layout => _layout;

    /// <summary>The number of prepared scenes (palette and time step) held.</summary>
    internal int ScenesHeld => _scenes.Count;

    /// <summary>The number of blocks rendered so far.</summary>
    public long BlocksRendered => Interlocked.Read(ref _blocksRendered);

    public async ValueTask<byte[]?> ReadAsync(int zoom, int x, int y, string? palette, DateTime? time, CancellationToken cancellationToken)
    {
        palette = palette?.Trim().ToLowerInvariant() ?? _defaultPalette;
        time = time is { } requested ? _session.SnapTime(requested) : null;
        if (!AllPalettes.Contains(palette)
            || zoom < _layout.MinZoom || zoom > _layout.MaxZoom
            || !TileSource.IsValid(zoom, x, y)
            || BlockFor(_layout.Area, _blockSize, zoom, x, y) is not { } block)
        {
            return null;
        }

        var key = new TileKey(palette, zoom, x, y, time);
        if (_cache.TryGet(key, out var data))
            return data.Length == 0 ? null : data;

        if (_diskCache?.TryGet(key, out data) == true)
        {
            _cache.Add(key, data);
        }
        else
        {
            var render = _rendering.GetOrAdd(
                (palette, time, block),
                k => new Lazy<Task<IReadOnlyDictionary<(int X, int Y), byte[]>>>(() => RenderBlockAsync(k.Palette, k.Time, k.Block)));
            var tiles = await render.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
            data = tiles[(x, y)];
        }

        return data.Length == 0 ? null : data;
    }

    public JsonObject ToTileJson(string? palette, DateTime? time = null)
    {
        palette = palette?.Trim().ToLowerInvariant() ?? _defaultPalette;
        time = time is { } requested ? _session.SnapTime(requested) : null;
        var json = new TileSetMetadata
        {
            Name = "s100 tiles",
            Description = $"S-100 portrayal of {string.Join(", ", _session.Specs.Distinct())} rendered on demand by s100 tiles serve.",
            Format = Format,
            MinZoom = _layout.MinZoom,
            MaxZoom = _layout.MaxZoom,
            Bounds = _layout.Bounds,
            TilePixelSize = _layout.TileOptions.PixelRatio * XyzTileGrid.TileSize,
            Settings = _session.DescribeSettings(_layout.ReferenceLatitude, palette, time),
        }.ToTileJson(tilesUrl: null);
        json["s100"]!["palettes"] = new JsonArray(AllPalettes.Select(p => (JsonNode?)p).ToArray());
        if (Times.Count > 0)
            json["s100"]!["times"] = new JsonArray(Times.Select(t => (JsonNode?)TileRenderSession.FormatTime(t)).ToArray());
        return json;
    }

    /// <summary>
    /// Completes when no block is being rendered. Call once nothing reads from
    /// the source any more, before disposing it: a render that a cancelled
    /// request started carries on without it.
    /// </summary>
    public async Task DrainAsync()
    {
        while (!_rendering.IsEmpty)
        {
            try
            {
                await Task.WhenAll(_rendering.Values.Where(r => r.IsValueCreated).Select(r => r.Value)).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // A failed render has already answered its requests.
            }

            await Task.Delay(10).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        _renderSlots.Dispose();
        _session.Dispose();
    }

    /// <summary>
    /// The block holding XYZ <paramref name="x"/>/<paramref name="y"/>: aligned
    /// to multiples of <paramref name="blockSize"/> and clipped to
    /// <paramref name="area"/>, as <c>tiles export</c> plans its blocks, or
    /// <see langword="null"/> when the tile lies outside the area.
    /// </summary>
    internal static TileBlock? BlockFor(TileArea area, int blockSize, int zoom, int x, int y)
    {
        int n = XyzTileGrid.TilesPerAxis(zoom);
        var (firstRow, lastRow) = XyzTileGrid.Rows(zoom, area.MinY, area.MaxY);
        if (y < firstRow || y > lastRow)
            return null;

        int rowStart = Math.Max(y - Mod(y, blockSize), firstRow);
        int rowEnd = Math.Min(y - Mod(y, blockSize) + blockSize - 1, lastRow);

        // Columns are worked out unwrapped, so an area kept in a 0…360° frame
        // or crossing the antimeridian (past ±180°) is matched on either side.
        double size = XyzTileGrid.TileWorldSize(zoom);
        long first = (long)Math.Floor((area.MinX + XyzTileGrid.Extent) / size);
        long last = Math.Max(first, (long)Math.Ceiling((area.MaxX + XyzTileGrid.Extent) / size) - 1);
        long columnStart, columnEnd;
        if (last - first + 1 >= n)
        {
            columnStart = Math.Max(x - Mod(x, blockSize), 0);
            columnEnd = Math.Min(x - Mod(x, blockSize) + blockSize - 1, n - 1);
        }
        else
        {
            // The copy of x (x + k·n) inside the unwrapped area, if any.
            long k = (long)Math.Ceiling((first - x) / (double)n);
            long unwrapped = x + (k * n);
            if (unwrapped > last)
                return null;

            long copy = k * n;
            long alignedStart = unwrapped - Mod(x, blockSize);
            columnStart = Math.Max(Math.Max(alignedStart, first), copy) - copy;
            columnEnd = Math.Min(Math.Min(alignedStart + blockSize - 1, last), copy + n - 1) - copy;
        }

        return new TileBlock(zoom, (int)columnStart, rowStart, (int)(columnEnd - columnStart + 1), rowEnd - rowStart + 1);

        static int Mod(int value, int divisor) => ((value % divisor) + divisor) % divisor;
    }

    private async Task<IReadOnlyDictionary<(int X, int Y), byte[]>> RenderBlockAsync(string palette, DateTime? time, TileBlock block)
    {
        await _renderSlots.WaitAsync().ConfigureAwait(false);
        try
        {
            // Rendering is CPU-bound; keep it off the request threads.
            var tiles = await Task.Run(() => Render(palette, time, block)).ConfigureAwait(false);
            foreach (var ((x, y), data) in tiles)
            {
                var key = new TileKey(palette, block.Zoom, x, y, time);
                _cache.Add(key, data);
                _diskCache?.Add(key, data);
            }

            Interlocked.Increment(ref _blocksRendered);
            return tiles;
        }
        finally
        {
            _renderSlots.Release();

            // Later requests find the tiles in the cache (or render again if
            // they were evicted).
            _rendering.TryRemove((palette, time, block), out _);
        }
    }

    private Dictionary<(int X, int Y), byte[]> Render(string palette, DateTime? time, TileBlock block)
    {
        var scene = _scenes.Get(palette, time);
        var rendered = scene.RendererFor(block.Zoom, _layout).Render(block);
        try
        {
            return rendered.ToDictionary(t => (t.X, t.Y), t => t.IsEmpty ? [] : _session.Encode(t));
        }
        finally
        {
            foreach (var tile in rendered)
                tile.Dispose();
        }
    }
}

/// <summary>
/// The prepared scenes of a <see cref="RenderedTileSource"/>, one per palette
/// and time step, created on first use. Only the most recently used few are
/// kept, so stepping through a long forecast doesn't hold a scene per step;
/// one evicted is prepared again when next needed. Thread-safe.
/// </summary>
internal sealed class SceneCache(TileRenderSession session)
{
    /// <summary>How many scenes are kept.</summary>
    public const int Capacity = 8;

    private readonly Lock _gate = new();
    private readonly Dictionary<(string Palette, DateTime? Time), LinkedListNode<((string Palette, DateTime? Time) Key, Lazy<TileScene> Scene)>> _entries = [];
    private readonly LinkedList<((string Palette, DateTime? Time) Key, Lazy<TileScene> Scene)> _recency = new();

    /// <summary>The number of scenes held.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
                return _entries.Count;
        }
    }

    /// <summary>Adds a scene already prepared.</summary>
    public void Seed(string palette, DateTime? time, TileScene scene) => Entry(palette, time, new Lazy<TileScene>(scene));

    /// <summary>The scene for <paramref name="palette"/> at <paramref name="time"/>, prepared on first use.</summary>
    public TileScene Get(string palette, DateTime? time) =>
        Entry(palette, time, new Lazy<TileScene>(() => session.Prepare(palette, time))).Value;

    private Lazy<TileScene> Entry(string palette, DateTime? time, Lazy<TileScene> create)
    {
        lock (_gate)
        {
            var key = (palette, time);
            if (_entries.TryGetValue(key, out var node))
            {
                _recency.Remove(node);
                _recency.AddFirst(node);
                return node.Value.Scene;
            }

            _entries[key] = _recency.AddFirst((key, create));
            while (_entries.Count > Capacity && _recency.Last is { } oldest)
            {
                _recency.RemoveLast();
                _entries.Remove(oldest.Value.Key);
            }

            return create;
        }
    }
}

/// <summary>One tile in one palette, at one time step (<see langword="null"/> for <c>--time-step</c>).</summary>
internal readonly record struct TileKey(string Palette, int Zoom, int X, int Y, DateTime? Time = null);

/// <summary>
/// Encoded tiles kept in memory up to a byte budget, evicting the least
/// recently used first. Thread-safe.
/// </summary>
internal sealed class TileCache
{
    // An empty tile is cached as an empty array; count it at a nominal size so
    // a flood of them is bounded too.
    private const int EntryOverhead = 64;

    private readonly long _capacity;
    private readonly Lock _gate = new();
    private readonly Dictionary<TileKey, LinkedListNode<(TileKey Key, byte[] Data)>> _entries = [];
    private readonly LinkedList<(TileKey Key, byte[] Data)> _recency = new();
    private long _size;

    public TileCache(long capacityBytes) => _capacity = Math.Max(0, capacityBytes);

    /// <summary>The bytes held.</summary>
    public long Size
    {
        get
        {
            lock (_gate)
                return _size;
        }
    }

    public bool TryGet(TileKey key, out byte[] data)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var node))
            {
                _recency.Remove(node);
                _recency.AddFirst(node);
                data = node.Value.Data;
                return true;
            }
        }

        data = [];
        return false;
    }

    public void Add(TileKey key, byte[] data)
    {
        long cost = data.Length + EntryOverhead;
        if (cost > _capacity)
            return;

        lock (_gate)
        {
            if (_entries.Remove(key, out var existing))
            {
                _recency.Remove(existing);
                _size -= existing.Value.Data.Length + EntryOverhead;
            }

            _entries[key] = _recency.AddFirst((key, data));
            _size += cost;

            while (_size > _capacity && _recency.Last is { } oldest)
            {
                _recency.RemoveLast();
                _entries.Remove(oldest.Value.Key);
                _size -= oldest.Value.Data.Length + EntryOverhead;
            }
        }
    }
}
