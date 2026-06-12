using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Viewer.Catalogs;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui.Layers;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

public class MainViewModelOpenRecentTests : IDisposable
{
    private readonly string _tempSettingsPath;

    public MainViewModelOpenRecentTests()
    {
        _tempSettingsPath = Path.Combine(
            Path.GetTempPath(),
            $"viewer-tests-{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        try { if (File.Exists(_tempSettingsPath)) File.Delete(_tempSettingsPath); }
        catch { /* best effort */ }
    }

    private sealed class EmptyCatalogSource : IDatasetCatalogSource
    {
        public string Id => "test";
        public string DisplayName => "Test";
        public IReadOnlyList<DatasetCatalogEntry> Entries => Array.Empty<DatasetCatalogEntry>();
        public event EventHandler<DatasetCatalogChangedEventArgs>? Changed { add { } remove { } }
    }

    private sealed class StubThemeService : IThemeService
    {
        public bool IsDarkTheme => false;
        public event System.EventHandler? ThemeChanged { add { } remove { } }
        public bool ToggleTheme() => false;
        public ChromeTheme Current => ChromeTheme.Light;
        public void SetTheme(ChromeTheme theme) { }
    }

    private sealed class RecordingLoaderService : IDatasetLoaderService
    {
        public List<DatasetEntry> Loaded { get; } = new();
        public IReadOnlyDictionary<DatasetEntry, IDatasetProcessor> Processors { get; }
            = new Dictionary<DatasetEntry, IDatasetProcessor>();
        public IReadOnlyDictionary<DatasetEntry, IReadOnlyList<ILayer>> EntryLayers { get; }
            = new Dictionary<DatasetEntry, IReadOnlyList<ILayer>>();
        public event Action<DatasetEntry>? DatasetLoaded { add { } remove { } }
        public event Action<DatasetEntry>? DatasetRemoved { add { } remove { } }
        public void Initialize(IMapHost host, ViewerCommandSettings? options) { }
        public Task LoadAsync(DatasetEntry entry, CancellationToken cancellationToken = default) { Loaded.Add(entry); return Task.CompletedTask; }
        public Task ReRenderAtTimeAsync(System.DateTime t, System.Threading.CancellationToken ct) => Task.CompletedTask;
        public Task ReRenderAllAsync() => Task.CompletedTask;
        public void RemoveEntry(DatasetEntry entry) { }
        public void SetEntryOrder(IReadOnlyList<DatasetEntry> ordered) { }
        public IReadOnlyList<ILayer> CurrentStackedLayers => Array.Empty<ILayer>();
        public IReadOnlyList<LayerStackEntry> CurrentStackEntries => Array.Empty<LayerStackEntry>();
        public event Action? LayerStackChanged { add { } remove { } }
        public bool GetActive(string datasetId) => true;
        public void SetActive(string datasetId, bool active) { }
        public event Action<string>? ActiveChanged { add { } remove { } }
    }

    private sealed class RecordingToastService : IToastService
    {
        public int WarningCount { get; private set; }
        public void ShowInfo(string title, string? content = null) { }
        public void ShowSuccess(string title, string? content = null) { }
        public void ShowWarning(string title, string? content = null) => WarningCount++;
        public void ShowError(string title, string? content = null, string? actionLabel = null, Action? action = null, bool sticky = false) { }
        public void ShowLoading(string title, string? content = null, string? actionLabel = null, Action? action = null) { }
        public void DismissAll() { }
    }

    private MainViewModel CreateViewModel(
        out RecordingLoaderService loader,
        out StubRecentFilesService recent)
        => CreateViewModel(out loader, out recent, out _);

    private MainViewModel CreateViewModel(
        out RecordingLoaderService loader,
        out StubRecentFilesService recent,
        out RecordingToastService toasts)
    {
        var settings = new ViewerSettings { SettingsFilePath = _tempSettingsPath };
        var catalogues = new PortrayalCatalogueManager();
        loader = new RecordingLoaderService();
        recent = new StubRecentFilesService();
        toasts = new RecordingToastService();
        var datasets = new DatasetsViewModel(loader);
        return new MainViewModel(
            settings,
            featureCatalogues: new FeatureCataloguesViewModel(settings),
            portrayalCatalogues: new PortrayalCataloguesViewModel(settings, catalogues),
            datasets: datasets,
            catalogPanel: new CatalogPanelViewModel(new EmptyCatalogSource()),
            layerStack: new LayerStackViewModel(loader),
            search: new FeatureSearchViewModel(new StubFeatureSearchService(), new StubPickService()),
            settingsViewModel: new SettingsViewModel(settings),
            pickReport: new PickReportViewModel(),
            timeline: new TimelineViewModel(new GlobalTimeService()),
            displayToolbar: new DisplayToolbarViewModel(new EcdisDisplayState()),
            textToolbar: new TextGroupToolbarViewModel(new EcdisDisplayState(), catalogues, datasets),
            ecdisDisplayPanel: new EcdisDisplayPanelViewModel(new EcdisDisplayState(), catalogues, datasets),
            themeService: new StubThemeService(),
            recentFiles: recent,
            measureAppearance: new StubMeasureOverlayAppearanceProvider(),
            toasts: toasts);
    }

    [Fact]
    public async Task OpenRecent_MissingFile_RemovesFromRecentAndNotifies()
    {
        var vm = CreateViewModel(out var loader, out var recent, out var toasts);
        var ghost = Path.Combine(Path.GetTempPath(), $"does-not-exist-{Guid.NewGuid():N}");
        recent.Add(ghost);

        await vm.OpenRecentCommand.ExecuteAsync(ghost);

        Assert.DoesNotContain(ghost, recent.Items);
        Assert.Equal(1, toasts.WarningCount);
        Assert.Empty(loader.Loaded);
    }

    [Fact]
    public async Task OpenRecent_NullOrEmpty_IsNoOp()
    {
        var vm = CreateViewModel(out var loader, out _);

        await vm.OpenRecentCommand.ExecuteAsync(null);
        await vm.OpenRecentCommand.ExecuteAsync(string.Empty);

        Assert.Empty(loader.Loaded);
    }
}
