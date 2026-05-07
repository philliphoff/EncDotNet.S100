using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Viewer.Catalogs;
using EncDotNet.S100.Viewer.Services;
using ExchangeSetProgress = EncDotNet.S100.Viewer.Services.ExchangeSetProgress;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui.Layers;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

public class MainViewModelExchangeSetProgressTests : IDisposable
{
    private readonly string _tempSettingsPath = Path.Combine(
        Path.GetTempPath(), $"viewer-tests-{Guid.NewGuid():N}.json");

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

    private sealed class NoopLoader : IDatasetLoaderService
    {
        public IReadOnlyDictionary<DatasetEntry, IDatasetProcessor> Processors { get; }
            = new Dictionary<DatasetEntry, IDatasetProcessor>();
        public IReadOnlyDictionary<DatasetEntry, IReadOnlyList<ILayer>> EntryLayers { get; }
            = new Dictionary<DatasetEntry, IReadOnlyList<ILayer>>();
        public event Action<DatasetEntry>? DatasetLoaded { add { } remove { } }
        public event Action<string?>? StatusChanged { add { } remove { } }
        public void Initialize(IMapHost host, ViewerCommandSettings? options) { }
        public Task LoadAsync(DatasetEntry entry) => Task.CompletedTask;
        public Task ReRenderAtTimeAsync(DateTime t, System.Threading.CancellationToken ct) => Task.CompletedTask;
        public Task ReRenderAllAsync() => Task.CompletedTask;
        public void RemoveEntry(DatasetEntry entry) { }
        public void SetEntryOrder(IReadOnlyList<DatasetEntry> ordered) { }
    }

    private MainViewModel CreateViewModel()
    {
        var settings = new ViewerSettings { SettingsFilePath = _tempSettingsPath };
        var catalogues = new PortrayalCatalogueManager();
        return new MainViewModel(
            settings,
            featureCatalogues: new FeatureCataloguesViewModel(settings),
            portrayalCatalogues: new PortrayalCataloguesViewModel(settings, catalogues),
            datasets: new DatasetsViewModel(new NoopLoader()),
            catalogPanel: new CatalogPanelViewModel(new EmptyCatalogSource()),
            search: new FeatureSearchViewModel(new StubFeatureSearchService(), new StubPickService()),
            settingsViewModel: new SettingsViewModel(settings),
            pickReport: new PickReportViewModel(),
            timeline: new TimelineViewModel(new GlobalTimeService()),
            displayToolbar: new DisplayToolbarViewModel(new EcdisDisplayState()),
            ecdisDisplayPanel: new EcdisDisplayPanelViewModel(new EcdisDisplayState(), catalogues, new DatasetsViewModel(new NoopLoader())),
            themeService: new StubThemeService(),
            recentFiles: new StubRecentFilesService(),
            measureAppearance: new StubMeasureOverlayAppearanceProvider());
    }

    [Fact]
    public void BeginExchangeSetLoad_SetsLoadingStateAndReturnsCancellableToken()
    {
        var vm = CreateViewModel();

        var token = vm.BeginExchangeSetLoad("/some/folder");

        Assert.True(vm.IsExchangeSetLoading);
        Assert.Equal("/some/folder", vm.ExchangeSetSourceLabel);
        Assert.Equal(0.0, vm.ExchangeSetProgressFraction);
        Assert.False(token.IsCancellationRequested);
        Assert.True(vm.CancelExchangeSetCommand.CanExecute(null));
    }

    [Fact]
    public void CancelCommand_TriggersTokenCancellation()
    {
        var vm = CreateViewModel();
        var token = vm.BeginExchangeSetLoad("/some/folder");

        vm.CancelExchangeSetCommand.Execute(null);

        Assert.True(token.IsCancellationRequested);
    }

    [Fact]
    public void CancelCommand_DisabledWhenIdle()
    {
        var vm = CreateViewModel();

        Assert.False(vm.IsExchangeSetLoading);
        Assert.False(vm.CancelExchangeSetCommand.CanExecute(null));
    }

    [Fact]
    public void ReportProgress_UpdatesFractionAndCounter()
    {
        var vm = CreateViewModel();
        vm.BeginExchangeSetLoad("/some/folder");

        vm.ReportExchangeSetProgress(new ExchangeSetProgress(
            "/some/folder", Total: 4, Completed: 1, Failed: 0, CurrentDataset: "101NO/foo.000"));

        Assert.Equal(0.25, vm.ExchangeSetProgressFraction);
        Assert.Equal("101NO/foo.000", vm.ExchangeSetCurrentDataset);
        Assert.False(string.IsNullOrEmpty(vm.ExchangeSetCounter));
    }

    [Fact]
    public void EndExchangeSetLoad_FullSuccess_ClearsBanner()
    {
        var vm = CreateViewModel();
        vm.BeginExchangeSetLoad("/some/folder");

        vm.EndExchangeSetLoad(new ExchangeSetOpenResult
        {
            SourcePath = "/some/folder",
            Total = 4,
            Loaded = 4,
        });

        Assert.False(vm.IsExchangeSetLoading);
        Assert.False(vm.IsExchangeSetBannerVisible);
    }

    [Fact]
    public void EndExchangeSetLoad_PartialFailure_ShowsBanner()
    {
        var vm = CreateViewModel();
        vm.BeginExchangeSetLoad("/some/folder");

        vm.EndExchangeSetLoad(new ExchangeSetOpenResult
        {
            SourcePath = "/some/folder",
            Total = 4,
            Loaded = 3,
            SkippedUnsupported = 1,
        });

        Assert.False(vm.IsExchangeSetLoading);
        Assert.True(vm.IsExchangeSetBannerVisible);
        Assert.False(string.IsNullOrEmpty(vm.ExchangeSetBannerMessage));
    }

    [Fact]
    public void EndExchangeSetLoad_FatalFailure_ShowsBanner()
    {
        var vm = CreateViewModel();
        vm.BeginExchangeSetLoad("/missing");

        vm.EndExchangeSetLoad(new ExchangeSetOpenResult
        {
            SourcePath = "/missing",
            FailureMessage = "boom",
        });

        Assert.True(vm.IsExchangeSetBannerVisible);
        Assert.Contains("boom", vm.ExchangeSetBannerMessage);
    }

    [Fact]
    public void DismissBannerCommand_HidesBanner()
    {
        var vm = CreateViewModel();
        vm.BeginExchangeSetLoad("/some/folder");
        vm.EndExchangeSetLoad(new ExchangeSetOpenResult
        {
            SourcePath = "/some/folder",
            Total = 2, Loaded = 1, SkippedUnsupported = 1,
        });
        Assert.True(vm.IsExchangeSetBannerVisible);

        vm.DismissExchangeSetBannerCommand.Execute(null);

        Assert.False(vm.IsExchangeSetBannerVisible);
    }
}
