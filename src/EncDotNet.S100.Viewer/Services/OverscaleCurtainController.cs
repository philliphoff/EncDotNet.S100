using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Rendering.Engine;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Keeps the on-chart overscale "curtain" overlay (issue #441, S-52 / S-101
/// <c>AP(OVERSC01)</c> Form A) in sync with the live viewport and the cells
/// its map draws: a subtle vertical-line pattern is painted over the region of
/// each cell that is currently displayed beyond its compilation scale. Each
/// map of a split reads its own session, so a cell hidden in one pane has no
/// curtain there (split map step 2 §2.6).
/// </summary>
/// <remarks>
/// <para>
/// The curtain <em>region</em> geometry depends only on the set of loaded cells
/// and the viewport <em>resolution</em> (zoom) — never on pan or rotation —
/// because <see cref="OverscaleCurtain.ComputeRegions"/> works in world
/// coordinates (EPSG:3857) and <see cref="OverscaleCurtainRenderer"/>
/// re-projects and clips the fill per frame. The controller therefore caches the
/// last resolution and skips recomputation on a pure pan, recomputing only when
/// the zoom changes, the session's layers change, or the
/// mariner toggles <see cref="ViewerSettingsService.ShowOverscaleIndication"/>.
/// Because a zoom leaves the cells unchanged, the per-cell regions are
/// memoised in an <see cref="OverscaleCurtainRegionCache"/>, so a zoom step
/// only re-evaluates which cells are overscaled rather than re-running the
/// polygon overlay (issue #691).
/// </para>
/// <para>
/// The overscale factor per cell is derived from its compilation-scale
/// denominator and the viewport resolution at the cell's latitude — identical to
/// the status-bar overscale pill (<see cref="OverscaleEvaluator"/>) so the two
/// indications always agree.
/// </para>
/// </remarks>
internal sealed class OverscaleCurtainController : IDisposable
{
    /// <summary>
    /// The overlay layer's name. Kept stable across the reusable-layer extraction
    /// (rather than the reusable default) so any name-based lookup or test keeps
    /// matching; hoisted to a constant so the two can't drift.
    /// </summary>
    private const string OverlayLayerName = "Overscale Curtain";

    private readonly IMapLayerCollection _layers;
    private readonly IMapDatasetSession _session;
    private readonly IMapViewportNotifier _viewport;
    private readonly ViewerSettingsService _settings;
    private readonly Action<Action> _marshal;
    private readonly Action<Action> _coalesce;
    private readonly S100OverscaleCurtainLayer _curtain;
    private readonly OverscaleCurtainRegionCache _regions = new();
    private double _lastResolution = double.NaN;
    private int _rebuildPending;
    private bool _disposed;

    /// <summary>
    /// Creates and attaches the controller. The map host must already be
    /// initialised so the curtain overlay lands above the chart slice.
    /// </summary>
    /// <param name="layers">Target map layer collection.</param>
    /// <param name="session">The map's dataset session, whose drawn cells get a curtain.</param>
    /// <param name="viewport">Publishes viewport (resolution) changes.</param>
    /// <param name="settings">Settings service supplying the on/off toggle.</param>
    /// <param name="marshal">Runs work on the UI thread (the app passes <see cref="UiThread.RunOrPost"/>).</param>
    /// <param name="coalesce">
    /// Queues work on the UI thread, even from it (the app passes
    /// <see cref="UiThread.Post"/>): a burst of layer changes, such as a load,
    /// rebuilds once.
    /// </param>
    public OverscaleCurtainController(
        IMapLayerCollection layers,
        IMapDatasetSession session,
        IMapViewportNotifier viewport,
        ViewerSettingsService settings,
        Action<Action> marshal,
        Action<Action> coalesce)
    {
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(viewport);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(marshal);
        ArgumentNullException.ThrowIfNull(coalesce);

        _layers = layers;
        _session = session;
        _viewport = viewport;
        _settings = settings;
        _marshal = marshal;
        _coalesce = coalesce;

        _curtain = new S100OverscaleCurtainLayer(name: OverlayLayerName);

        _session.LayersChanged += OnLayersChanged;
        _viewport.ViewportChanged += OnViewportChanged;
        _settings.OverscaleIndicationChanged += OnToggleChanged;

        if (_viewport.Current is { } current)
            _lastResolution = current.MercatorResolution;

        _marshal(() =>
        {
            _layers.AddOverlayLayer(_curtain.Layer);
            Rebuild();
        });
    }

    private void OnViewportChanged(object? sender, MapViewportSnapshot snapshot)
    {
        // Region geometry is pan/rotation-invariant; only a zoom (resolution)
        // change alters which cells are overscaled and by how much.
        if (ResolutionsEqual(snapshot.MercatorResolution, _lastResolution))
            return;

        _lastResolution = snapshot.MercatorResolution;
        _marshal(Rebuild);
    }

    // A dataset added, removed, rendered, hidden or shown in this map. The
    // session can raise this off the UI thread.
    private void OnLayersChanged(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _rebuildPending, 1) == 1)
            return;
        _coalesce(() =>
        {
            Volatile.Write(ref _rebuildPending, 0);
            Rebuild();
        });
    }

    private void OnToggleChanged() => _marshal(Rebuild);

    private void Rebuild()
    {
        if (_disposed) return;

        IReadOnlyList<OverscaleRegion> regions = Array.Empty<OverscaleRegion>();

        if (_settings.ShowOverscaleIndication && !double.IsNaN(_lastResolution))
        {
            var cells = MapOverscaleCells.Of(_session);
            if (cells.Count > 0)
                regions = _regions.ComputeRegions(cells, _lastResolution);
        }

        _curtain.Show(regions);
    }

    private static bool ResolutionsEqual(double a, double b)
    {
        if (double.IsNaN(a) || double.IsNaN(b))
            return double.IsNaN(a) && double.IsNaN(b);

        // Treat sub-per-mille resolution changes as "same zoom" so incidental
        // navigator jitter during a pan does not trigger a rebuild.
        return Math.Abs(a - b) <= Math.Abs(b) * 1e-3;
    }

    /// <summary>Detaches the overlay layer and unsubscribes. Idempotent.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _session.LayersChanged -= OnLayersChanged;

        _viewport.ViewportChanged -= OnViewportChanged;
        _settings.OverscaleIndicationChanged -= OnToggleChanged;

        _marshal(() => _layers.RemoveOverlayLayer(_curtain.Layer));
    }
}
