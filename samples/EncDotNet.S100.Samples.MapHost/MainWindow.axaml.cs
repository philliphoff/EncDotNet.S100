using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Renderers.Mapsui.Avalonia;
using Mapsui;

namespace EncDotNet.S100.Samples.MapHost;

/// <summary>
/// The whole sample. A minimal, non-Viewer Avalonia window that embeds the
/// reusable S-100 Mapsui extension: it attaches a <c>Map.AddS100(...)</c>
/// session to a bare <see cref="Map"/>, renders a bundled S-101 cell, and wires
/// its toolbar and pointer input straight to the <see cref="IS100MapSession"/>
/// and the Avalonia pick adapter - no Viewer types involved.
/// </summary>
/// <remarks>
/// <para>
/// Read the constructor top-to-bottom as the integration recipe: (1) compose a
/// session over a map, (2) bind that map to the live control and attach the
/// Avalonia adapter, (3) add the optional reusable overlays. The button and
/// pointer handlers below then call the session directly - each one is a small
/// worked example of one API (load, presentation, visibility, zoom, pick).
/// </para>
/// <para>
/// <b>Threading contract.</b> The session mutates <c>Map.Layers</c> and installs
/// generated layers on the calling synchronization context, so every session
/// call here runs on Avalonia's UI thread (all these handlers do). The only
/// work that leaves the UI thread is the pick, which the adapter dispatches for
/// us. Normal pan / zoom / rotation stay entirely with <c>Map.Navigator</c> and
/// the Mapsui control - this sample never reimplements navigation.
/// </para>
/// </remarks>
public partial class MainWindow : Window
{
    private readonly BundledDatasetProcessorFactory _processorFactory;
    private readonly IS100MapSession _session;
    private readonly AvaloniaMapsuiMapAdapter _adapter;
    private readonly S100PickHighlightLayer _highlight;
    private readonly S100DatasetExtentIndicatorLayer _extentIndicator;
    private readonly Action _redraw;

    // Whatever was installed on the process-global redraw hooks before us, so we
    // restore rather than clobber on close (see the constructor / OnClosed).
    private readonly Action? _prevSnapshotRedraw;
    private readonly Action? _prevSceneRedraw;
    private readonly Action? _prevTileRedraw;

    private readonly string _cellPath;

    // The presentation is immutable; palette buttons apply a modified copy.
    private MapPresentationState _presentation = MapPresentationState.Default;
    private MapDatasetId? _loadedDatasetId;

    public MainWindow()
    {
        InitializeComponent();

        _cellPath = System.IO.Path.Combine(AppContext.BaseDirectory, "sample-cell.000");

        // ── 1. Compose an S-100 session over a bare Mapsui map ──────────────
        //
        // BundledDatasetProcessorFactory (from the EncDotNet.S100 convenience
        // package) is the one-call replacement for hand-wiring the portrayal /
        // feature catalogues, Lua engine, CRS factory, and product registry: it
        // returns an IDatasetProcessorFactory seeded with the official bundled
        // catalogues for every product. The session needs it to turn a file path
        // into a rendered dataset. A host that only ever adds pre-built processors
        // via IS100MapSession.AddDatasetAsync would not need one.
        _processorFactory = BundledDatasetProcessorFactory.Create();

        // CRS = EPSG:3857: the reusable renderer projects every dataset to Web
        // Mercator, so the map must declare that CRS for the layers to line up
        // and for the pick adapter to convert pointer pixels back to WGS-84.
        var map = new Map { CRS = "EPSG:3857" };

        // Map.AddS100 is THE entry point. It returns an IS100MapSession that owns
        // the S-100 layer bands, processors, renderer, and navigation surface;
        // disposing it (see OnClosed) releases all of them. Ownership lives only
        // on the returned instance - it is not stashed in a static table or
        // Map.Tag - so the host holds and disposes it explicitly.
        //   * crsTransformFactory: required. The reusable assembly ships no CRS
        //     implementation, so the host supplies one (ProjNet here).
        //   * options.DatasetPipelineFactory: enables Datasets.LoadAsync(path).
        _session = map.AddS100(
            new ProjNetCrsTransformFactory(),
            new S100MapsuiOptions { DatasetPipelineFactory = _processorFactory });

        // ── 2. Bind the map to the live control and attach the Avalonia adapter ─
        //
        // The session is UI-framework-neutral. AvaloniaMapsuiMapAdapter is the
        // bridge to *this* framework: it converts pointer pixels to geographic
        // pick queries, requests live redraws, and captures snapshots. It borrows
        // the control and map (disposing it does not dispose them).
        MapControl.Map = map;
        _adapter = AvaloniaMapsuiMapAdapter.Attach(MapControl);
        _redraw = _adapter.RequestRedraw;

        // Redraw after async re-renders. The S-100 vector renderers rasterise
        // cached / scene / tile output on background threads and signal
        // completion only through these process-global hooks. A host that does
        // not set them sees stale content after an in-place re-render (e.g. a
        // palette change) until an unrelated pan/zoom triggers Mapsui's own
        // refresh - so point them at our redraw. Because they are process-global
        // we first capture whatever was installed and restore it in OnClosed,
        // rather than clobbering another host in the same process. (Issue #512
        // tracks replacing these statics with a per-session seam that would make
        // all of this unnecessary.)
        _prevSnapshotRedraw = S100VectorSnapshotRenderer.RequestRedraw;
        _prevSceneRedraw = S100VectorSceneRenderer.RequestRedraw;
        _prevTileRedraw = S100VectorTileRenderer.RequestRedraw;
        S100VectorSnapshotRenderer.RequestRedraw = _redraw;
        S100VectorSceneRenderer.RequestRedraw = _redraw;
        S100VectorTileRenderer.RequestRedraw = _redraw;

        // ── 3. Add the optional reusable overlays ──────────────────────────
        //
        // Both are self-contained Mapsui layers that depend only on Mapsui (not
        // on the session, a catalogue, a palette, or a view model). Add each
        // Layer to Map.Layers once, then drive it with Show/Clear. Order matters
        // only for z-order; the highlight goes on top of the extent indicator.
        //   * extent indicator: a dashed border around the cell, revealed by
        //     Mapsui exactly when the viewport zooms out past the cell's content
        //     cutoff, giving the mariner a target to zoom toward.
        //   * pick highlight: outlines the topmost picked feature.
        _extentIndicator = new S100DatasetExtentIndicatorLayer();
        map.Layers.Add(_extentIndicator.Layer);
        _highlight = new S100PickHighlightLayer();
        map.Layers.Add(_highlight.Layer);

        // Pick on release so a click reports the S-100 features underneath while
        // ordinary drag-pan / wheel-zoom stay with the Mapsui control.
        MapControl.PointerReleased += OnMapPointerReleased;
        Closed += OnClosed;
    }

    private async void OnLoad(object? sender, RoutedEventArgs e)
    {
        if (_loadedDatasetId is not null)
        {
            return;
        }

        try
        {
            SetBusy("Loading and rendering the S-101 cell…");

            // Datasets.LoadAsync is the one-call load path: it detects the
            // product spec from the file, builds a processor with the configured
            // DatasetPipelineFactory, registers a renderer-neutral dataset, and
            // renders it - returning a stable MapDatasetId the host uses for
            // every later operation (visibility, ordering, zoom, removal). Await
            // it on the UI thread; first render of an S-101 cell runs the Part 9A
            // Lua portrayal, so it is not instant. Load *policy* (duplicate
            // suppression, default visibility, prompts) stays with the host.
            _loadedDatasetId = await _session.Datasets.LoadAsync(_cellPath);

            // ZoomToDataset is an optional convenience over Map.Navigator; the
            // control is laid out by now, so the viewport frames the cell.
            // Navigation is otherwise entirely Mapsui's - the session adds only
            // this framing helper, never its own pan/zoom.
            _session.ZoomToDataset(_loadedDatasetId.Value);
            UpdateExtentIndicator();
            _adapter.RequestRedraw();

            SetLoadedUi(true);
            Status($"Loaded {_loadedDatasetId} - drag to pan, wheel to zoom, click to pick.");
        }
        catch (Exception ex)
        {
            _loadedDatasetId = null;
            SetLoadedUi(false);
            Status($"Load failed: {ex.Message}");
        }
    }

    private void OnUnload(object? sender, RoutedEventArgs e)
    {
        if (_loadedDatasetId is not { } id)
        {
            return;
        }

        _session.RemoveDataset(id);
        _highlight.Clear();
        _extentIndicator.Clear();
        _loadedDatasetId = null;
        SetLoadedUi(false);
        _adapter.RequestRedraw();
        Status("Cell unloaded. Click 'Load cell' to render it again.");
    }

    private void OnZoomToDataset(object? sender, RoutedEventArgs e)
    {
        if (_loadedDatasetId is { } id)
        {
            _session.ZoomToDataset(id);
            _adapter.RequestRedraw();
        }
    }

    private async void OnPaletteDay(object? sender, RoutedEventArgs e) => await ApplyPaletteAsync(PaletteType.Day);

    private async void OnPaletteDusk(object? sender, RoutedEventArgs e) => await ApplyPaletteAsync(PaletteType.Dusk);

    private async void OnPaletteNight(object? sender, RoutedEventArgs e) => await ApplyPaletteAsync(PaletteType.Night);

    private async Task ApplyPaletteAsync(PaletteType palette)
    {
        try
        {
            // MapPresentationState is immutable: the host keeps the current
            // state and applies a modified copy (WithPalette / WithSymbolScale /
            // ... also cover text scale, ECDIS display, and mariner settings).
            // SetPresentationAsync applies it map-wide and re-renders every
            // dataset; awaiting it is how the host knows the new state is in
            // effect. State is applied explicitly - there are no viewer-style
            // "refresh" trigger events to raise.
            _presentation = _presentation.WithPalette(palette);
            await _session.SetPresentationAsync(_presentation);
            _adapter.RequestRedraw();
            Status($"Palette: {palette}");
        }
        catch (Exception ex)
        {
            Status($"Palette change failed: {ex.Message}");
        }
    }

    private void OnToggleVisible(object? sender, RoutedEventArgs e)
    {
        if (_loadedDatasetId is not { } id)
        {
            return;
        }

        // SetVisible toggles only whether the dataset paints. It is distinct
        // from SetActive, which controls cross-product (S-98) composition and
        // whether the dataset participates in picking; a host can hide a dataset
        // visually while keeping it active, or vice versa. SetOpacity and
        // SetOrder round out the per-dataset controls.
        var isVisible = VisibleToggle.IsChecked ?? true;
        _session.SetVisible(id, isVisible);
        UpdateExtentIndicator();
        _adapter.RequestRedraw();
        Status(isVisible ? "Dataset shown." : "Dataset hidden.");
    }

    /// <summary>
    /// Rebuilds the extent-indicator overlay from the loaded dataset's snapshot.
    /// The dashed border is gated by the cell's content cutoff, so Mapsui reveals
    /// it only when the viewport zooms out past the point the cell stops drawing.
    /// </summary>
    /// <remarks>
    /// Only shown when the dataset reports a content cutoff
    /// (<c>ContentMaxVisibleResolution</c>): with no cutoff the cell draws at
    /// every zoom, so there is no "scaled out" state to indicate and an
    /// always-on border would just be noise. (Passing <c>0</c> to
    /// <see cref="S100DatasetExtentIndicator"/> is reserved for a different case
    /// the module documents - a catalogue footprint whose cell is not yet loaded.)
    /// </remarks>
    private void UpdateExtentIndicator()
    {
        if (_loadedDatasetId is { } id
            && _session.GetDataset(id) is { Extent: { } extent, ContentMaxVisibleResolution: { } cutoff }
            && (VisibleToggle.IsChecked ?? true))
        {
            _extentIndicator.Show(new[]
            {
                new S100DatasetExtentIndicator(extent, cutoff),
            });
        }
        else
        {
            _extentIndicator.Clear();
        }
    }

    private async void OnMapPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_loadedDatasetId is null)
        {
            return;
        }

        var point = e.GetPosition(MapControl);
        try
        {
            // Picking is split into two layers so the reusable core stays
            // UI-free. session.Query.PickAsync answers a purely *geographic*
            // query (lat/lon + radius) - no notion of pixels or gestures.
            // PickAtScreenAsync is the Avalonia adapter over it: it reads the
            // live viewport on the UI thread to turn this pointer pixel into that
            // geographic query (and to capture the current resolution so cells
            // scaled out at this zoom are excluded), then runs the pick off the
            // UI thread. Results are ranked topmost-first by the S-98 paint
            // stack. A non-Avalonia host would translate its own pointer and call
            // session.Query.PickAsync directly.
            var picks = await _adapter.PickAtScreenAsync(_session.Query, point.X, point.Y);
            if (picks.Count == 0)
            {
                _highlight.Clear();
                Status("No S-100 feature at that point.");
                return;
            }

            var top = picks[0];
            _highlight.Show(top);
            _adapter.RequestRedraw();
            var kind = top.IsCoverage ? "coverage" : (top.FeatureType ?? "feature");
            Status($"Picked {kind} in {top.DatasetId} ({picks.Count} hit(s), topmost {top.DistanceMeters:F0} m).");
        }
        catch (Exception ex)
        {
            Status($"Pick failed: {ex.Message}");
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        // Dispose everything: the session releases its processors, layers,
        // subscriptions, and caches; the adapter detaches; the catalogue host
        // frees its parse caches.
        MapControl.PointerReleased -= OnMapPointerReleased;

        // Restore the process-global redraw hooks to whatever preceded us, but
        // only where ours is still the installed one (guarding against a newer
        // handler having taken over).
        if (ReferenceEquals(S100VectorSnapshotRenderer.RequestRedraw, _redraw))
        {
            S100VectorSnapshotRenderer.RequestRedraw = _prevSnapshotRedraw;
        }

        if (ReferenceEquals(S100VectorSceneRenderer.RequestRedraw, _redraw))
        {
            S100VectorSceneRenderer.RequestRedraw = _prevSceneRedraw;
        }

        if (ReferenceEquals(S100VectorTileRenderer.RequestRedraw, _redraw))
        {
            S100VectorTileRenderer.RequestRedraw = _prevTileRedraw;
        }

        _adapter.Dispose();
        _session.Dispose();
        _processorFactory.Dispose();
    }

    private void SetBusy(string message)
    {
        LoadButton.IsEnabled = false;
        Status(message);
    }

    private void SetLoadedUi(bool loaded)
    {
        LoadButton.IsEnabled = !loaded;
        UnloadButton.IsEnabled = loaded;
        ZoomButton.IsEnabled = loaded;
        DayButton.IsEnabled = loaded;
        DuskButton.IsEnabled = loaded;
        NightButton.IsEnabled = loaded;
        VisibleToggle.IsEnabled = loaded;
        if (loaded)
        {
            VisibleToggle.IsChecked = true;
        }
    }

    private void Status(string message) => StatusText.Text = message;
}
