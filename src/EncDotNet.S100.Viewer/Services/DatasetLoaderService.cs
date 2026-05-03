using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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

    private readonly Dictionary<DatasetEntry, IDatasetProcessor> _processors = new();
    private readonly Dictionary<DatasetEntry, IReadOnlyList<ILayer>> _entryLayers = new();
    private readonly ReadOnlyDictionary<DatasetEntry, IDatasetProcessor> _processorsView;
    private readonly ReadOnlyDictionary<DatasetEntry, IReadOnlyList<ILayer>> _entryLayersView;

    private IMapHost? _mapHost;
    private DatasetPipelineFactory? _pipelineFactory;

    public DatasetLoaderService(
        ViewerSettings settings,
        PortrayalCatalogueManager catalogueManager,
        PortrayalCatalogueSeeder catalogueSeeder,
        IRecentFilesService recentFiles,
        S128DatasetCatalogSource s128CatalogSource,
        SettingsViewModel settingsVm)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(catalogueManager);
        ArgumentNullException.ThrowIfNull(catalogueSeeder);
        ArgumentNullException.ThrowIfNull(recentFiles);
        ArgumentNullException.ThrowIfNull(s128CatalogSource);
        ArgumentNullException.ThrowIfNull(settingsVm);

        _settings = settings;
        _catalogueManager = catalogueManager;
        _catalogueSeeder = catalogueSeeder;
        _recentFiles = recentFiles;
        _s128CatalogSource = s128CatalogSource;
        _settingsVm = settingsVm;

        _processorsView = new ReadOnlyDictionary<DatasetEntry, IDatasetProcessor>(_processors);
        _entryLayersView = new ReadOnlyDictionary<DatasetEntry, IReadOnlyList<ILayer>>(_entryLayers);
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

            var initialContext = CreateRenderContext(processor);
            var result = await Task.Run(() => processor.Render(initialContext));

            ReplaceLayers(entry, result.Layers.ToList());
            _mapHost!.ZoomToExtent(result.Extent);

            entry.IsLoaded = true;
            entry.Info = result.Info;
            SetStatus(result.Info);

            _recentFiles.Add(entry.FilePath);

            // Populate time steps for S-111 datasets
            if (processor is S111DatasetProcessor s111)
            {
                entry.AvailableTimes = s111.AvailableTimes;
            }

            // Populate time steps for S-104 datasets
            if (processor is S104DatasetProcessor s104)
            {
                entry.AvailableTimes = s104.AvailableTimes;
            }

            // Re-render when the user changes the time step
            entry.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(DatasetEntry.SelectedTimeIndex))
                    _ = ReRenderTimeStepAsync(entry);
            };

            DatasetLoaded?.Invoke(entry);
        }
        catch (Exception ex)
        {
            SetStatus(string.Format(Strings.Status_Error, ex.Message));
            Console.Error.WriteLine($"Failed to load {entry.FilePath}:\n{ex}");
        }
    }

    public async Task ReRenderTimeStepAsync(DatasetEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (!_processors.TryGetValue(entry, out var proc))
            return;

        var times = entry.AvailableTimes;
        var idx = entry.SelectedTimeIndex;
        if (times is null || idx < 0 || idx >= times.Count)
            return;

        SetStatus(string.Format(Strings.Status_RenderingTimeStep, idx + 1, times.Count));

        try
        {
            var context = CreateRenderContext(proc, times[idx]);
            var result = await Task.Run(() => proc.Render(context));

            ReplaceLayers(entry, result.Layers.ToList());

            entry.Info = result.Info;
            SetStatus(result.Info);
        }
        catch (Exception ex)
        {
            SetStatus(string.Format(Strings.Status_Error, ex.Message));
            Console.Error.WriteLine($"Failed to re-render time step:\n{ex}");
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
                var times = entry.AvailableTimes;
                var idx = entry.SelectedTimeIndex;

                DateTime? timeStep = times is not null && idx >= 0 && idx < times.Count ? times[idx] : null;
                var context = CreateRenderContext(proc, timeStep);

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
