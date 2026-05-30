using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Viewer.Catalogs;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.ViewModels.Activities;
using Mapsui.Layers;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// PR-M1: registry-shaped behaviour of <see cref="MainViewModel"/> driven
/// by <see cref="IActivityTab"/>. Exercises ordering, command dispatch,
/// pane-title binding, persistence semantics, and startup restoration.
/// </summary>
public sealed class ActivityTabRegistryTests : IDisposable
{
    private readonly string _tempSettingsPath = Path.Combine(
        Path.GetTempPath(), $"viewer-tests-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        try { if (File.Exists(_tempSettingsPath)) File.Delete(_tempSettingsPath); }
        catch { /* best effort */ }
    }

    private sealed class FakeTab : IActivityTab
    {
        public required string Id { get; init; }
        public int Order { get; init; }
        public string Title { get; init; } = "T";
        public string Tooltip { get; init; } = "Tip";
        public object ViewModel { get; init; } = new object();
        public Type ViewType { get; init; } = typeof(ContentControl);
        public bool PersistAsLastSelected { get; init; } = true;
        public TabDock Dock { get; init; } = TabDock.Left;
        public bool AutoOpenOnContentSignal { get; init; }

        public Control CreateIcon() => new ContentControl();
    }

    private sealed class SignalingViewModel : IActivityTabContentSignal
    {
        public event EventHandler? ContentBecameAvailable;
        public void Raise() => ContentBecameAvailable?.Invoke(this, EventArgs.Empty);
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
        public ChromeTheme Current => ChromeTheme.Light;
        public void SetTheme(ChromeTheme theme) { }
        public event System.EventHandler<ChromeTheme>? ThemeChanged { add { } remove { } }
    }

    private sealed class NoopLoader : IDatasetLoaderService
    {
        public IReadOnlyDictionary<DatasetEntry, IDatasetProcessor> Processors { get; }
            = new Dictionary<DatasetEntry, IDatasetProcessor>();
        public IReadOnlyDictionary<DatasetEntry, IReadOnlyList<ILayer>> EntryLayers { get; }
            = new Dictionary<DatasetEntry, IReadOnlyList<ILayer>>();
        public event Action<DatasetEntry>? DatasetLoaded { add { } remove { } }
        public event Action<DatasetEntry>? DatasetRemoved { add { } remove { } }
        public event Action<string?>? StatusChanged { add { } remove { } }
        public void Initialize(IMapHost host, ViewerCommandSettings? options) { }
        public System.Threading.Tasks.Task LoadAsync(DatasetEntry entry, System.Threading.CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task ReRenderAtTimeAsync(DateTime t, System.Threading.CancellationToken ct) => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task ReRenderAllAsync() => System.Threading.Tasks.Task.CompletedTask;
        public void RemoveEntry(DatasetEntry entry) { }
        public void SetEntryOrder(IReadOnlyList<DatasetEntry> ordered) { }
        public IReadOnlyList<ILayer> CurrentStackedLayers => Array.Empty<ILayer>();
        public IReadOnlyList<LayerStackEntry> CurrentStackEntries => Array.Empty<LayerStackEntry>();
        public event Action? LayerStackChanged { add { } remove { } }
        public bool GetActive(string datasetId) => true;
        public void SetActive(string datasetId, bool active) { }
        public event Action<string>? ActiveChanged { add { } remove { } }
    }

    private MainViewModel CreateViewModel(IEnumerable<IActivityTab>? tabs, ViewerSettings? settings = null)
    {
        settings ??= new ViewerSettings { SettingsFilePath = _tempSettingsPath };
        var catalogues = new PortrayalCatalogueManager();
        var datasets = new DatasetsViewModel(new NoopLoader());
        return new MainViewModel(
            settings,
            featureCatalogues: new FeatureCataloguesViewModel(settings),
            portrayalCatalogues: new PortrayalCataloguesViewModel(settings, catalogues),
            datasets: datasets,
            catalogPanel: new CatalogPanelViewModel(new EmptyCatalogSource()),
            layerStack: new LayerStackViewModel(new NoopLoader()),
            search: new FeatureSearchViewModel(new StubFeatureSearchService(), new StubPickService()),
            settingsViewModel: new SettingsViewModel(settings),
            pickReport: new PickReportViewModel(),
            timeline: new TimelineViewModel(new GlobalTimeService()),
            displayToolbar: new DisplayToolbarViewModel(new EcdisDisplayState()),
            textToolbar: new TextGroupToolbarViewModel(new EcdisDisplayState(), catalogues, datasets),
            ecdisDisplayPanel: new EcdisDisplayPanelViewModel(new EcdisDisplayState(), catalogues, datasets),
            themeService: new StubThemeService(),
            recentFiles: new StubRecentFilesService(),
            measureAppearance: new StubMeasureOverlayAppearanceProvider(),
            toasts: new StubToastService(),
            activityTabs: tabs);
    }

    [Fact]
    public void Tabs_AreReturnedInOrderAscending()
    {
        var tabs = new IActivityTab[]
        {
            new FakeTab { Id = "C", Order = 30 },
            new FakeTab { Id = "A", Order = 10 },
            new FakeTab { Id = "B", Order = 20 },
            new FakeTab { Id = "Z", Order = 1000 }, // bottom group
        };

        var vm = CreateViewModel(tabs);

        Assert.Equal(new[] { "A", "B", "C", "Z" }, vm.Tabs.Select(t => t.Id));
        Assert.Equal(new[] { "A", "B", "C" }, vm.LeftDockTopTabs.Select(t => t.Id));
        Assert.Equal(new[] { "Z" }, vm.LeftDockBottomTabs.Select(t => t.Id));
    }

    [Fact]
    public void SelectTabCommand_SetsSelectedTabAndSelectedTabId()
    {
        var a = new FakeTab { Id = "A", Order = 10 };
        var b = new FakeTab { Id = "B", Order = 20 };
        var vm = CreateViewModel(new IActivityTab[] { a, b });

        vm.SelectTabCommand.Execute(b);

        Assert.Same(b, vm.SelectedLeftTab);
        Assert.Equal("B", vm.SelectedLeftTabId);
    }

    [Fact]
    public void PaneTitle_ReflectsSelectedTabTitle()
    {
        var datasets = new FakeTab { Id = "Datasets", Order = 30, Title = "Default" };
        var a = new FakeTab { Id = "A", Order = 10, Title = "Alpha" };
        var b = new FakeTab { Id = "B", Order = 20, Title = "Beta" };
        var vm = CreateViewModel(new IActivityTab[] { datasets, a, b });

        // Startup defaults to Datasets.
        Assert.Equal("Default", vm.LeftDockTitle);

        vm.SelectTabCommand.Execute(a);
        Assert.Equal("Alpha", vm.LeftDockTitle);

        vm.SelectTabCommand.Execute(b);
        Assert.Equal("Beta", vm.LeftDockTitle);
    }

    [Fact]
    public void Selecting_PersistentTab_WritesIdToSettings()
    {
        var settings = new ViewerSettings { SettingsFilePath = _tempSettingsPath };
        var datasets = new FakeTab { Id = "Datasets", Order = 30, PersistAsLastSelected = true };
        var other = new FakeTab { Id = "Search", Order = 60, PersistAsLastSelected = true };
        var vm = CreateViewModel(new IActivityTab[] { datasets, other }, settings);

        vm.SelectTabCommand.Execute(other);

        Assert.Equal("Search", settings.LastSelectedActivity);
    }

    [Fact]
    public void Selecting_NonPersistentTab_DoesNotUpdateSettings()
    {
        var settings = new ViewerSettings
        {
            SettingsFilePath = _tempSettingsPath,
            LastSelectedActivity = "Datasets",
        };
        var datasets = new FakeTab { Id = "Datasets", Order = 30, PersistAsLastSelected = true };
        var settingsTab = new FakeTab { Id = "Settings", Order = 1000, PersistAsLastSelected = false };
        var vm = CreateViewModel(new IActivityTab[] { datasets, settingsTab }, settings);

        vm.SelectTabCommand.Execute(settingsTab);

        Assert.Same(settingsTab, vm.SelectedLeftTab);
        Assert.Equal("Datasets", settings.LastSelectedActivity);
    }

    [Fact]
    public void Startup_RestoresValidLastSelectedActivity()
    {
        var settings = new ViewerSettings
        {
            SettingsFilePath = _tempSettingsPath,
            LastSelectedActivity = "Search",
        };
        var datasets = new FakeTab { Id = "Datasets", Order = 30 };
        var search = new FakeTab { Id = "Search", Order = 60 };
        var vm = CreateViewModel(new IActivityTab[] { datasets, search }, settings);

        Assert.Same(search, vm.SelectedLeftTab);
        Assert.Equal("Search", settings.LastSelectedActivity);
    }

    [Fact]
    public void Startup_WithStaleLastSelectedActivity_FallsBackToDatasets()
    {
        var settings = new ViewerSettings
        {
            SettingsFilePath = _tempSettingsPath,
            LastSelectedActivity = "DoesNotExist",
        };
        var datasets = new FakeTab { Id = "Datasets", Order = 30 };
        var search = new FakeTab { Id = "Search", Order = 60 };
        var vm = CreateViewModel(new IActivityTab[] { datasets, search }, settings);

        Assert.Same(datasets, vm.SelectedLeftTab);
    }

    [Fact]
    public void Startup_WithNoLastSelectedActivity_DefaultsToDatasets()
    {
        var settings = new ViewerSettings { SettingsFilePath = _tempSettingsPath };
        var datasets = new FakeTab { Id = "Datasets", Order = 30 };
        var search = new FakeTab { Id = "Search", Order = 60 };
        var vm = CreateViewModel(new IActivityTab[] { datasets, search }, settings);

        Assert.Same(datasets, vm.SelectedLeftTab);
    }

    [Fact]
    public void SelectDefaultTab_SelectsDatasetsEvenIfNotFirstByOrder()
    {
        var search = new FakeTab { Id = "Search", Order = 10 }; // earliest order
        var datasets = new FakeTab { Id = "Datasets", Order = 30 };
        var vm = CreateViewModel(new IActivityTab[] { search, datasets });

        vm.SelectTabCommand.Execute(search);
        Assert.Same(search, vm.SelectedLeftTab);

        vm.SelectDefaultTab();

        Assert.Same(datasets, vm.SelectedLeftTab);
        Assert.Equal("Datasets", vm.SelectedLeftTabId);
    }

    [Fact]
    public void SelectDefaultTab_WithoutDatasets_FallsBackToFirstTab()
    {
        var alpha = new FakeTab { Id = "Alpha", Order = 10 };
        var beta = new FakeTab { Id = "Beta", Order = 20 };
        var vm = CreateViewModel(new IActivityTab[] { alpha, beta });

        // Startup falls back to first tab when Datasets is absent.
        Assert.Same(alpha, vm.SelectedLeftTab);

        vm.SelectTabCommand.Execute(beta);
        vm.SelectDefaultTab();

        Assert.Same(alpha, vm.SelectedLeftTab);
    }
}
