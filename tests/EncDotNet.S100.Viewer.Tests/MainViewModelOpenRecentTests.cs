using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using EncDotNet.S100.Datasets.Pipelines;
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
        public bool ToggleTheme() => false;
    }

    private sealed class RecordingLoaderService : IDatasetLoaderService
    {
        public List<DatasetEntry> Loaded { get; } = new();
        public IReadOnlyDictionary<DatasetEntry, IDatasetProcessor> Processors { get; }
            = new Dictionary<DatasetEntry, IDatasetProcessor>();
        public IReadOnlyDictionary<DatasetEntry, IReadOnlyList<ILayer>> EntryLayers { get; }
            = new Dictionary<DatasetEntry, IReadOnlyList<ILayer>>();
        public event Action<DatasetEntry>? DatasetLoaded { add { } remove { } }
        public event Action<string?>? StatusChanged { add { } remove { } }
        public void Initialize(IMapHost host, ViewerCommandSettings? options) { }
        public Task LoadAsync(DatasetEntry entry) { Loaded.Add(entry); return Task.CompletedTask; }
        public Task ReRenderTimeStepAsync(DatasetEntry entry) => Task.CompletedTask;
        public Task ReRenderAllAsync() => Task.CompletedTask;
        public void RemoveEntry(DatasetEntry entry) { }
    }

    private MainViewModel CreateViewModel(
        out RecordingLoaderService loader,
        out StubRecentFilesService recent)
    {
        var settings = new ViewerSettings { SettingsFilePath = _tempSettingsPath };
        var catalogues = new PortrayalCatalogueManager();
        loader = new RecordingLoaderService();
        recent = new StubRecentFilesService();
        return new MainViewModel(
            settings,
            featureCatalogues: new FeatureCataloguesViewModel(settings),
            portrayalCatalogues: new PortrayalCataloguesViewModel(settings, catalogues),
            datasets: new DatasetsViewModel(loader),
            catalogPanel: new CatalogPanelViewModel(new EmptyCatalogSource()),
            settingsViewModel: new SettingsViewModel(settings),
            pickReport: new PickReportViewModel(),
            themeService: new StubThemeService(),
            recentFiles: recent,
            measureAppearance: new StubMeasureOverlayAppearanceProvider());
    }

    [Fact]
    public async Task OpenRecent_MissingFile_RemovesFromRecentAndSetsStatus()
    {
        var vm = CreateViewModel(out var loader, out var recent);
        var ghost = Path.Combine(Path.GetTempPath(), $"does-not-exist-{Guid.NewGuid():N}");
        recent.Add(ghost);

        await vm.OpenRecentCommand.ExecuteAsync(ghost);

        Assert.DoesNotContain(ghost, recent.Items);
        Assert.False(string.IsNullOrEmpty(vm.StatusText));
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
