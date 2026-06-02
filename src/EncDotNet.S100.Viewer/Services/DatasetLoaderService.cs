using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Renderers.Mapsui.Simplification;
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
    private readonly FeatureCatalogueOverrides _fcOverrides;
    private readonly DatasetPipelineFactory _pipelineFactory;
    private readonly IRecentFilesService _recentFiles;
    private readonly S128DatasetCatalogSource _s128CatalogSource;
    private readonly SettingsViewModel _settingsVm;
    private readonly GlobalTimeService _globalTime;
    private readonly EcdisDisplayState _ecdisDisplay;
    private readonly IMarinerSettingsProvider _marinerSettings;
    private readonly IToastService _toasts;
    /// <summary>
    /// Resolves the <em>currently active</em> cross-dataset paint-order
    /// policy on each consult. Hosts can swap the authority at runtime
    /// (e.g. flip from S-98 to strict load-order) and we re-sort the
    /// stack in response to <see cref="IInteroperabilityAuthorityProvider.CurrentChanged"/>.
    /// </summary>
    private readonly IInteroperabilityAuthorityProvider _authorityProvider;

    private readonly Dictionary<DatasetEntry, IDatasetProcessor> _processors = new();
    private readonly Dictionary<DatasetEntry, IReadOnlyList<ILayer>> _entryLayers = new();
    /// <summary>
    /// Per-entry S-98 layer-stack entries produced by the processor's
    /// most recent render. Each entry's <see cref="LayerStackEntry.Layer"/>
    /// also appears in <see cref="_entryLayers"/>. Populated from
    /// <see cref="DatasetResult.StackEntries"/> when available; otherwise
    /// synthesised through the active <see cref="IInteroperabilityAuthority"/>.
    /// </summary>
    private readonly Dictionary<DatasetEntry, IReadOnlyList<LayerStackEntry>> _entryStackEntries = new();
    /// <summary>
    /// Snapshot of the most recently computed S-98 layer stack
    /// (bottom-of-paint-stack first; index 0 = drawn first / under
    /// everything else). Mirrors what was just handed to
    /// <see cref="IMapHost.ReorderDatasetLayers"/>. Refreshed whenever
    /// the layer order changes so <see cref="PickService"/> can rank
    /// multi-hit picks top-of-stack first.
    /// </summary>
    private IReadOnlyList<ILayer> _currentStackedLayers = Array.Empty<ILayer>();
    /// <summary>
    /// Snapshot of the most recently computed S-98 layer stack as
    /// <see cref="LayerStackEntry"/> records (bottom-of-paint-stack
    /// first). Same order as <see cref="_currentStackedLayers"/>;
    /// the Layer Stack panel
    /// (<see cref="ViewModels.LayerStackViewModel"/>) groups these
    /// by <see cref="S98DisplayPlane"/> for the tree view.
    /// </summary>
    private IReadOnlyList<LayerStackEntry> _currentStackEntries = Array.Empty<LayerStackEntry>();
    /// <summary>
    /// In-memory per-dataset Active flags. Keyed by dataset id (the
    /// same identifier produced by <see cref="EntryId"/>). Missing
    /// entries default to <c>true</c>. Process-local for PR-L3; PR-L4
    /// will persist in <see cref="ViewerSettings"/>.
    /// </summary>
    // TODO PR-L4: persist Active in ViewerSettings
    private readonly Dictionary<string, bool> _activeFlags = new(StringComparer.Ordinal);
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
        FeatureCatalogueOverrides fcOverrides,
        DatasetPipelineFactory pipelineFactory,
        IRecentFilesService recentFiles,
        S128DatasetCatalogSource s128CatalogSource,
        SettingsViewModel settingsVm,
        GlobalTimeService globalTime,
        EcdisDisplayState ecdisDisplay,
        IMarinerSettingsProvider marinerSettings,
        IToastService toasts,
        IInteroperabilityAuthorityProvider authorityProvider)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(catalogueManager);
        ArgumentNullException.ThrowIfNull(catalogueSeeder);
        ArgumentNullException.ThrowIfNull(fcOverrides);
        ArgumentNullException.ThrowIfNull(pipelineFactory);
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
        _fcOverrides = fcOverrides;
        _pipelineFactory = pipelineFactory;
        _recentFiles = recentFiles;
        _s128CatalogSource = s128CatalogSource;
        _settingsVm = settingsVm;
        _globalTime = globalTime;
        _ecdisDisplay = ecdisDisplay;
        _marinerSettings = marinerSettings;
        _toasts = toasts;
        ArgumentNullException.ThrowIfNull(authorityProvider);
        _authorityProvider = authorityProvider;
        // Re-sort the live layer stack whenever the host swaps the
        // active authority. Cheap when no datasets are loaded.
        _authorityProvider.CurrentChanged += OnAuthorityChanged;

        _processorsView = new ReadOnlyDictionary<DatasetEntry, IDatasetProcessor>(_processors);
        _entryLayersView = new ReadOnlyDictionary<DatasetEntry, IReadOnlyList<ILayer>>(_entryLayers);

        _globalTime.CurrentTimeChanged += t => _ = ReRenderAtTimeAsync(t, CancellationToken.None);
    }

    public IReadOnlyDictionary<DatasetEntry, IDatasetProcessor> Processors => _processorsView;
    public IReadOnlyDictionary<DatasetEntry, IReadOnlyList<ILayer>> EntryLayers => _entryLayersView;

    public IReadOnlyList<ILayer> CurrentStackedLayers => _currentStackedLayers;

    public IReadOnlyList<LayerStackEntry> CurrentStackEntries => _currentStackEntries;

    public event Action? LayerStackChanged;

    public event Action<string>? ActiveChanged;

    public bool GetActive(string datasetId)
    {
        ArgumentException.ThrowIfNullOrEmpty(datasetId);
        return !_activeFlags.TryGetValue(datasetId, out var v) || v;
    }

    public void SetActive(string datasetId, bool active)
    {
        ArgumentException.ThrowIfNullOrEmpty(datasetId);
        var previous = GetActive(datasetId);
        if (previous == active) return;
        _activeFlags[datasetId] = active;
        // Recompute the cross-product stack so R-101-102-B (and any
        // future Active-aware rules) re-evaluates with the new
        // flag, and rebroadcast it through the map host so PickService
        // / Layer Stack panel see the change.
        if (_mapHost is not null)
            _mapHost.ReorderDatasetLayers(FlattenLayerOrder());
        ActiveChanged?.Invoke(datasetId);
    }

    public event Action<DatasetEntry>? DatasetLoaded;

    /// <summary>
    /// Raised whenever the loader wants to surface a status message
    /// (loading, errors, time-step progress, etc.). The window forwards
    /// these to <see cref="MainViewModel.StatusText"/>.
    /// </summary>
    public event Action<string?>? StatusChanged;

    public event Action<DatasetEntry>? DatasetRemoved;

    /// <inheritdoc />
    public bool SuppressAutoZoom { get; set; }

    private void SetStatus(string? text) => StatusChanged?.Invoke(text);

    public void Initialize(IMapHost host, ViewerCommandSettings? options)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (_mapHost is not null)
            throw new InvalidOperationException("DatasetLoaderService has already been initialized.");

        _mapHost = host;

        var transientFcPaths = _catalogueSeeder.Seed(options);
        _fcOverrides.SetTransientPaths(transientFcPaths);

        // Re-render every loaded dataset whenever the user changes a setting
        // that affects portrayal output (palette / display scale). These are
        // wired here, not in the window, so the loader fully owns its
        // re-render lifecycle.
        _settingsVm.PaletteChanged += palette => _ = ReRenderAllAsync();
        _settingsVm.DisplayScaleChanged += () => _ = ReRenderAllAsync();
        _ecdisDisplay.Changed += () => _ = ReRenderAllAsync();
        _marinerSettings.Changed += m => _ = ReRenderAllAsync();
    }

    public async Task LoadAsync(DatasetEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        EnsureInitialized();

        using var __cmd = ViewerObservability.BeginCommand("dataset.open");

        // Create a linked CTS so the caller's token and the toast's
        // Cancel button both feed into a single token.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = cts.Token;

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

        // Show a loading toast with a Cancel action for standalone
        // dataset loads. Exchange-set entries are covered by the
        // exchange-set progress overlay instead, so skip the per-
        // dataset toast to avoid toast churn during bulk loads.
        if (!fromExchangeSet)
        {
            _toasts.ShowLoading(
                Strings.Toast_Loading,
                string.Format(Strings.Status_LoadingFile, entry.DisplayName),
                Strings.Toast_Cancel,
                () => cts.Cancel());
        }

        try
        {
            var processor = await Task.Run(() => fromExchangeSet
                ? _pipelineFactory.CreateProcessor(entry.Source!, entry.RelativePath!, spec)
                : _pipelineFactory.CreateProcessor(entry.FilePath), token);
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
            var result = await Task.Run(() => processor.RenderAsync(initialContext, token), token).ConfigureAwait(true);

            token.ThrowIfCancellationRequested();
            ReplaceLayers(entry, result.Layers.ToList(), result.LayerNames, result.StackEntries);
            // Exchange-set entries opt out of the per-dataset auto-zoom so
            // the union-extent zoom from `IExchangeSetService` (or the
            // user's manual Zoom-to-Extent toolbar action) wins. Without
            // this, the last-completed dataset would race with the bulk
            // load and "win" the viewport.
            if (!fromExchangeSet && !SuppressAutoZoom)
            {
                _mapHost!.ZoomToExtent(result.Extent);
            }

            entry.IsLoaded = true;
            entry.Info = result.Info;
            entry.CurrentTime = initialTime ?? adapter?.AvailableTimes.FirstOrDefault();

            // Run the spec's normative validation rule pack against
            // the parsed dataset. Validation is a pure function of the
            // parsed model so we only do this once per load; ECDIS
            // / palette / time-step changes never re-run it. A null
            // return means the spec has no rule pack defined yet —
            // distinct from an empty report — and the Validation tab
            // surfaces those two states with different empty-state
            // messages.
            var validation = await Task.Run(() => SafeValidate(processor), token);
            entry.SetValidationReport(validation);

            // Dismiss the loading toast before showing the result.
            // Exchange-set entries don't show per-dataset toasts.
            if (!fromExchangeSet)
            {
                _toasts.DismissAll();
            }
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
        catch (OperationCanceledException)
        {
            if (!fromExchangeSet)
            {
                _toasts.DismissAll();
                _toasts.ShowInfo(Strings.Toast_DatasetCancelled, entry.DisplayName);
            }
            SetStatus(null);
        }
        catch (Exception ex)
        {
            if (!fromExchangeSet)
            {
                _toasts.DismissAll();
            }

            // Shape the toast around the innermost structured S-100
            // exception (when present) so the user sees a friendly
            // one-liner instead of a raw stack trace. The full
            // ToString() is still available via the "Copy details"
            // action button; the toast itself sticks around until
            // explicitly dismissed.
            var failure = LoadFailureViewModel.FromException(
                entry.DisplayName, entry.FilePath, ex);
            SetStatus(string.Format(Strings.Status_Error, ex.Message));
            _toasts.ShowError(
                title: string.Format(Strings.Toast_DatasetErrorTitle, entry.DisplayName),
                content: failure.PrimaryMessage,
                actionLabel: Strings.LoadFailureToast_CopyDetails,
                action: () => CopyTextToClipboard(failure.Details),
                sticky: true);
        }
    }

    /// <summary>
    /// Copies <paramref name="text"/> to the system clipboard via the
    /// active main window. Used by the load-failure toast's
    /// "Copy details" action. Best-effort: any failure is swallowed so
    /// a flaky clipboard backend never crashes the dataset open path.
    /// </summary>
    private static void CopyTextToClipboard(string text)
    {
        try
        {
            if (Avalonia.Application.Current?.ApplicationLifetime
                is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                && desktop.MainWindow is { } mainWindow
                && Avalonia.Controls.TopLevel.GetTopLevel(mainWindow)?.Clipboard is { } clipboard)
            {
                _ = clipboard.SetTextAsync(text);
            }
        }
        catch
        {
            // Best-effort; clipboard access can fail on some Linux WMs.
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
                var result = await Task.Run(() => proc.RenderAsync(context, token), token).ConfigureAwait(true);

                token.ThrowIfCancellationRequested();
                ReplaceLayers(entry, result.Layers.ToList(), result.LayerNames, result.StackEntries);
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

                var result = await Task.Run(() => proc.RenderAsync(context, CancellationToken.None));

                ReplaceLayers(entry, result.Layers.ToList(), result.LayerNames, result.StackEntries);
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
        _entryStackEntries.Remove(entry);
        if (_subscribedEntries.Remove(entry))
            entry.PropertyChanged -= OnEntryPropertyChanged;
        _processors.Remove(entry);
        _entryOrder.Remove(entry);
        _activeFlags.Remove(EntryId(entry));
        _globalTime.Unregister(entry);
        _s128CatalogSource.RemoveDataset(entry.DisplayName);
        // Publish the new (empty / smaller) stack so PickService and
        // anyone else who cares drops references to the removed layers.
        if (_mapHost is not null)
            _mapHost.ReorderDatasetLayers(FlattenLayerOrder());
        DatasetRemoved?.Invoke(entry);
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
        // PR-L1 (S-98): defer the cross-dataset paint order to the
        // S-98 interoperability authority. _entryOrder is top-of-UI
        // first (mirrors the Datasets panel); the authority sorts
        // by S-98 display plane (BaseChartUnder → EcdisAlerts) and
        // uses input order as the final tiebreaker. We feed it
        // bottom-of-UI first so the topmost-UI dataset wins ties
        // (and lands at the highest layer index — drawn last, on
        // top), preserving the prior behaviour for single-plane
        // dataset stacks.
        //
        // PR-L3: we keep building the FULL plane-sorted list of
        // entries (including inactive datasets) so the Layer Stack
        // panel can still show their rows and let the user re-enable
        // them. Only the rendered layer list (returned to the map
        // host) is filtered to active entries; the snapshot stored
        // in <see cref="_currentStackEntries"/> retains every entry.
        var perDataset = new List<IReadOnlyList<LayerStackEntry>>(_entryOrder.Count);
        for (int i = 0; i < _entryOrder.Count; i++)
        {
            var entry = _entryOrder[i];
            if (!_entryLayers.TryGetValue(entry, out var layers)) continue;

            var datasetId = EntryId(entry);

            if (_entryStackEntries.TryGetValue(entry, out var stack) && stack.Count > 0)
            {
                perDataset.Add(stack);
            }
            else
            {
                // Fallback: processor didn't supply StackEntries. Drop
                // each layer onto the spec's default plane with
                // priority 0 so it still participates in S-98 ordering.
                var specName = _processors.TryGetValue(entry, out var proc)
                    ? proc.Spec.Name
                    : "unknown";
                var plane = _authorityProvider.Current.GetDefaultPlane(specName);
                var synth = new List<LayerStackEntry>(layers.Count);
                foreach (var l in layers)
                {
                    synth.Add(new LayerStackEntry(
                        Layer: l,
                        Plane: plane,
                        WithinPlanePriority: 0,
                        SourceDatasetId: datasetId));
                }
                perDataset.Add(synth);
            }
        }

        var authority = _authorityProvider.Current;
        var sorted = LayerStackBuilder.Build(authority, perDataset);

        // PR-L2: apply S-98 inter-product rules (suppression, etc.)
        // after the per-plane sort. The rule set is the default
        // S98DefaultRules collection; rules read the mariner settings
        // (e.g. SafetyContour for R-101-102-B's safety-contour
        // exception per MSC.232(82) §5.8). LoadOrderInteroperabilityAuthority
        // explicitly no-ops ApplyRules so the strict load-order mode
        // is unaffected.
        var loaded = BuildLoadedDatasetInfos();
        var ruled = authority.ApplyRules(sorted, loaded, _marinerSettings.Current);

        // Cache the FULL ruled list (including inactive datasets) for
        // the Layer Stack panel.
        _currentStackEntries = ruled;

        // PR-L3: filter inactive datasets out of the rendered layer
        // list handed back to the map host. The active flag is the
        // single source of truth: inactive entries don't paint and
        // don't influence pick.
        var renderEntries = new List<LayerStackEntry>(ruled.Count);
        foreach (var e in ruled)
        {
            if (!GetActive(e.SourceDatasetId)) continue;
            renderEntries.Add(e);
        }

        var list = LayerStackBuilder.ToLayerList(renderEntries);
        _currentStackedLayers = list;
        LayerStackChanged?.Invoke();
        return list;
    }

    /// <summary>
    /// Stable per-entry identifier matching
    /// <see cref="LayerStackEntry.SourceDatasetId"/>. Used both as
    /// the key for <see cref="_activeFlags"/> and as the dataset id
    /// passed to <see cref="IInteroperabilityAuthority"/> rules.
    /// </summary>
    private static string EntryId(DatasetEntry entry) =>
        entry.FilePath is { } p && p.Length > 0
            ? System.IO.Path.GetFileName(p)
            : entry.DisplayName;

    /// <summary>
    /// Builds the snapshot of <see cref="LoadedDatasetInfo"/> values
    /// the S-98 rule engine consumes. <c>Active</c> combines the
    /// PR-L3 in-memory flag, the existing <c>DatasetEntry.IsVisible</c>
    /// proxy, and a "did the processor actually produce layers?"
    /// check so a failed render doesn't accidentally suppress
    /// sibling products.
    /// </summary>
    private IReadOnlyList<LoadedDatasetInfo> BuildLoadedDatasetInfos()
    {
        var result = new List<LoadedDatasetInfo>(_entryOrder.Count);
        foreach (var entry in _entryOrder)
        {
            if (!_processors.TryGetValue(entry, out var proc)) continue;
            var datasetId = EntryId(entry);
            var active = GetActive(datasetId)
                && entry.IsVisible
                && _entryLayers.TryGetValue(entry, out var layers)
                && layers.Count > 0;
            result.Add(new LoadedDatasetInfo(datasetId, proc.Spec.Name, active));
        }
        return result;
    }

    /// <summary>
    /// Runs the processor's spec-specific validation rule pack and
    /// swallows any exception so a buggy rule cannot abort a dataset
    /// load. Returns the report on success, the processor's null on
    /// "no rule pack for this spec", or null on exception.
    /// </summary>
    private static EncDotNet.S100.Validation.ValidationReport? SafeValidate(IDatasetProcessor processor)
    {
        try
        {
            return processor.Validate();
        }
        catch (Exception ex)
        {
            // Defensive — individual rule failures are already
            // captured as synthetic Error findings by ValidationRuleSet.
            // This catches the unlikely case where projection or rule
            // pack construction itself throws.
            System.Diagnostics.Debug.WriteLine($"[validation] {processor.Spec.Name}: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
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
            S201DatasetProcessor
                => new S201RenderContext { Palette = palette, SymbolScale = symbolScale, TextScale = textScale, EcdisDisplay = ecdis, Mariner = mariner },
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

    private void ReplaceLayers(
        DatasetEntry entry,
        IReadOnlyList<ILayer> layers,
        IReadOnlyList<string>? layerKeys,
        IReadOnlyList<LayerStackEntry>? stackEntries)
    {
        bool isFirstLoad = !_entryOrder.Contains(entry);

        // Issue #164: opt-in resolution-aware geometry simplification.
        // Applied to the inner MemoryLayer BEFORE the rasterization
        // wrap below so that, when both flags are on, rasterized tiles
        // are produced from already-simplified geometry. The cache
        // lives on the layer; clearing on toggle is automatic via
        // RaiseMarinerChanged → full reload.
        if (_settingsVm.EnableGeometrySimplification && layers.Count > 0)
        {
            foreach (var layer in layers)
            {
                if (layer is InstrumentedMemoryLayer iml)
                {
                    iml.EnableSimplification(
                        DouglasPeuckerLineSimplifier.Instance,
                        SimplificationOptions.Default);
                }
            }
        }

        // Experimental: wrap S-100 vector (MemoryLayer) outputs in a
        // rasterising tile cache so each visible region is rendered
        // once and re-used during subsequent pan/zoom frames.
        // Coverage / image layers (S-102/S-104/S-111) are already
        // raster, so we leave them alone. Wrapping happens AFTER the
        // processor's AnnotateFeatures step (which type-checks the
        // raw MemoryLayer), so feature tagging is preserved.
        if (_settingsVm.EnableVectorRasterization && layers.Count > 0)
        {
            var wrapMap = new Dictionary<ILayer, ILayer>(ReferenceEqualityComparer.Instance);
            var wrapped = new List<ILayer>(layers.Count);
            foreach (var layer in layers)
            {
                if (layer is MemoryLayer memoryLayer)
                {
                    var wrapper = new S100RasterizingTileLayer(memoryLayer)
                    {
                        Name = memoryLayer.Name,
                    };
                    wrapped.Add(wrapper);
                    wrapMap[memoryLayer] = wrapper;
                }
                else
                {
                    wrapped.Add(layer);
                }
            }
            if (wrapMap.Count > 0)
            {
                layers = wrapped;
                if (stackEntries is not null && stackEntries.Count > 0)
                {
                    var remapped = new List<LayerStackEntry>(stackEntries.Count);
                    foreach (var se in stackEntries)
                    {
                        var l = wrapMap.TryGetValue(se.Layer, out var w) ? w : se.Layer;
                        remapped.Add(se with { Layer = l });
                    }
                    stackEntries = remapped;
                }
            }
        }

        RemoveEntryLayers(entry);
        _entryLayers[entry] = layers;
        _entryLayerKeys[entry] = layerKeys;
        // Keep _entryStackEntries in sync with _entryLayers. If the
        // processor didn't supply StackEntries, FlattenLayerOrder will
        // synthesise defaults below — but we still clear any stale
        // entries from a previous render so they don't leak.
        if (stackEntries is not null && stackEntries.Count > 0)
            _entryStackEntries[entry] = stackEntries;
        else
            _entryStackEntries.Remove(entry);

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

        // PR-L1 (S-98): always recompute the cross-dataset paint
        // order after a load/re-render. The S-98 plane sort can
        // place a newly-loaded dataset *under* existing layers
        // (e.g. an S-102 bathymetry load arrives after S-101 line
        // work — the bathy must sit between the ENC's area fills
        // and its line work). Pre-PR-L1 we only re-shuffled on
        // re-renders; that was correct for the old "load order
        // wins" model.
        if (isFirstLoad)
        {
            _entryOrder.Insert(0, entry);
        }
        _mapHost!.ReorderDatasetLayers(FlattenLayerOrder());
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

    private void OnAuthorityChanged()
    {
        // The host swapped the active interoperability authority
        // (e.g. flipped a viewer setting between S-98 and load-order).
        // Re-flatten the current stack through the new authority's
        // policy and push the result to the map host. Cheap when no
        // datasets are loaded.
        if (_mapHost is null) return;
        if (_entryOrder.Count == 0) return;
        _mapHost.ReorderDatasetLayers(FlattenLayerOrder());
    }

    private void EnsureInitialized()
    {
        if (_mapHost is null)
            throw new InvalidOperationException("DatasetLoaderService.Initialize must be called before LoadAsync.");
    }
}
