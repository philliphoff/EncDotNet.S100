using System.Diagnostics;
using Mapsui;
using Mapsui.Layers;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// What the tiled layers showed in their most recent live frames, for a host
/// that measures how long a view takes to settle (map rendering slice 1).
/// </summary>
/// <param name="LayersConsidered">
/// Enabled layers in range at the latest frame's resolution that have tile
/// state, so the renderer draws them.
/// </param>
/// <param name="LayersNotPainted">
/// Considered layers whose latest live frame was not at the latest frame's
/// view, so they haven't painted the view yet.
/// </param>
/// <param name="LayersIncomplete">
/// Considered layers that painted the view but showed something other than
/// their own target tiles somewhere (a held tile, a sibling scale class, a
/// fallback band, a predecessor, or a gap), or still had visible tiles queued
/// or rasterising.
/// </param>
/// <param name="MissingTiles">Visible target tiles not resident at the frame's pixel size, summed over considered layers.</param>
/// <param name="PendingVisibleTiles">Visible tiles queued or rasterising, summed over considered layers.</param>
/// <param name="LatestFrameTimestamp">
/// The <see cref="Stopwatch"/> timestamp of the latest live frame of any
/// considered layer, or 0 when none has painted since tracking started.
/// </param>
public readonly record struct TileSettleState(
    int LayersConsidered,
    int LayersNotPainted,
    int LayersIncomplete,
    int MissingTiles,
    int PendingVisibleTiles,
    long LatestFrameTimestamp)
{
    /// <summary>
    /// Whether every considered layer painted the latest view and showed only
    /// its own target tiles, with no visible tile work left. True when no
    /// tiled layer is considered.
    /// </summary>
    public bool IsSettled => LayersNotPainted == 0 && LayersIncomplete == 0 && PendingVisibleTiles == 0;
}

/// <summary>Native bytes the tile caches hold.</summary>
/// <param name="RasterBytes">Decoded raster tiles in the per-layer hot caches.</param>
/// <param name="GpuBytes">GPU-resident tile textures.</param>
/// <param name="Tiles">Raster tiles resident across all layers.</param>
/// <param name="Layers">Layers with tile state.</param>
public readonly record struct TileCacheMemory(long RasterBytes, long GpuBytes, int Tiles, int Layers)
{
    /// <summary>Raster plus GPU bytes.</summary>
    public long TotalBytes => RasterBytes + GpuBytes;
}

public static partial class S100VectorTileRenderer
{
    private static volatile bool _settleTracking;

    /// <summary>
    /// Whether live frames record what each tiled layer showed, so
    /// <see cref="EvaluateSettle"/> can answer. Off by default, so the frame
    /// path does no extra work unless a host asks for settle measurements.
    /// Turning it off forgets nothing; turning it on takes effect from the
    /// next live frame.
    /// </summary>
    public static bool SettleTrackingEnabled
    {
        get => _settleTracking;
        set => _settleTracking = value;
    }

    /// <summary>
    /// Reports whether the tiled layers among <paramref name="layers"/> have
    /// settled: each enabled layer in range has painted the latest live view
    /// and shown only its own target tiles there, with no visible tile work
    /// queued or rasterising. Layers the renderer has never drawn (no tile
    /// state) are ignored. Requires <see cref="SettleTrackingEnabled"/>; before
    /// any frame was recorded, every layer with tile state counts as not
    /// painted. Safe to call from any thread.
    /// </summary>
    /// <param name="layers">The map's layers, for example a snapshot of <c>Map.Layers</c>.</param>
    /// <returns>The settle state.</returns>
    public static TileSettleState EvaluateSettle(IEnumerable<ILayer> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);

        var stamped = new List<(ILayer Layer, SettleStamp? Stamp, int Pending)>();
        SettleStamp? latest = null;
        foreach (var layer in layers)
        {
            if (layer is null || !layer.Enabled || !States.TryGetValue(layer, out var state))
            {
                continue;
            }

            SettleStamp? stamp;
            int pending;
            lock (state.Sync)
            {
                stamp = state.Settle;
                pending = state.PendingVisible.Count + CountVisibleInFlight(state);
            }

            stamped.Add((layer, stamp, pending));
            if (stamp is not null && (latest is null || stamp.Timestamp > latest.Timestamp))
            {
                latest = stamp;
            }
        }

        return Summarise(stamped.Select(s => new SettleInput(
            s.Layer.Enabled,
            s.Layer.MinVisible,
            s.Layer.MaxVisible,
            s.Stamp,
            s.Pending)), latest);
    }

    /// <summary>
    /// The native bytes held by the process-wide raster and GPU tile pools
    /// (<see cref="TileMemoryPool"/>), which every layer's caches charge.
    /// Safe to call from any thread.
    /// </summary>
    /// <returns>The totals.</returns>
    public static TileCacheMemory GetTileCacheMemory()
    {
        var layers = 0;
        foreach (var _ in States)
        {
            layers++;
        }

        return new TileCacheMemory(
            TileMemoryPool.Raster.ResidentBytes,
            TileMemoryPool.Gpu.ResidentBytes,
            TileMemoryPool.Raster.Count,
            layers);
    }

    /// <summary>One layer's input to <see cref="Summarise"/>.</summary>
    internal readonly record struct SettleInput(
        bool Enabled,
        double MinVisible,
        double MaxVisible,
        SettleStamp? Stamp,
        int PendingVisible);

    /// <summary>
    /// The pure settle decision over per-layer stamps. The reference view is
    /// the latest stamp's; a layer counts when it's enabled and that view's
    /// resolution is within its visible range (Mapsui only draws such layers).
    /// </summary>
    internal static TileSettleState Summarise(IEnumerable<SettleInput> inputs, SettleStamp? latest)
    {
        var considered = 0;
        var notPainted = 0;
        var incomplete = 0;
        var missing = 0;
        var pending = 0;
        foreach (var input in inputs)
        {
            if (!input.Enabled)
            {
                continue;
            }

            if (latest is not null
                && (latest.Resolution < input.MinVisible || latest.Resolution > input.MaxVisible))
            {
                continue;
            }

            considered++;
            pending += input.PendingVisible;
            if (latest is null || input.Stamp is not { } stamp || !stamp.SameView(latest))
            {
                notPainted++;
                continue;
            }

            missing += stamp.MissingTiles;
            if (!stamp.Exact || input.PendingVisible > 0)
            {
                incomplete++;
            }
        }

        return new TileSettleState(considered, notPainted, incomplete, missing, pending, latest?.Timestamp ?? 0);
    }

    private static int CountVisibleInFlight(TileState state)
    {
        var count = 0;
        foreach (var key in state.InFlight)
        {
            if (state.CurrentVisible.Contains(key))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Records a live frame in which the layer drew no tiles: culled, past its
    /// scale cap, hidden by finer cells, or drawn live from its scene. Nothing
    /// is waiting on tiles, so it counts as exact.
    /// </summary>
    private static void RecordSettleCulled(TileState state, Viewport viewport, double resolution, bool offscreen)
    {
        if (offscreen || !_settleTracking)
        {
            return;
        }

        var stamp = new SettleStamp(Stopwatch.GetTimestamp(), viewport.CenterX, viewport.CenterY, viewport.Width, viewport.Height, resolution, Exact: true, MissingTiles: 0);
        lock (state.Sync)
        {
            state.Settle = stamp;
        }
    }

    /// <summary>
    /// Records a composited live frame. Call under <c>state.Sync</c>. The frame
    /// is exact when the composite drew only target tiles and every visible
    /// tile is resident at the frame's pixel size with none queued.
    /// </summary>
    private static void RecordSettleComposite(
        TileState state, Viewport viewport, double resolution, IReadOnlyList<TileKey> visible, bool targetExact, int tilePx)
    {
        var missing = 0;
        foreach (var key in visible)
        {
            if (!state.Cache.Contains(key, tilePx))
            {
                missing++;
            }
        }

        state.Settle = new SettleStamp(
            Stopwatch.GetTimestamp(),
            viewport.CenterX,
            viewport.CenterY,
            viewport.Width,
            viewport.Height,
            resolution,
            Exact: targetExact && missing == 0 && state.PendingVisible.Count == 0,
            MissingTiles: missing);
    }

    /// <summary>What one live frame of a layer showed, and at which view.</summary>
    internal sealed record SettleStamp(
        long Timestamp,
        double CenterX,
        double CenterY,
        double Width,
        double Height,
        double Resolution,
        bool Exact,
        int MissingTiles)
    {
        /// <summary>Whether <paramref name="other"/> was drawn at the same view; every layer of one frame shares it exactly.</summary>
        public bool SameView(SettleStamp other) =>
            CenterX == other.CenterX
            && CenterY == other.CenterY
            && Width == other.Width
            && Height == other.Height
            && Resolution == other.Resolution;
    }
}
