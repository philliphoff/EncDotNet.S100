using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.Viewer.Catalogs;
using EncDotNet.S100.Viewer.Diagnostics;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui.Layers;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Default <see cref="IDatasetLoaderService"/> implementation. Owns the
/// dataset pipeline factory, the per-entry processor + layer maps, and
/// drives all map mutations through an <see cref="IMapHost"/>.
/// </summary>
internal sealed class DatasetLoaderService : IDatasetLoaderService
{
    private readonly ViewerSettings _settings;
    private readonly PortrayalCatalogueManager _catalogueManager;
    private readonly PortrayalCatalogueSeeder _catalogueSeeder;
    private readonly IRecentFilesService _recentFiles;
    private readonly S128DatasetCatalogSource _s128CatalogSource;
    private readonly SettingsViewModel _settingsVm;
    private readonly GlobalTimeService _globalTime;
    private readonly EcdisDisplayState _ecdisDisplay;
    private readonly IMarinerSettingsProvider _marinerSettings;
    private readonly IToastService _toasts;

    private readonly Dictionary<DatasetEntry, IDatasetProcessor> _processors = new();
    private readonly Dictionary<DatasetEntry, IReadOnlyList<ILayer>> _entryLayers = new();
    /// <summary>
    /// Per-entry sub-layer keys, parallel by index to
    /// <see cref="_entryLayers"/>. Null when the processor did not
    /// supply per-layer names (single-layer products).
    /// </summary>
    private readonly Dictionary<DatasetEntry, IReadOnlyList<string>?> _entryLayerKeys = new();
    private readonly HashSet<DatasetEntry> _subscribedEntries = new();
    /// <summary>
    /// Canonical paint-order of dataset entries. Mirrors the order the
    /// user sees in the Datasets panel; index 0 is the TOP of the
    /// paint stack (drawn last, on top of every other dataset) — the
    /// Photoshop/QGIS convention. Mutated only by the loader so
    /// palette/time re-renders don't disturb user-driven ordering.
    /// </summary>
    private readonly List<DatasetEntry> _entryOrder = new();
    private readonly ReadOnlyDictionary<DatasetEntry, IDatasetProcessor> _processorsView;
    private readonly ReadOnlyDictionary<DatasetEntry, IReadOnlyList<ILayer>> _entryLayersView;

    private IMapHost? _mapHost;
    private DatasetPipelineFactory? _pipelineFactory;

    // Coalesce slider scrubs into a single render pass after the user has
    // paused for ~100 ms. Each new SetCurrentTime cancels the in-flight
    // debounce + render so we never queue dozens of stale renders behind
    // the latest mouse position.
    private static readonly TimeSpan ScrubDebounceWindow = TimeSpan.FromMilliseconds(100);
    private CancellationTokenSource? _scrubCts;

    public DatasetLoaderService(
        ViewerSettings settings,
        PortrayalCatalogueManager catalogueManager,
        PortrayalCatalogueSeeder catalogueSeeder,
        IRecentFilesService recentFiles,
        S128DatasetCatalogSource s128CatalogSource,
        SettingsViewModel settingsVm,
        GlobalTimeService globalTime,
        EcdisDisplayState ecdisDisplay,
        IMarinerSettingsProvider marinerSettings,
        IToastService toasts)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(catalogueManager);
        ArgumentNullException.ThrowIfNull(catalogueSeeder);
        ArgumentNullException.ThrowIfNull(recentFiles);
        ArgumentNullException.ThrowIfNull(s128CatalogSource);
        ArgumentNullException.ThrowIfNull(settingsVm);
        ArgumentNullException.ThrowIfNull(globalTime);
        ArgumentNullException.ThrowIfNull(ecdisDisplay);
        ArgumentNullException.ThrowIfNull(marinerSettings);
        ArgumentNullException.ThrowIfNull(toasts);

        _settings = settings;
        _catalogueManager = catalogueManager;
        _catalogueSeeder = catalogueSeeder;
        _recentFiles = recentFiles;
        _s128CatalogSource = s128CatalogSource;
        _settingsVm = settingsVm;
        _globalTime = globalTime;
        _ecdisDisplay = ecdisDisplay;
        _marinerSettings = marinerSettings;
        _toasts = toasts;

        _processorsView = new ReadOnlyDictionary<DatasetEntry, IDatasetProcessor>(_processors);
        _entryLayersView = new ReadOnlyDictionary<DatasetEntry, IReadOnlyList<ILayer>>(_entryLayers);

        _globalTime.CurrentTimeChanged += t => _ = ReRenderAtTimeAsync(t, CancellationToken.None);
    }

    public IReadOnlyDictionary<DatasetEntry, IDatasetProcessor> Processors => _processorsView;
    public IReadOnlyDictionary<DatasetEntry, IReadOnlyList<ILayer>> EntryLayers => _entryLayersView;

    public event Action<DatasetEntry>? DatasetLoaded;

    /// <summary>
    /// Raised whenever the loader wants to surface a status message
    /// (loading, errors, time-step progress, etc.). The window forwards
    /// these to <see cref="MainViewModel.StatusText"/>.
    /// </summary>
    public event Action<string?>? StatusChanged;

    private void SetStatus(string? text) => StatusChanged?.Invoke(text);

    public void Initialize(IMapHost host, ViewerCommandSettings? options)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (_mapHost is not null)
            throw new InvalidOperationException("DatasetLoaderService has already been initialized.");

        _mapHost = host;

        var transientFcPaths = _catalogueSeeder.Seed(options);

        _pipelineFactory = new DatasetPipelineFactory(
            _catalogueManager,
            new MoonSharpLuaEngine(),
            new ProjNetCrsTransformFactory(),
            spec => transientFcPaths.TryGetValue(spec, out var p) ? File.OpenRead(p)
                  : _settings.FeatureCataloguePaths.TryGetValue(spec, out var sp) ? File.OpenRead(sp)
                  : Specifications.Specification.TryOpenFeatureCatalogue(spec));

        // Re-render every loaded dataset whenever the user changes a setting
        // that affects portrayal output (palette / display scale). These are
        // wired here, not in the window, so the loader fully owns its
        // re-render lifecycle.
        _settingsVm.PaletteChanged += palette => _ = ReRenderAllAsync();
        _settingsVm.DisplayScaleChanged += () => _ = ReRenderAllAsync();
        _ecdisDisplay.Changed += () => _ = ReRenderAllAsync();
        _marinerSettings.Changed += m => _ = ReRenderAllAsync();
    }

    public async Task LoadAsync(DatasetEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        EnsureInitialized();

        using var __cmd = ViewerObservability.BeginCommand("dataset.open");

        // Exchange-set entries carry an explicit ProductSpec from the
        // catalogue and never require path-based detection or recent-
        // files updates (the relative path inside a ZIP is not
        // meaningful as a recent-file entry).
        var fromExchangeSet = entry.IsFromExchangeSet;

        string? spec;
        if (fromExchangeSet)
        {
            spec = entry.ProductSpec;
        }
        else
        {
            spec = DatasetPipelineFactory.DetectProductSpec(entry.FilePath);
            if (spec is null)
            {
                SetStatus(string.Format(Strings.Status_UnrecognizedFileType, Path.GetExtension(entry.FilePath)));
                _toasts.ShowWarning(Strings.Toast_Warning,
                    string.Format(Strings.Status_UnrecognizedFileType, Path.GetExtension(entry.FilePath)));
                return;
            }
        }

        // S-104 ships a built-in portrayal catalogue.
        // S-57 datasets are translated to S-101 in-memory and rendered with the S-101 portrayal catalogue.
        var requiredCatalogue = spec == "S-57" ? "S-101" : spec;
        if (spec != "S-104" && !_catalogueManager.HasCatalogue(requiredCatalogue))
        {
            SetStatus(string.Format(Strings.Status_SelectPortrayalCatalogue, requiredCatalogue));
            _toasts.ShowWarning(Strings.Toast_Warning,
                string.Format(Strings.Status_SelectPortrayalCatalogue, requiredCatalogue));
            return;
        }

        SetStatus(string.Format(Strings.Status_LoadingFile, entry.DisplayName));

        try
        {
            var processor = await Task.Run(() => fromExchangeSet
                ? _pipelineFactory!.CreateProcessor(entry.Source!, entry.RelativePath!, spec)
                : _pipelineFactory!.CreateProcessor(entry.FilePath));
            _processors[entry] = processor;

            // Surface S-128 catalogues into the Dataset Catalog panel.
            if (processor is S128DatasetProcessor s128)
            {
                _s128CatalogSource.AddDataset(entry.DisplayName, s128.Dataset);
            }

            // Discover time samples from the processor (S-104, S-111, S-411).
            // The adapter wraps the processor in a spec-agnostic view used
            // by the global time slider.
            var adapter = TimeAwareDatasetAdapter.TryCreate(processor, () => entry.CurrentTime);
            if (adapter is not null)
            {
                entry.AvailableTimes = adapter.AvailableTimes;
            }

            // Pick the initial render time. If the global slider already
            // has a clock, snap this dataset to it; otherwise let the
            // processor pick its default (typically the first sample).
            DateTime? initialTime = null;
            if (adapter is not null && _globalTime.CurrentTime is { } globalNow)
                initialTime = adapter.SnapTo(globalNow);

            var initialContext = CreateRenderContext(processor, initialTime);
            var result = await Task.Run(() => processor.Render(initialContext));

            ReplaceLayers(entry, result.Layers.ToList(), result.LayerNames);
            // Exchange-set entries opt out of the per-dataset auto-zoom so
            // the union-extent zoom from `IExchangeSetService` (or the
            // user's manual Zoom-to-Extent toolbar action) wins. Without
            // this, the last-completed dataset would race with the bulk
            // load and "win" the viewport.
            if (!fromExchangeSet)
            {
                _mapHost!.ZoomToExtent(result.Extent);
            }

            entry.IsLoaded = true;
            entry.Info = result.Info;
            entry.CurrentTime = initialTime ?? adapter?.AvailableTimes.FirstOrDefault();
            SetStatus(result.Info);

            // Recent files only makes sense for plain file loads. An
            // exchange-set entry's FilePath is a relative path inside
            // a folder/ZIP source and not openable on its own.
            if (!fromExchangeSet)
            {
                _recentFiles.Add(entry.FilePath);
            }

            // Register with the global time service after the entry's
            // CurrentTime has been set so the first slider snap reflects
            // the actual rendered state.
            if (adapter is not null && adapter.AvailableTimes.Count > 0)
            {
                _globalTime.Register(entry, adapter);
            }

            DatasetLoaded?.Invoke(entry);
        }
        catch (Exception ex)
        {
            SetStatus(string.Format(Strings.Status_Error, ex.Message));
            _toasts.ShowError(Strings.Toast_DatasetError,
                string.Format(Strings.Status_Error, ex.Message));
            Console.Error.WriteLine($"Failed to load {entry.FilePath}:\n{ex}");
        }
    }

    public async Task ReRenderAtTimeAsync(DateTime t, CancellationToken cancellationToken)
    {
        using var __cmd = ViewerObservability.BeginCommand("timeline.scrub");

        // Cancel any in-flight scrub render and start a fresh debounce
        // window. The token passed in is honoured in addition to the
        // internal debounce token so callers can cancel from outside
        // (e.g. on shutdown).
        _scrubCts?.Cancel();
        var localCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _scrubCts = localCts;
        var token = localCts.Token;

        try
        {
            await Task.Delay(ScrubDebounceWindow, token).ConfigureAwait(true);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested) return;

        SetStatus(string.Format(Strings.Status_RenderingAtTime, t));

        foreach (var (entry, adapter) in _globalTime.Adapters.ToArray())
        {
            if (token.IsCancellationRequested) return;
            if (!_processors.TryGetValue(entry, out var proc)) continue;

            var snapped = adapter.SnapTo(t);
            if (snapped == entry.CurrentTime && entry.IsLoaded) continue;

            try
            {
                var context = CreateRenderContext(proc, snapped);
                var result = await Task.Run(() => proc.Render(context), token).ConfigureAwait(true);

                ReplaceLayers(entry, result.Layers.ToList(), result.LayerNames);
                entry.Info = result.Info;
                entry.CurrentTime = snapped;
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to re-render {entry.FilePath} at {t:u}:\n{ex}");
            }
        }
    }

    public async Task ReRenderAllAsync()
    {
        using var __cmd = ViewerObservability.BeginCommand("palette.change");

        var palette = _settingsVm.SelectedPalette;
        SetStatus(string.Format(Strings.Status_SwitchingPalette, palette));

        foreach (var (entry, proc) in _processors.ToArray())
        {
            if (!entry.IsLoaded) continue;

            try
            {
                var context = CreateRenderContext(proc, entry.CurrentTime);

                var result = await Task.Run(() => proc.Render(context));

                ReplaceLayers(entry, result.Layers.ToList(), result.LayerNames);
                entry.Info = result.Info;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to re-render {entry.FilePath} with {palette} palette:\n{ex}");
            }
        }

        SetStatus(string.Format(Strings.Status_PaletteApplied, palette));
        _toasts.ShowSuccess(Strings.Toast_Success,
            string.Format(Strings.Status_PaletteApplied, palette));
    }

    public void RemoveEntry(DatasetEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        RemoveEntryLayers(entry);
        UnsubscribeSubLayers(entry);
        entry.SubLayers.Clear();
        _entryLayerKeys.Remove(entry);
        if (_subscribedEntries.Remove(entry))
            entry.PropertyChanged -= OnEntryPropertyChanged;
        _processors.Remove(entry);
        _entryOrder.Remove(entry);
        _globalTime.Unregister(entry);
        _s128CatalogSource.RemoveDataset(entry.DisplayName);
    }

    public void SetEntryOrder(IReadOnlyList<DatasetEntry> orderedEntries)
    {
        ArgumentNullException.ThrowIfNull(orderedEntries);
        if (_mapHost is null) return;

        // Rebuild the canonical order from the supplied sequence,
        // dropping any entries that are no longer bound to layers
        // (e.g. removed concurrently).
        _entryOrder.Clear();
        foreach (var e in orderedEntries)
        {
            if (_entryLayers.ContainsKey(e))
                _entryOrder.Add(e);
        }
        _mapHost.ReorderDatasetLayers(FlattenLayerOrder());
    }

    private List<ILayer> FlattenLayerOrder()
    {
        // _entryOrder is ordered top-of-stack-first (UI convention).
        // MapsuiMapHost.ReorderDatasetLayers expects bottom-of-stack-
        // first (lower indices in the layer collection are drawn
        // earlier). Reverse here so the top-of-UI entry's layers end
        // up at the highest layer index.
        var list = new List<ILayer>();
        for (int i = _entryOrder.Count - 1; i >= 0; i--)
        {
            if (_entryLayers.TryGetValue(_entryOrder[i], out var ls))
                list.AddRange(ls);
        }
        return list;
    }

    private RenderContext CreateRenderContext(IDatasetProcessor processor, DateTime? timeStep = null)
    {
        var palette = _settingsVm.SelectedPalette;
        var symbolScale = _settingsVm.SymbolScale;
        var textScale = _settingsVm.TextScale;
        var ecdis = _ecdisDisplay.Snapshot();
        var mariner = _marinerSettings.Current;

        return processor switch
        {
            S104DatasetProcessor when timeStep is not null
                => new S104RenderContext(timeStep) { Palette = palette, SymbolScale = symbolScale, TextScale = textScale, EcdisDisplay = ecdis, Mariner = mariner },
            S104DatasetProcessor
                => new S104RenderContext { Palette = palette, SymbolScale = symbolScale, TextScale = textScale, EcdisDisplay = ecdis, Mariner = mariner },
            S111DatasetProcessor when timeStep is not null
                => new S111RenderContext(timeStep) { Palette = palette, SymbolScale = symbolScale, TextScale = textScale, EcdisDisplay = ecdis, Mariner = mariner },
            S111DatasetProcessor
                => new S111RenderContext { Palette = palette, SymbolScale = symbolScale, TextScale = textScale, EcdisDisplay = ecdis, Mariner = mariner },
            S101DatasetProcessor
                => new S101RenderContext { Palette = palette, SymbolScale = symbolScale, TextScale = textScale, EcdisDisplay = ecdis, Mariner = mariner },
            S102DatasetProcessor
                => new S102RenderContext { Palette = palette, SymbolScale = symbolScale, TextScale = textScale, EcdisDisplay = ecdis, Mariner = mariner },
            S122DatasetProcessor
                => new S122RenderContext { Palette = palette, SymbolScale = symbolScale, TextScale = textScale, EcdisDisplay = ecdis, Mariner = mariner },
            S124DatasetProcessor
                => new S124RenderContext { Palette = palette, SymbolScale = symbolScale, TextScale = textScale, EcdisDisplay = ecdis, Mariner = mariner },
            S125DatasetProcessor
                => new S125RenderContext { Palette = palette, SymbolScale = symbolScale, TextScale = textScale, EcdisDisplay = ecdis, Mariner = mariner },
            S127DatasetProcessor
                => new S127RenderContext { Palette = palette, SymbolScale = symbolScale, TextScale = textScale, EcdisDisplay = ecdis, Mariner = mariner },
            S129DatasetProcessor
                => new S129RenderContext { Palette = palette, SymbolScale = symbolScale, TextScale = textScale, EcdisDisplay = ecdis, Mariner = mariner },
            S411DatasetProcessor when timeStep is not null
                => new S411RenderContext(timeStep) { Palette = palette, SymbolScale = symbolScale, TextScale = textScale, EcdisDisplay = ecdis, Mariner = mariner },
            S411DatasetProcessor
                => new S411RenderContext { Palette = palette, SymbolScale = symbolScale, TextScale = textScale, EcdisDisplay = ecdis, Mariner = mariner },
            _ => new S101RenderContext { Palette = palette, SymbolScale = symbolScale, TextScale = textScale, EcdisDisplay = ecdis, Mariner = mariner },
        };
    }

    private void ReplaceLayers(DatasetEntry entry, IReadOnlyList<ILayer> layers, IReadOnlyList<string>? layerKeys)
    {
        bool isFirstLoad = !_entryOrder.Contains(entry);

        RemoveEntryLayers(entry);
        _entryLayers[entry] = layers;
        _entryLayerKeys[entry] = layerKeys;

        // Reconcile sub-layers (don't replace) so existing per-sub-layer
        // visibility / opacity choices survive palette switches and
        // time-scrub re-renders. Sub-layers are matched by stable key.
        ReconcileSubLayers(entry, layerKeys);

        // Re-apply effective display state (parent + sub-layer combined)
        // to the freshly-produced layers. Each ReplaceLayers call creates
        // new ILayer instances that default to Enabled=true / Opacity=1
        // — without this step those defaults silently win.
        ApplyDisplayState(entry);

        // Subscribe lazily on first ReplaceLayers so that property
        // changes raised by the UI propagate to the live ILayer
        // instances. The subscription persists across re-renders.
        if (_subscribedEntries.Add(entry))
            entry.PropertyChanged += OnEntryPropertyChanged;

        foreach (var layer in layers)
        {
            _mapHost!.AddLayer(layer);
        }

        // First-time loads: the new entry goes to the TOP of the
        // canonical order (matching DatasetsViewModel.Add which
        // inserts at index 0). MapsuiMapHost already puts new layers
        // on top of the dataset block by default, so a re-shuffle is
        // only needed for re-renders.
        if (isFirstLoad)
        {
            _entryOrder.Insert(0, entry);
        }
        else
        {
            _mapHost!.ReorderDatasetLayers(FlattenLayerOrder());
        }
    }

    /// <summary>
    /// Brings <see cref="DatasetEntry.SubLayers"/> in line with the
    /// processor's freshly-emitted layer keys. Existing
    /// <see cref="DatasetSubLayer"/> instances are reused (matched by
    /// <see cref="DatasetSubLayer.Key"/>) so user toggles survive
    /// re-renders. Single-layer datasets have an empty SubLayers
    /// collection, which the UI treats as "no disclosure".
    /// </summary>
    private void ReconcileSubLayers(DatasetEntry entry, IReadOnlyList<string>? layerKeys)
    {
        // Single-layer datasets: clear any (stale) sub-layers and bail.
        if (layerKeys is null || layerKeys.Count <= 1)
        {
            if (entry.SubLayers.Count > 0)
            {
                UnsubscribeSubLayers(entry);
                entry.SubLayers.Clear();
            }
            return;
        }

        var existing = entry.SubLayers.ToDictionary(s => s.Key, s => s);
        var seen = new HashSet<string>();
        var orderedNew = new List<DatasetSubLayer>(layerKeys.Count);
        foreach (var key in layerKeys)
        {
            // Suffix-resolve duplicate keys defensively (the contract
            // expects unique keys; this just keeps a runtime collision
            // from corrupting the SubLayers collection).
            var k = key;
            int n = 1;
            while (!seen.Add(k))
            {
                k = $"{key}#{++n}";
            }

            if (existing.TryGetValue(k, out var sub))
            {
                orderedNew.Add(sub);
            }
            else
            {
                var displayName = ResolveSubLayerDisplayName(k);
                sub = new DatasetSubLayer(k, displayName);
                sub.PropertyChanged += OnSubLayerPropertyChanged;
                orderedNew.Add(sub);
            }
        }

        // Drop sub-layers that no longer correspond to any emitted
        // layer (e.g. processor changed shape between renders).
        foreach (var stale in existing.Values.Where(s => !seen.Contains(s.Key)))
        {
            stale.PropertyChanged -= OnSubLayerPropertyChanged;
        }

        entry.SubLayers.Clear();
        foreach (var sub in orderedNew) entry.SubLayers.Add(sub);
    }

    private void UnsubscribeSubLayers(DatasetEntry entry)
    {
        foreach (var s in entry.SubLayers)
            s.PropertyChanged -= OnSubLayerPropertyChanged;
    }

    /// <summary>
    /// Maps stable processor-supplied sub-layer keys to localized
    /// display names. Unknown keys fall back to the key itself so a
    /// new processor that forgets to add a translation still shows
    /// something readable.
    /// </summary>
    private static string ResolveSubLayerDisplayName(string key) => key switch
    {
        "s111.color-band" => Strings.SubLayer_S111_ColorBand,
        "s111.arrows" => Strings.SubLayer_S111_Arrows,
        _ => key,
    };

    private void ApplyDisplayState(DatasetEntry entry)
    {
        if (!_entryLayers.TryGetValue(entry, out var layers)) return;

        // When the processor emitted sub-layer keys, fold the per-
        // sub-layer state into the per-layer Enabled/Opacity values.
        // Otherwise apply parent state uniformly.
        _entryLayerKeys.TryGetValue(entry, out var keys);

        var subLayerLookup = entry.SubLayers.Count > 0
            ? entry.SubLayers.ToDictionary(s => s.Key, s => s)
            : null;

        for (int i = 0; i < layers.Count; i++)
        {
            var layer = layers[i];
            DatasetSubLayer? sub = null;
            if (subLayerLookup is not null && keys is not null && i < keys.Count)
            {
                subLayerLookup.TryGetValue(keys[i], out sub);
            }

            // AND visibility, multiply opacity. (Mapsui has a single
            // scalar opacity per layer, so multiplication is the
            // canonical way to express parent×sub.)
            layer.Enabled = entry.IsVisible && (sub?.IsVisible ?? true);
            layer.Opacity = entry.Opacity * (sub?.Opacity ?? 1.0);
        }
    }

    private void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not DatasetEntry entry) return;
        if (e.PropertyName is not (nameof(DatasetEntry.IsVisible) or nameof(DatasetEntry.Opacity)))
            return;
        ApplyDisplayState(entry);
    }

    private void OnSubLayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not DatasetSubLayer sub) return;
        if (e.PropertyName is not (nameof(DatasetSubLayer.IsVisible) or nameof(DatasetSubLayer.Opacity)))
            return;

        // The sub-layer doesn't know its parent; find it by membership.
        // The cost is bounded by the number of loaded datasets which is
        // always small for an interactive viewer.
        foreach (var (entry, _) in _entryLayers)
        {
            if (entry.SubLayers.Contains(sub))
            {
                ApplyDisplayState(entry);
                break;
            }
        }
    }

    private void RemoveEntryLayers(DatasetEntry entry)
    {
        if (_mapHost is null)
            return;

        if (_entryLayers.TryGetValue(entry, out var oldLayers))
        {
            foreach (var layer in oldLayers)
            {
                _mapHost.RemoveLayer(layer);
            }
            _entryLayers.Remove(entry);
        }
    }

    private void EnsureInitialized()
    {
        if (_mapHost is null || _pipelineFactory is null)
            throw new InvalidOperationException("DatasetLoaderService.Initialize must be called before LoadAsync.");
    }
}
