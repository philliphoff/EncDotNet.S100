using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

    private readonly Dictionary<DatasetEntry, IDatasetProcessor> _processors = new();
    private readonly Dictionary<DatasetEntry, IReadOnlyList<ILayer>> _entryLayers = new();
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
        GlobalTimeService globalTime)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(catalogueManager);
        ArgumentNullException.ThrowIfNull(catalogueSeeder);
        ArgumentNullException.ThrowIfNull(recentFiles);
        ArgumentNullException.ThrowIfNull(s128CatalogSource);
        ArgumentNullException.ThrowIfNull(settingsVm);
        ArgumentNullException.ThrowIfNull(globalTime);

        _settings = settings;
        _catalogueManager = catalogueManager;
        _catalogueSeeder = catalogueSeeder;
        _recentFiles = recentFiles;
        _s128CatalogSource = s128CatalogSource;
        _settingsVm = settingsVm;
        _globalTime = globalTime;

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
    }

    public async Task LoadAsync(DatasetEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        EnsureInitialized();

        var spec = DatasetPipelineFactory.DetectProductSpec(entry.FilePath);
        if (spec is null)
        {
            SetStatus(string.Format(Strings.Status_UnrecognizedFileType, Path.GetExtension(entry.FilePath)));
            return;
        }

        // S-104 ships a built-in portrayal catalogue; all others need an external one.
        if (spec != "S-104" && !_catalogueManager.HasCatalogue(spec))
        {
            SetStatus(string.Format(Strings.Status_SelectPortrayalCatalogue, spec));
            return;
        }

        SetStatus(string.Format(Strings.Status_LoadingFile, Path.GetFileName(entry.FilePath)));

        try
        {
            var processor = await Task.Run(() => _pipelineFactory!.CreateProcessor(entry.FilePath));
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

            ReplaceLayers(entry, result.Layers.ToList());
            _mapHost!.ZoomToExtent(result.Extent);

            entry.IsLoaded = true;
            entry.Info = result.Info;
            entry.CurrentTime = initialTime ?? adapter?.AvailableTimes.FirstOrDefault();
            SetStatus(result.Info);

            _recentFiles.Add(entry.FilePath);

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
            Console.Error.WriteLine($"Failed to load {entry.FilePath}:\n{ex}");
        }
    }

    public async Task ReRenderAtTimeAsync(DateTime t, CancellationToken cancellationToken)
    {
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

                ReplaceLayers(entry, result.Layers.ToList());
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
        var palette = _settingsVm.SelectedPalette;
        SetStatus(string.Format(Strings.Status_SwitchingPalette, palette));

        foreach (var (entry, proc) in _processors.ToArray())
        {
            if (!entry.IsLoaded) continue;

            try
            {
                var context = CreateRenderContext(proc, entry.CurrentTime);

                var result = await Task.Run(() => proc.Render(context));

                ReplaceLayers(entry, result.Layers.ToList());
                entry.Info = result.Info;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to re-render {entry.FilePath} with {palette} palette:\n{ex}");
            }
        }

        SetStatus(string.Format(Strings.Status_PaletteApplied, palette));
    }

    public void RemoveEntry(DatasetEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        RemoveEntryLayers(entry);
        _processors.Remove(entry);
        _globalTime.Unregister(entry);
        _s128CatalogSource.RemoveDataset(entry.DisplayName);
    }

    private RenderContext CreateRenderContext(IDatasetProcessor processor, DateTime? timeStep = null)
    {
        var palette = _settingsVm.SelectedPalette;
        var symbolScale = _settingsVm.SymbolScale;
        var textScale = _settingsVm.TextScale;

        return processor switch
        {
            S104DatasetProcessor when timeStep is not null
                => new S104RenderContext(timeStep) { Palette = palette, SymbolScale = symbolScale, TextScale = textScale },
            S104DatasetProcessor
                => new S104RenderContext { Palette = palette, SymbolScale = symbolScale, TextScale = textScale },
            S111DatasetProcessor when timeStep is not null
                => new S111RenderContext(timeStep) { Palette = palette, SymbolScale = symbolScale, TextScale = textScale },
            S111DatasetProcessor
                => new S111RenderContext { Palette = palette, SymbolScale = symbolScale, TextScale = textScale },
            S101DatasetProcessor
                => new S101RenderContext { Palette = palette, SymbolScale = symbolScale, TextScale = textScale },
            S102DatasetProcessor
                => new S102RenderContext { Palette = palette, SymbolScale = symbolScale, TextScale = textScale },
            S122DatasetProcessor
                => new S122RenderContext { Palette = palette, SymbolScale = symbolScale, TextScale = textScale },
            S124DatasetProcessor
                => new S124RenderContext { Palette = palette, SymbolScale = symbolScale, TextScale = textScale },
            S125DatasetProcessor
                => new S125RenderContext { Palette = palette, SymbolScale = symbolScale, TextScale = textScale },
            S127DatasetProcessor
                => new S127RenderContext { Palette = palette, SymbolScale = symbolScale, TextScale = textScale },
            S129DatasetProcessor
                => new S129RenderContext { Palette = palette, SymbolScale = symbolScale, TextScale = textScale },
            S411DatasetProcessor when timeStep is not null
                => new S411RenderContext(timeStep) { Palette = palette, SymbolScale = symbolScale, TextScale = textScale },
            S411DatasetProcessor
                => new S411RenderContext { Palette = palette, SymbolScale = symbolScale, TextScale = textScale },
            _ => new S101RenderContext { Palette = palette, SymbolScale = symbolScale, TextScale = textScale },
        };
    }

    private void ReplaceLayers(DatasetEntry entry, IReadOnlyList<ILayer> layers)
    {
        RemoveEntryLayers(entry);
        _entryLayers[entry] = layers;
        foreach (var layer in layers)
        {
            _mapHost!.AddLayer(layer);
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
