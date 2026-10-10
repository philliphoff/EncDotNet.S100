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
/// At most <c>--parallel</c> blocks render at once. Each palette is portrayed
/// the first time it is asked for. Tiles outside the tiled area or its zoom
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
    private readonly ConcurrentDictionary<string, Lazy<TileScene>> _scenes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<(string Palette, TileBlock Block), Lazy<Task<IReadOnlyDictionary<(int X, int Y), byte[]>>>> _rendering = new();
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
        _scenes[_defaultPalette] = new Lazy<TileScene>(scene);
    }

    public string Path => $"{_session.Specs.Count} dataset(s) ({string.Join(", ", _session.Specs.Distinct())})";

    public TileImageFormat Format => _session.Format;

    public IReadOnlyList<string> Palettes => AllPalettes;

    /// <summary>The area and zoom range served.</summary>
    public TileLayout Layout => _layout;

    /// <summary>The number of blocks rendered so far.</summary>
    public long BlocksRendered => Interlocked.Read(ref _blocksRendered);

    public async ValueTask<byte[]?> ReadAsync(int zoom, int x, int y, string? palette, CancellationToken cancellationToken)
    {
        palette = palette?.Trim().ToLowerInvariant() ?? _defaultPalette;
        if (!AllPalettes.Contains(palette)
            || zoom < _layout.MinZoom || zoom > _layout.MaxZoom
            || !TileSource.IsValid(zoom, x, y)
            || BlockFor(_layout.Area, _blockSize, zoom, x, y) is not { } block)
        {
            return null;
        }

        var key = new TileKey(palette, zoom, x, y);
        if (_cache.TryGet(key, out var data))
            return data.Length == 0 ? null : data;

        if (_diskCache?.TryGet(key, out data) == true)
        {
            _cache.Add(key, data);
        }
        else
        {
            var render = _rendering.GetOrAdd(
                (palette, block),
                k => new Lazy<Task<IReadOnlyDictionary<(int X, int Y), byte[]>>>(() => RenderBlockAsync(k.Palette, k.Block)));
            var tiles = await render.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
            data = tiles[(x, y)];
        }

        return data.Length == 0 ? null : data;
    }

    public JsonObject ToTileJson(string? palette)
    {
        palette = palette?.Trim().ToLowerInvariant() ?? _defaultPalette;
        var json = new TileSetMetadata
        {
            Name = "s100 tiles",
            Description = $"S-100 portrayal of {string.Join(", ", _session.Specs.Distinct())} rendered on demand by s100 tiles serve.",
            Format = Format,
            MinZoom = _layout.MinZoom,
            MaxZoom = _layout.MaxZoom,
            Bounds = _layout.Bounds,
            TilePixelSize = _layout.TileOptions.PixelRatio * XyzTileGrid.TileSize,
            Settings = _session.DescribeSettings(_layout.ReferenceLatitude, palette),
        }.ToTileJson(tilesUrl: null);
        json["s100"]!["palettes"] = new JsonArray(AllPalettes.Select(p => (JsonNode?)p).ToArray());
        return json;
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

    private async Task<IReadOnlyDictionary<(int X, int Y), byte[]>> RenderBlockAsync(string palette, TileBlock block)
    {
        await _renderSlots.WaitAsync().ConfigureAwait(false);
        try
        {
            // Rendering is CPU-bound; keep it off the request threads.
            var tiles = await Task.Run(() => Render(palette, block)).ConfigureAwait(false);
            foreach (var ((x, y), data) in tiles)
            {
                var key = new TileKey(palette, block.Zoom, x, y);
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
            _rendering.TryRemove((palette, block), out _);
        }
    }

    private Dictionary<(int X, int Y), byte[]> Render(string palette, TileBlock block)
    {
        var scene = _scenes.GetOrAdd(palette, p => new Lazy<TileScene>(() => _session.Prepare(p))).Value;
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

/// <summary>One tile in one palette.</summary>
internal readonly record struct TileKey(string Palette, int Zoom, int X, int Y);

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
