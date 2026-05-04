using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Viewer.Catalogs;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui.Layers;

namespace EncDotNet.S100.Viewer.Tests;

public class PickServiceTests
{
    private sealed class EmptyCatalogSource : IDatasetCatalogSource
    {
        public string Id => "test";
        public string DisplayName => "Test";
        public IReadOnlyList<DatasetCatalogEntry> Entries => Array.Empty<DatasetCatalogEntry>();
        public event EventHandler<DatasetCatalogChangedEventArgs>? Changed { add { } remove { } }
    }

    private sealed class StubThemeService : IThemeService
    {
        public bool IsDarkTheme { get; private set; }
        public bool ToggleTheme() { IsDarkTheme = !IsDarkTheme; return IsDarkTheme; }
    }

    private sealed class StubLoader : IDatasetLoaderService
    {
        public IReadOnlyDictionary<DatasetEntry, IDatasetProcessor> Processors { get; }
            = new Dictionary<DatasetEntry, IDatasetProcessor>();
        public IReadOnlyDictionary<DatasetEntry, IReadOnlyList<ILayer>> EntryLayers { get; }
            = new Dictionary<DatasetEntry, IReadOnlyList<ILayer>>();
        public event Action<DatasetEntry>? DatasetLoaded { add { } remove { } }
        public event Action<string?>? StatusChanged { add { } remove { } }
        public void Initialize(IMapHost host, ViewerCommandSettings? options) { }
        public Task LoadAsync(DatasetEntry entry) => Task.CompletedTask;
        public Task ReRenderAtTimeAsync(System.DateTime t, System.Threading.CancellationToken ct) => Task.CompletedTask;
        public Task ReRenderAllAsync() => Task.CompletedTask;
        public void RemoveEntry(DatasetEntry entry) { }
    }

    private static MainViewModel CreateMainViewModel()
    {
        var settings = new ViewerSettings();
        var catalogues = new PortrayalCatalogueManager();
        return new MainViewModel(
            settings,
            featureCatalogues: new FeatureCataloguesViewModel(settings),
            portrayalCatalogues: new PortrayalCataloguesViewModel(settings, catalogues),
            datasets: new DatasetsViewModel(new StubLoader()),
            catalogPanel: new CatalogPanelViewModel(new EmptyCatalogSource()),
            settingsViewModel: new SettingsViewModel(settings),
            pickReport: new PickReportViewModel(),
            timeline: new TimelineViewModel(new GlobalTimeService()),
            themeService: new StubThemeService(),
            recentFiles: new StubRecentFilesService(),
            measureAppearance: new StubMeasureOverlayAppearanceProvider());
    }

    [Fact]
    public void HandlePick_NullMapInfo_ClearsPickReport()
    {
        var viewModel = CreateMainViewModel();
        // Seed with an active pick so we can verify it gets cleared.
        viewModel.PickReport.SetPick(
            featureType: "DepthArea",
            featureRef: "FRID#1",
            datasetFileName: "test.000",
            productSpec: "S-101",
            attributes: new Dictionary<string, string?>());
        Assert.True(viewModel.PickReport.HasPick);

        var service = new PickService(new StubLoader(), viewModel);
        service.HandlePick(null);

        Assert.False(viewModel.PickReport.HasPick);
    }

    [Fact]
    public void HandlePick_NullMapInfo_NoPriorPick_StaysCleared()
    {
        var viewModel = CreateMainViewModel();
        var service = new PickService(new StubLoader(), viewModel);

        service.HandlePick(null);

        Assert.False(viewModel.PickReport.HasPick);
    }
}
