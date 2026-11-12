using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui.Layers;

namespace EncDotNet.S100.Viewer.Tests;

public class DatasetOpeningExchangeSetProgressTests : IDisposable
{
    private readonly string _tempSettingsPath = Path.Combine(
        Path.GetTempPath(), $"viewer-tests-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        try { if (File.Exists(_tempSettingsPath)) File.Delete(_tempSettingsPath); }
        catch { /* best effort */ }
    }



    private sealed class NoopLoader : IDatasetLoaderService
    {
        public IReadOnlyDictionary<DatasetEntry, IDatasetProcessor> Processors { get; }
            = new Dictionary<DatasetEntry, IDatasetProcessor>();
        public IReadOnlyDictionary<DatasetEntry, IReadOnlyList<ILayer>> EntryLayers { get; }
            = new Dictionary<DatasetEntry, IReadOnlyList<ILayer>>();
        public event Action<DatasetEntry>? DatasetLoaded { add { } remove { } }
        public event Action<DatasetEntry>? DatasetRemoved { add { } remove { } }
        public void Initialize(IMapLayerCollection layers, IMapViewportController viewport, ViewerCommandSettings? options) { }
        public Task LoadAsync(DatasetEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ReRenderAtTimeAsync(DateTime t, System.Threading.CancellationToken ct) => Task.CompletedTask;
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

    private DatasetOpeningViewModel CreateViewModel()
    {
        var settings = new ViewerSettings { SettingsFilePath = _tempSettingsPath };
        var datasets = new DatasetsViewModel(new NoopLoader());
        var timeline = new TimelineViewModel(new GlobalTimeService(), null, new Microsoft.Extensions.Time.Testing.FakeTimeProvider(), dispatch: EncDotNet.S100.Viewer.Services.UiThread.RunOrPost, refresher: null, progress: null, scope: null, library: null);
        return new DatasetOpeningViewModel(
            new StubRecentFilesService(),
            Notifications.TestNotifications.Create(),
            datasets,
            new ShellLayoutViewModel(settings, [], timeline, EncDotNet.S100.Viewer.Services.UiThread.Post));
    }

    [Fact]
    public void BeginExchangeSetLoad_SetsLoadingStateAndReturnsCancellableToken()
    {
        var vm = CreateViewModel();

        var token = vm.BeginExchangeSetLoad("/some/folder");

        Assert.True(vm.IsExchangeSetLoading);
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
    public void EndExchangeSetLoad_FullSuccess_ClearsLoadingState()
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
    }

    [Fact]
    public void EndExchangeSetLoad_PartialFailure_ClearsLoadingState()
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
    }

    [Fact]
    public void EndExchangeSetLoad_FatalFailure_ClearsLoadingState()
    {
        var vm = CreateViewModel();
        vm.BeginExchangeSetLoad("/missing");

        vm.EndExchangeSetLoad(new ExchangeSetOpenResult
        {
            SourcePath = "/missing",
            FailureMessage = "boom",
        });

        Assert.False(vm.IsExchangeSetLoading);
    }
}
