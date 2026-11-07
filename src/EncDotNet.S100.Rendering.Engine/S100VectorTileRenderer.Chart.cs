using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using EncDotNet.S100.Renderers.Skia.Scene;
using EncDotNet.S100.Rendering.Scene;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using SkiaSharp;
using CoreViewport = EncDotNet.S100.Pipelines.Viewport;
using S100Diag = EncDotNet.S100.Rendering.Engine.Diagnostics;
using SceneRgbaColor = EncDotNet.S100.Pipelines.RgbaColor;

namespace EncDotNet.S100.Rendering.Engine;

// Chart tiles (map rendering slice 8): one raster per slot across every layer
// of a ChartStack. A slot's plan lists the stack layers that draw there at the
// live resolution, in stack order, with the finer coverages that clip each;
// its StackSlice (a hash of the plan) goes into the tile key, so a stack change
// re-rasters only the slots whose content changed, and a changed slot shows its
// old tile until the new one lands.
public static partial class S100VectorTileRenderer
{
    /// <summary>The disk-cache namespace chart tiles share: their keys are content-addressed.</summary>
    internal const string ChartDiskNamespace = "chart";

    private const int SlotMemoCapacity = 8192;
    private const int PlanCapacity = 8192;

    /// <summary>
    /// Binds <paramref name="stack"/> to <paramref name="owner"/>, a chart
    /// layer: from the next frame the owner draws one chart tile per slot over
    /// every layer of the stack. Tiles of slots the change doesn't touch stay
    /// valid; a changed slot keeps showing its old tile until the new one lands.
    /// </summary>
    /// <param name="owner">The chart layer's owner token.</param>
    /// <param name="stack">The stack to draw.</param>
    /// <param name="options">The options of the session the owner belongs to.</param>
    /// <param name="planner">The demand planner of the session the owner belongs to.</param>
    internal static void BindStack(object owner, ChartStack stack, ChartSessionOptions options, Prediction.DemandPlanner? planner = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(options);

        var resolved = ResolveStack(stack);
        var state = States.GetValue(owner, static l => new TileState(l));
        lock (state.Sync)
        {
            state.Options = options;
            state.Planner = planner;
            state.Chart = resolved;
            state.DiskNamespace = options.TileDiskCacheEnabled ? ChartDiskNamespace : null;
            state.SlotContents.Clear();
            state.PlanMemo.Clear();
        }

        LayerRepaint.Request(owner);
    }

    /// <summary>
    /// Whether the scene bound to <paramref name="source"/> can be drawn into
    /// a chart layer: it is bound, and its base plane isn't drawn live (a
    /// sparse overlay such as a route stays a layer of its own).
    /// </summary>
    internal static bool CanJoinChart(object source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!States.TryGetValue(source, out var state))
        {
            return false;
        }

        lock (state.Sync)
        {
            return state.Scene is not null && state.Chart is null && !state.LiveBase;
        }
    }

    /// <summary>
    /// Builds the planes of <paramref name="source"/>'s scene without the
    /// <paramref name="suppressed"/> features ahead of a stack that will draw
    /// them, so publishing the stack finds them ready. Safe on any thread.
    /// </summary>
    internal static void PrepareSuppressedFeatures(object source, IReadOnlySet<string> suppressed)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(suppressed);
        if (suppressed.Count == 0 || !States.TryGetValue(source, out var state))
        {
            return;
        }

        VectorScene? baseScene;
        VectorScene? overlay;
        lock (state.Sync)
        {
            baseScene = state.Scene;
            overlay = state.Overlay?.Scene;
        }

        if (baseScene is not null)
        {
            FilterSuppressed(baseScene, overlay, suppressed);
        }
    }

    /// <summary>The extent of the stack bound to <paramref name="owner"/>, or null.</summary>
    internal static WorldRect? ChartExtent(object owner)
    {
        if (!States.TryGetValue(owner, out var state))
        {
            return null;
        }

        lock (state.Sync)
        {
            return state.Chart?.Extent;
        }
    }

    // ---------------------------------------------------------------------
    // Stack resolution.

    private static ResolvedChartStack ResolveStack(ChartStack stack)
    {
        var layers = new List<ChartLayerState>(stack.Entries.Count);
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var entry in stack.Entries)
        {
            if (!entry.Visible || entry.Opacity <= 0 || !States.TryGetValue(entry.Source, out var source))
            {
                continue;
            }

            VectorScene? baseScene;
            BaseSpatialIndex? baseIndex;
            OverlaySource? overlay;
            string? diskNamespace;
            lock (source.Sync)
            {
                baseScene = source.Scene;
                baseIndex = source.BaseIndex;
                overlay = source.Overlay;
                diskNamespace = source.DiskNamespace;
            }

            if (baseScene is null || baseIndex is null)
            {
                continue;
            }

            var key = diskNamespace;
            if (entry.SuppressedFeatures is { Count: > 0 } suppressed)
            {
                var filtered = FilterSuppressed(baseScene, overlay?.Scene, suppressed);
                baseScene = filtered.Base;
                baseIndex = filtered.BaseIndex;
                overlay = filtered.Overlay is null ? null : new OverlaySource(filtered.Overlay, filtered.OverlayIndex);
                key = key is null ? null : key + "|" + filtered.Hash;
            }
            else if (overlay is not null)
            {
                // A fresh scratch holder: the overlay is drawn per stack layer,
                // and the source's own holder belongs to the source.
                overlay = new OverlaySource(overlay.Scene, overlay.Index);
            }

            if (baseIndex.Bounds is { } b)
            {
                minX = Math.Min(minX, b.MinX);
                minY = Math.Min(minY, b.MinY);
                maxX = Math.Max(maxX, b.MaxX);
                maxY = Math.Max(maxY, b.MaxY);
            }

            var finer = entry.Finer is { Length: > 0 } f ? f : [];
            layers.Add(new ChartLayerState(
                layers.Count,
                entry.Source,
                baseScene,
                baseIndex,
                overlay,
                key ?? LocalSceneKey(baseScene),
                stable: key is not null,
                finer,
                entry.Opacity >= 1 ? 1.0 : entry.Opacity,
                entry.MinVisibleResolution,
                entry.MaxVisibleResolution));
        }

        return new ResolvedChartStack(
            stack.Version,
            [.. layers],
            maxX >= minX ? new WorldRect(minX, minY, maxX, maxY) : null);
    }

    private static readonly ConditionalWeakTable<VectorScene, string> LocalKeys = new();
    private static long _localKeySequence;

    /// <summary>
    /// A process-local key for a scene that has no stable disk namespace: its
    /// chart tiles are cached in memory but never written to disk.
    /// </summary>
    private static string LocalSceneKey(VectorScene scene) =>
        LocalKeys.GetValue(scene, static _ => "local:" + Interlocked.Increment(ref _localKeySequence).ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static readonly ConditionalWeakTable<VectorScene, SuppressionMemo> SuppressionMemos = new();

    /// <summary>
    /// The scene without the ops of <paramref name="suppressed"/> features (an
    /// S-98 rule's op filter), memoised per source scene and suppressed set, so
    /// a stack recomposed while the set is unchanged reuses the filtered planes
    /// and their indexes.
    /// </summary>
    private static FilteredPlanes FilterSuppressed(VectorScene baseScene, VectorScene? overlayScene, IReadOnlySet<string> suppressed)
    {
        var hash = HashOfSet(suppressed);
        var memo = SuppressionMemos.GetValue(baseScene, static _ => new SuppressionMemo());
        lock (memo)
        {
            if (memo.Planes is { } cached && cached.Hash == hash && ReferenceEquals(memo.Overlay, overlayScene))
            {
                return cached;
            }

            var keptBase = new List<PaintOp>(baseScene.Ops.Count);
            foreach (var op in baseScene.Ops)
            {
                if (!suppressed.Contains(op.FeatureReference))
                {
                    keptBase.Add(op);
                }
            }

            var filteredBase = new VectorScene(keptBase)
            {
                ScaleMinimumCap = baseScene.ScaleMinimumCap,
                PreferLiveDrawing = baseScene.PreferLiveDrawing,
            };

            VectorScene? filteredOverlay = null;
            OverlaySpatialIndex? overlayIndex = null;
            if (overlayScene is not null)
            {
                var keptOverlay = new List<PaintOp>(overlayScene.Ops.Count);
                foreach (var op in overlayScene.Ops)
                {
                    if (!suppressed.Contains(op.FeatureReference))
                    {
                        keptOverlay.Add(op);
                    }
                }

                filteredOverlay = new VectorScene(keptOverlay)
                {
                    ScaleMinimumCap = overlayScene.ScaleMinimumCap,
                    PreferLiveDrawing = overlayScene.PreferLiveDrawing,
                };
                overlayIndex = new OverlaySpatialIndex(filteredOverlay);
            }

            var planes = new FilteredPlanes(hash, filteredBase, new BaseSpatialIndex(filteredBase), filteredOverlay, overlayIndex);
            memo.Planes = planes;
            memo.Overlay = overlayScene;
            return planes;
        }
    }

    private static string HashOfSet(IReadOnlySet<string> set)
    {
        var ordered = set.Where(static r => r is not null).OrderBy(static r => r, StringComparer.Ordinal);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', ordered)));
        return Convert.ToHexString(hash, 0, 16).ToLowerInvariant();
    }

    private sealed class SuppressionMemo
    {
        public FilteredPlanes? Planes;
        public VectorScene? Overlay;
    }

    private sealed record FilteredPlanes(
        string Hash,
        VectorScene Base,
        BaseSpatialIndex BaseIndex,
        VectorScene? Overlay,
        OverlaySpatialIndex? OverlayIndex);

    private static readonly ConditionalWeakTable<Geometry, string> CoverageKeys = new();

    /// <summary>A stable key for a finer coverage's geometry: a hash of its WKB.</summary>
    private static string CoverageKey(Geometry coverage) =>
        CoverageKeys.GetValue(coverage, static g =>
            Convert.ToHexString(SHA256.HashData(new WKBWriter().Write(g)), 0, 16).ToLowerInvariant());

    // ---------------------------------------------------------------------
    // Slot plans.

    /// <summary>
    /// The plan of slot (<paramref name="band"/>, <paramref name="x"/>,
    /// <paramref name="y"/>) at the live <paramref name="resolution"/>, or
    /// <see langword="null"/> when no stack layer draws there. Call under
    /// <c>state.Sync</c>.
    /// </summary>
    private static SlotPlan? PlanSlot(
        TileState state, ResolvedChartStack chart, int band, int x, int y, double resolution, double liveDenominator)
    {
        var slot = (band, x, y);
        var content = SlotContentFor(state, chart, slot);
        if (content.Layers.Length == 0)
        {
            return null;
        }

        // The answer changes with the live resolution and, through the
        // layers' out-of-band caps, with the live scale at the view centre.
        ulong capSignature = 0;
        for (var i = 0; i < content.Layers.Length && i < 64; i++)
        {
            if (chart.Layers[content.Layers[i]].Cap is double cap && liveDenominator > cap)
            {
                capSignature |= 1UL << i;
            }
        }

        if (state.PlanMemo.TryGetValue(slot, out var memo)
            && memo.Resolution.Equals(resolution)
            && memo.CapSignature == capSignature)
        {
            return memo.Plan;
        }

        var plan = BuildPlan(chart, content, band, x, y, resolution, liveDenominator);
        if (state.PlanMemo.Count >= SlotMemoCapacity)
        {
            state.PlanMemo.Clear();
        }

        state.PlanMemo[slot] = new PlanMemoEntry(resolution, capSignature, plan);
        return plan;
    }

    private static SlotContent SlotContentFor(TileState state, ResolvedChartStack chart, (int Band, int X, int Y) slot)
    {
        if (state.SlotContents.TryGetValue(slot, out var content))
        {
            return content;
        }

        var (minX, minY, maxX, maxY) = TileGrid.TileWorldBounds(new TileKey(slot.Band, slot.X, slot.Y));
        var gutterWorld = GutterDip * TileGrid.ResolutionForBand(slot.Band);
        minX -= gutterWorld;
        minY -= gutterWorld;
        maxX += gutterWorld;
        maxY += gutterWorld;

        var layers = new List<int>();
        var thresholds = new List<double[]>();
        foreach (var layer in chart.Layers)
        {
            if (layer.Bounds is { } b && !layer.Index.HasUnboundedOps
                && (b.MaxX < minX || b.MinX > maxX || b.MaxY < minY || b.MinY > maxY))
            {
                continue;
            }

            if (!layer.Index.Intersects(minX, minY, maxX, maxY))
            {
                continue;
            }

            layers.Add(layer.Position);
            thresholds.Add(TileScaleClass.Collect(layer.Index.Query(minX, minY, maxX, maxY)));
        }

        content = new SlotContent([.. layers], [.. thresholds]);
        if (state.SlotContents.Count >= SlotMemoCapacity)
        {
            state.SlotContents.Clear();
        }

        state.SlotContents[slot] = content;
        return content;
    }

    private static SlotPlan? BuildPlan(
        ResolvedChartStack chart, SlotContent content, int band, int x, int y, double resolution, double liveDenominator)
    {
        var key = new TileKey(band, x, y);
        var (minX, minY, maxX, maxY) = TileGrid.TileWorldBounds(key);
        var bandResolution = TileGrid.ResolutionForBand(band);
        var gutterWorld = GutterDip * bandResolution;
        var margin = HiddenMarginDip * Math.Sqrt(2.0) * bandResolution;

        List<ChartLayerState>? layers = null;
        List<SlotClip[]>? clips = null;
        var thresholds = new SortedSet<double>();
        for (var i = 0; i < content.Layers.Length; i++)
        {
            var layer = chart.Layers[content.Layers[i]];
            if (resolution < layer.MinResolution || resolution > layer.MaxResolution)
            {
                continue;
            }

            if (layer.Cap is double cap && liveDenominator > cap)
            {
                continue;
            }

            // One finer coverage hides the slot: the layer adds nothing here.
            // (Never a union of coverages: that left hairlines at seams.)
            if (CoverageClip.HiddenFor(layer.Finer, resolution) is { } hidden
                && hidden.Covers(minX - margin, minY - margin, maxX + margin, maxY + margin))
            {
                continue;
            }

            List<SlotClip>? layerClips = null;
            foreach (var finer in layer.Finer)
            {
                if (resolution > finer.CutoffResolution || finer.Coverage.IsEmpty)
                {
                    continue;
                }

                var e = finer.Coverage.EnvelopeInternal;
                foreach (var offset in WorldCopies.OffsetsX)
                {
                    if (e.MaxX + offset < minX - gutterWorld || e.MinX + offset > maxX + gutterWorld
                        || e.MaxY < minY - gutterWorld || e.MinY > maxY + gutterWorld)
                    {
                        continue;
                    }

                    (layerClips ??= []).Add(new SlotClip(finer, offset));
                }
            }

            (layers ??= []).Add(layer);
            (clips ??= []).Add(layerClips is null ? [] : [.. layerClips]);
            foreach (var t in content.Thresholds[i])
            {
                thresholds.Add(t);
            }
        }

        if (layers is null)
        {
            return null;
        }

        // The slice: ordered scene keys, opacities and clip inputs.
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> scratch = stackalloc byte[8];
        var persistable = true;
        for (var i = 0; i < layers.Count; i++)
        {
            var layer = layers[i];
            persistable &= layer.Stable;
            hash.AppendData(Encoding.UTF8.GetBytes(layer.Key));
            BinaryPrimitives.WriteDoubleLittleEndian(scratch, layer.Opacity);
            hash.AppendData(scratch);
            foreach (var clip in clips![i])
            {
                hash.AppendData(Encoding.UTF8.GetBytes(CoverageKey(clip.Coverage.Coverage)));
                BinaryPrimitives.WriteDoubleLittleEndian(scratch, clip.OffsetX);
                hash.AppendData(scratch);
            }

            hash.AppendData("\n"u8);
        }

        Span<byte> digest = stackalloc byte[32];
        hash.GetHashAndReset(digest);
        var slice = BinaryPrimitives.ReadUInt128LittleEndian(digest);
        if (slice == UInt128.Zero)
        {
            slice = UInt128.One;
        }

        return new SlotPlan([.. layers], [.. clips!], [.. thresholds], slice, persistable);
    }

    /// <summary>
    /// The chart tile keys of <paramref name="tiles"/>: each slot with content
    /// at <paramref name="resolution"/>, with its scale class and stack slice.
    /// Records each key's plan. Call under <c>state.Sync</c>.
    /// </summary>
    private static IReadOnlyList<TileKey> ChartTiles(
        TileState state, ResolvedChartStack chart, IReadOnlyList<TileKey> tiles,
        double bandRatio, double resolution, double liveDenominator)
    {
        var result = new List<TileKey>(tiles.Count);
        foreach (var grid in tiles)
        {
            if (TryChartTile(state, chart, grid, bandRatio, resolution, liveDenominator, out var key))
            {
                result.Add(key);
            }
        }

        return result;
    }

    /// <summary>
    /// The chart tile key of <paramref name="grid"/>'s slot shown at
    /// <paramref name="resolution"/> (the live resolution for the slot's
    /// band), or <see langword="false"/> when nothing draws there.
    /// </summary>
    private static bool TryChartTile(
        TileState state, ResolvedChartStack chart, TileKey grid, double bandRatio,
        double resolution, double liveDenominator, out TileKey key)
    {
        var plan = PlanSlot(state, chart, grid.Band, grid.X, grid.Y, resolution, liveDenominator);
        if (plan is null)
        {
            key = default;
            return false;
        }

        var scaleClass = plan.Thresholds.Length == 0
            ? 0
            : TileScaleClass.For(plan.Thresholds, grid.Band, BandDenominator(grid), bandRatio);
        key = new TileKey(grid.Band, grid.X, grid.Y, scaleClass, plan.Slice);
        state.Plans[key] = plan;
        return true;
    }

    /// <summary>
    /// Drops recorded plans no tile needs any more, once there are many. Call
    /// under <c>state.Sync</c>.
    /// </summary>
    private static void PrunePlans(TileState state)
    {
        if (state.Plans.Count <= PlanCapacity)
        {
            return;
        }

        var keep = new HashSet<TileKey>(state.Cache.SnapshotKeys());
        keep.UnionWith(state.InFlight);
        keep.UnionWith(state.PendingVisible);
        keep.UnionWith(state.PendingPredicted);
        keep.UnionWith(state.PendingCrossBand);
        keep.UnionWith(state.CurrentVisible);
        keep.UnionWith(state.CurrentSpeculative);
        keep.UnionWith(state.ShownBySlot.Values);
        foreach (var key in state.Plans.Keys.ToList())
        {
            if (!keep.Contains(key))
            {
                state.Plans.Remove(key);
            }
        }
    }

    // ---------------------------------------------------------------------
    // Stale-while-revalidate.

    /// <summary>
    /// <paramref name="keys"/> with each uncached chart tile replaced by the
    /// last tile its slot showed, while that is still cached: a slot whose
    /// content changed keeps its old image until the new one lands. Returns the
    /// input list when nothing is replaced. Call under <c>state.Sync</c>.
    /// </summary>
    private static IReadOnlyList<TileKey> WithStandIns(TileState state, IReadOnlyList<TileKey> keys)
    {
        TileKey[]? replaced = null;
        for (var i = 0; i < keys.Count; i++)
        {
            var key = keys[i];
            if (state.Cache.Contains(key))
            {
                continue;
            }

            if (state.ShownBySlot.TryGetValue(key.Slot, out var shown)
                && shown != key
                && state.Cache.Contains(shown))
            {
                replaced ??= [.. keys];
                replaced[i] = shown;
            }
        }

        return (IReadOnlyList<TileKey>?)replaced ?? keys;
    }

    /// <summary>
    /// Whether a chart tile rastered for <paramref name="key"/>, whose stack
    /// has since changed, should stand in for its slot: the slot is in view
    /// at the key's band and scale class, and shows neither its current
    /// tile nor a stand-in yet. A slot whose cells land one after another
    /// would otherwise lose each raster to the next arrival and stay blank
    /// until they stop. Call under <c>state.Sync</c>.
    /// </summary>
    private static bool IsWantedStandIn(TileState state, TileKey key)
    {
        foreach (var target in state.CurrentVisible)
        {
            if (target.Slot != key.Slot || target.ScaleClass != key.ScaleClass)
            {
                continue;
            }

            if (state.Cache.Contains(target))
            {
                return false;
            }

            return !(state.ShownBySlot.TryGetValue(key.Slot, out var shown) && state.Cache.Contains(shown));
        }

        return false;
    }

    /// <summary>
    /// Remembers the tile each slot shows exactly this frame, so a later stack
    /// change can keep showing it. Call under <c>state.Sync</c>.
    /// </summary>
    private static void RecordShown(TileState state, IReadOnlyList<CopyTiles> copies)
    {
        if (state.ShownBySlot.Count > SlotMemoCapacity)
        {
            state.ShownBySlot.Clear();
        }

        foreach (var copy in copies)
        {
            foreach (var key in copy.Tiles)
            {
                if (state.Cache.Contains(key))
                {
                    state.ShownBySlot[key.Slot] = key;
                }
            }
        }
    }

    /// <summary>
    /// The stack layers whose symbols must wait: a slot in view whose target
    /// includes the layer shows a stand-in without it, or shows nothing at all
    /// (and no backdrop either). Call under <c>state.Sync</c>.
    /// </summary>
    private static HashSet<ChartLayerState>? UnrevealedLayers(
        TileState state, IReadOnlyList<CopyTiles> copies, IReadOnlyList<IReadOnlyList<TileKey>> drawn, bool anyBackdrop)
    {
        HashSet<ChartLayerState>? unrevealed = null;
        for (var c = 0; c < copies.Count; c++)
        {
            var targets = copies[c].Tiles;
            var shown = drawn[c];
            for (var i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                if (!state.Plans.TryGetValue(target, out var targetPlan))
                {
                    continue;
                }

                var stand = shown[i];
                if (stand == target && state.Cache.Contains(target))
                {
                    continue;
                }

                if (state.Cache.Contains(stand) && state.Plans.TryGetValue(stand, out var standPlan))
                {
                    foreach (var layer in targetPlan.Layers)
                    {
                        if (Array.IndexOf(standPlan.Layers, layer) < 0)
                        {
                            (unrevealed ??= []).Add(layer);
                        }
                    }
                }
                else if (!anyBackdrop)
                {
                    (unrevealed ??= []).UnionWith(targetPlan.Layers);
                }
            }
        }

        return unrevealed;
    }

    // ---------------------------------------------------------------------
    // Overlay.

    /// <summary>
    /// Draws each stack layer's live overlay (point symbols, soundings and
    /// text) in stack order, clipped by its finer coverages and at its
    /// opacity, leaving out layers whose base hasn't appeared in view yet.
    /// </summary>
    private static void DrawChartOverlay(
        SKCanvas canvas, ResolvedChartStack chart, HashSet<ChartLayerState>? unrevealed,
        ViewState viewport, IReadOnlyList<double> copies,
        double centerX, double centerY, double widthDip, double heightDip,
        double resolution, double rotationDeg, float deviceScale, double liveDenominator)
    {
        foreach (var layer in chart.Layers)
        {
            if (layer.Overlay is not { } overlay || overlay.Scene.Ops.Count == 0)
            {
                continue;
            }

            if (resolution < layer.MinResolution || resolution > layer.MaxResolution
                || (layer.Cap is double cap && liveDenominator > cap)
                || unrevealed?.Contains(layer) == true)
            {
                continue;
            }

            if (CoverageClip.HiddenFor(layer.Finer, resolution) is { } hidden
                && AreAllCopiesHidden(hidden, viewport, resolution, copies))
            {
                continue;
            }

            var paths = new List<SKPath>();
            if (layer.Finer.Length > 0)
            {
                foreach (var offset in WorldCopies.OffsetsX)
                {
                    paths.AddRange(CoverageClip.BuildActiveDifferencePaths(
                        layer.Finer, viewport with { CenterX = viewport.CenterX - offset }, resolution));
                }
            }

            var saves = canvas.Save();
            try
            {
                foreach (var path in paths)
                {
                    canvas.ClipPath(path, SKClipOperation.Difference, antialias: true);
                }

                if (layer.Opacity < 1.0)
                {
                    using var paint = new SKPaint { Color = SKColors.White.WithAlpha((byte)Math.Round(layer.Opacity * 255)) };
                    canvas.SaveLayer(paint);
                }

                foreach (var offset in copies)
                {
                    DrawOverlay(canvas, overlay, centerX - offset, centerY, widthDip, heightDip, resolution, rotationDeg, deviceScale);
                }
            }
            finally
            {
                canvas.RestoreToCount(saves);
                foreach (var path in paths)
                {
                    path.Dispose();
                }
            }
        }
    }

    // ---------------------------------------------------------------------
    // Raster.

    /// <summary>
    /// Rasterises chart tiles <paramref name="keys"/> (one metatile's worth of
    /// one band) from their plans: one raster when the tiles share a scale
    /// denominator and no layer reaches across a world-copy seam, otherwise one
    /// per tile.
    /// </summary>
    private static IReadOnlyDictionary<TileKey, SKImage> RasterizeChartTiles(
        IReadOnlyList<TileKey> keys, IReadOnlyDictionary<TileKey, SlotPlan> plans, float deviceScale)
    {
        var result = new Dictionary<TileKey, SKImage>(keys.Count);
        try
        {
            if (keys.Count > 1 && TryRasterizeChartMetatile(keys, plans, deviceScale, result))
            {
                return result;
            }

            if (keys.Count > 1)
            {
                RecordMetatileFallback("chart");
            }

            foreach (var key in keys)
            {
                if (!plans.TryGetValue(key, out var plan))
                {
                    continue;
                }

                using var bitmap = RasterizeChartTile(plan, key, deviceScale);
                result.Add(key, SKImage.FromBitmap(bitmap));
            }
        }
        catch
        {
            DisposeImages(result.Values);
            throw;
        }

        return result;
    }

    /// <summary>
    /// The scale denominator <paramref name="key"/>'s tile is rasterised at
    /// within its plan's scale class, before any layer's own cap.
    /// </summary>
    private static double ChartClassDenominator(SlotPlan plan, TileKey key) =>
        TileScaleClass.Denominator(plan.Thresholds, key.Band, BandDenominator(key), key.ScaleClass);

    /// <summary>Rasterises one chart tile from its <paramref name="plan"/>.</summary>
    internal static SKBitmap RasterizeChartTile(SlotPlan plan, TileKey key, float deviceScale)
    {
        using var activity = S100Diag.Telemetry.ActivitySource.StartActivity(
            "s100.render.tile.rasterize", ActivityKind.Internal);
        activity?.SetTag("s100.render.tile.keys", FormatTileKeys([key]));
        activity?.SetTag("s100.render.tile.count", 1);
        activity?.SetTag("s100.render.tile.band", key.Band);
        activity?.SetTag("s100.render.tile.layers", plan.Layers.Length);

        var (minX, minY, maxX, maxY) = TileGrid.TileWorldBounds(key);
        var corePx = CorePixelSize(deviceScale);
        var gutterPx = GutterPixelSize(deviceScale);
        var gutterWorld = gutterPx * (maxX - minX) / corePx;
        var rasterPx = corePx + 2 * gutterPx;

        using var raster = new SKBitmap(rasterPx, rasterPx, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(raster))
        {
            canvas.Clear(SKColors.Transparent);
            DrawPlan(
                canvas, plan.Layers, plan.Clips, ChartClassDenominator(plan, key),
                minX - gutterWorld, minY - gutterWorld, maxX + gutterWorld, maxY + gutterWorld,
                rasterPx, rasterPx, gutterPx, TileGrid.ResolutionForBand(key.Band), adjacentWorlds: true);
            canvas.Flush();
        }

        return CropStored(raster, gutterPx - ApronPx, gutterPx - ApronPx, TilePixelSize(deviceScale));
    }

    private static bool TryRasterizeChartMetatile(
        IReadOnlyList<TileKey> keys, IReadOnlyDictionary<TileKey, SlotPlan> plans, float deviceScale,
        Dictionary<TileKey, SKImage> result)
    {
        // One raster needs one scale denominator for every tile, and the
        // union of the tiles' plans in stack order: a layer missing from a
        // tile's plan has no ops there or is hidden by one finer coverage over
        // the tile's core and apron, and a clip that misses a tile changes
        // none of its pixels.
        double? denominator = null;
        var union = new SortedDictionary<int, (ChartLayerState Layer, HashSet<SlotClip> Clips)>();
        foreach (var key in keys)
        {
            if (!plans.TryGetValue(key, out var plan))
            {
                return false;
            }

            var d = ChartClassDenominator(plan, key);
            if (denominator is null)
            {
                denominator = d;
            }
            else if (!denominator.Value.Equals(d))
            {
                RecordMetatileFallback("scamin");
                return false;
            }

            for (var i = 0; i < plan.Layers.Length; i++)
            {
                var layer = plan.Layers[i];
                if (!union.TryGetValue(layer.Position, out var entry))
                {
                    entry = (layer, []);
                    union[layer.Position] = entry;
                }

                entry.Clips.UnionWith(plan.Clips[i]);
            }
        }

        var xMin = keys.Min(k => k.X);
        var xMax = keys.Max(k => k.X);
        var yMin = keys.Min(k => k.Y);
        var yMax = keys.Max(k => k.Y);
        var band = keys[0].Band;
        var spanX = xMax - xMin + 1;
        var spanY = yMax - yMin + 1;
        var corePx = CorePixelSize(deviceScale);
        var gutterPx = GutterPixelSize(deviceScale);
        var tilePx = TilePixelSize(deviceScale);
        var widthPx = spanX * corePx + 2 * gutterPx;
        var heightPx = spanY * corePx + 2 * gutterPx;
        if (widthPx > MaxImageDimension || heightPx > MaxImageDimension)
        {
            RecordMetatileFallback("dimension");
            return false;
        }

        var (blockMinX, _, _, blockMaxY) = TileGrid.TileWorldBounds(new TileKey(band, xMin, yMin));
        var (_, blockMinY, blockMaxX, _) = TileGrid.TileWorldBounds(new TileKey(band, xMax, yMax));
        var gutterWorld = gutterPx * (blockMaxX - blockMinX) / (spanX * corePx);
        var fullMinX = blockMinX - gutterWorld;
        var fullMinY = blockMinY - gutterWorld;
        var fullMaxX = blockMaxX + gutterWorld;
        var fullMaxY = blockMaxY + gutterWorld;
        foreach (var (layer, _) in union.Values)
        {
            if (HasAdjacentWorldOps(layer.Index, fullMinX, fullMinY, fullMaxX, fullMaxY))
            {
                RecordMetatileFallback("world_copy");
                return false;
            }
        }

        var layers = union.Values.Select(static v => v.Layer).ToArray();
        var clips = union.Values.Select(static v => v.Clips.ToArray()).ToArray();

        var rasterStart = Stopwatch.GetTimestamp();
        using var raster = new SKBitmap(widthPx, heightPx, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(raster))
        {
            canvas.Clear(SKColors.Transparent);
            DrawPlan(
                canvas, layers, clips, denominator!.Value,
                fullMinX, fullMinY, fullMaxX, fullMaxY,
                widthPx, heightPx, gutterPx, TileGrid.ResolutionForBand(band), adjacentWorlds: false);
            canvas.Flush();
        }

        S100Diag.Telemetry.MetatileRasterizeDuration.Record(Stopwatch.GetElapsedTime(rasterStart).TotalMilliseconds);
        foreach (var key in keys)
        {
            var left = gutterPx - ApronPx + (key.X - xMin) * corePx;
            var top = gutterPx - ApronPx + (key.Y - yMin) * corePx;
            using var bitmap = CropStored(raster, left, top, tilePx);
            result.Add(key, SKImage.FromBitmap(bitmap));
        }

        S100Diag.Telemetry.MetatileTiles.Record(keys.Count);
        S100Diag.Telemetry.MetatileJobs.Add(1);
        return true;
    }

    /// <summary>
    /// Draws the base plane of <paramref name="layers"/>, in order, over the
    /// world box onto a raster of <paramref name="widthPx"/> ×
    /// <paramref name="heightPx"/>: each layer clipped by its finer coverages
    /// (anti-aliased, as the per-layer screen clip was) and at its opacity, at
    /// <paramref name="classDenominator"/> or its own out-of-band cap,
    /// whichever is finer.
    /// </summary>
    private static void DrawPlan(
        SKCanvas canvas, IReadOnlyList<ChartLayerState> layers, IReadOnlyList<SlotClip[]> clips, double classDenominator,
        double fullMinX, double fullMinY, double fullMaxX, double fullMaxY,
        int widthPx, int heightPx, int gutterPx, double bandResolution, bool adjacentWorlds)
    {
        var (minLon, minLat) = WebMercator.ToLonLat(fullMinX, fullMinY, clampLatitude: false);
        var (maxLon, maxLat) = WebMercator.ToLonLat(fullMaxX, fullMaxY, clampLatitude: false);
        var renderer = new SkiaDisplayListRenderer
        {
            Background = SceneRgbaColor.Transparent,
            HonorScaleVisibility = true,
            EnableSeamWrap = false,
        };

        // World to raster pixels, for the clip paths.
        var clipView = new ViewState(
            (fullMinX + fullMaxX) * 0.5,
            (fullMinY + fullMaxY) * 0.5,
            (fullMaxX - fullMinX) / widthPx,
            0,
            widthPx,
            heightPx);

        for (var i = 0; i < layers.Count; i++)
        {
            var layer = layers[i];
            var ops = layer.Index.Query(fullMinX, fullMinY, fullMaxX, fullMaxY);
            var hasAdjacent = adjacentWorlds && HasAdjacentWorldOps(layer.Index, fullMinX, fullMinY, fullMaxX, fullMaxY);
            if (ops.Count == 0 && !hasAdjacent)
            {
                continue;
            }

            var viewport = new CoreViewport
            {
                MinLatitude = minLat,
                MaxLatitude = maxLat,
                MinLongitude = minLon,
                MaxLongitude = maxLon,
                WidthPixels = widthPx,
                HeightPixels = heightPx,
                ScaleDenominator = layer.Cap is double cap ? Math.Min(classDenominator, cap) : classDenominator,
            };

            var saves = canvas.Save();
            try
            {
                foreach (var clip in clips[i])
                {
                    using var path = new SKPath { FillType = SKPathFillType.EvenOdd };
                    CoverageClip.AddGeometry(path, clip.Coverage.Coverage, clipView with { CenterX = clipView.CenterX - clip.OffsetX });
                    if (path.PointCount > 0)
                    {
                        canvas.ClipPath(path, SKClipOperation.Difference, antialias: true);
                    }
                }

                if (layer.Opacity < 1.0)
                {
                    using var paint = new SKPaint { Color = SKColors.White.WithAlpha((byte)Math.Round(layer.Opacity * 255)) };
                    canvas.SaveLayer(paint);
                }

                if (ops.Count > 0)
                {
                    renderer.RenderOnto(canvas, new VectorScene(ops), viewport);
                }

                if (hasAdjacent)
                {
                    DrawAdjacentWorldOps(
                        renderer, canvas, widthPx, layer.Index, viewport, gutterPx,
                        bandResolution, fullMinX, fullMinY, fullMaxX, fullMaxY);
                }
            }
            finally
            {
                canvas.RestoreToCount(saves);
            }
        }
    }

    /// <summary>
    /// Test-only seam: the tiles <paramref name="owner"/>'s last live frame
    /// wanted on screen, with their scale classes and stack slices.
    /// </summary>
    internal static IReadOnlyList<TileKey> CurrentVisibleForTest(object owner)
    {
        if (!States.TryGetValue(owner, out var state))
        {
            return [];
        }

        lock (state.Sync)
        {
            return [.. state.CurrentVisible];
        }
    }

    // ---------------------------------------------------------------------
    // Types.

    /// <summary>
    /// An overlay plane with its anchor index and the render-thread scratch
    /// that scopes it to the view each frame without allocating.
    /// </summary>
    internal sealed class OverlaySource
    {
        public OverlaySource(VectorScene scene, OverlaySpatialIndex? index = null)
        {
            Scene = scene;
            Index = index ?? new OverlaySpatialIndex(scene);
            ScopedScene = new VectorScene(Candidates);
        }

        public VectorScene Scene { get; }

        public OverlaySpatialIndex Index { get; }

        public List<int> QueryScratch { get; } = [];

        public List<PaintOp> Candidates { get; } = [];

        public VectorScene ScopedScene { get; }
    }

    /// <summary>A stack resolved against its layers' bound scenes.</summary>
    internal sealed record ResolvedChartStack(long Version, ChartLayerState[] Layers, WorldRect? Extent);

    /// <summary>One resolved layer of a chart stack.</summary>
    internal sealed class ChartLayerState(
        int position,
        object source,
        VectorScene baseScene,
        BaseSpatialIndex index,
        OverlaySource? overlay,
        string key,
        bool stable,
        FinerCoverage[] finer,
        double opacity,
        double minResolution,
        double maxResolution)
    {
        public int Position { get; } = position;

        public object Source { get; } = source;

        public VectorScene Base { get; } = baseScene;

        public BaseSpatialIndex Index { get; } = index;

        public (double MinX, double MinY, double MaxX, double MaxY)? Bounds { get; } = index.Bounds;

        public OverlaySource? Overlay { get; } = overlay;

        public double? Cap { get; } = baseScene.ScaleMinimumCap;

        /// <summary>The scene key the slice hashes: stable across sessions when <see cref="Stable"/>.</summary>
        public string Key { get; } = key;

        public bool Stable { get; } = stable;

        public FinerCoverage[] Finer { get; } = finer;

        public double Opacity { get; } = opacity;

        public double MinResolution { get; } = minResolution;

        public double MaxResolution { get; } = maxResolution;
    }

    /// <summary>A finer coverage clipping a layer in a slot, at a world-copy offset.</summary>
    internal readonly record struct SlotClip(FinerCoverage Coverage, double OffsetX);

    /// <summary>
    /// What one slot draws at one live resolution: the stack layers in order,
    /// the clips of each, the union of their ops' scale limits in the slot,
    /// and the slice that keys it.
    /// </summary>
    internal sealed record SlotPlan(
        ChartLayerState[] Layers,
        SlotClip[][] Clips,
        double[] Thresholds,
        UInt128 Slice,
        bool Persistable);

    /// <summary>The stack layers with ops in a slot (plus gutter), with their ops' scale limits there.</summary>
    internal sealed record SlotContent(int[] Layers, double[][] Thresholds);

    internal readonly record struct PlanMemoEntry(double Resolution, ulong CapSignature, SlotPlan? Plan);
}
