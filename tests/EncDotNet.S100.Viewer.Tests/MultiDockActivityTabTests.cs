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
/// PR-M4: multi-dock activity-tab registry. Exercises the per-dock
/// partitioning, dock-aware selection, close commands, and
/// auto-open-on-content-signal behaviour.
/// </summary>
public sealed class MultiDockActivityTabTests : IDisposable
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

    private sealed class SignalingVm : IActivityTabContentSignal
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
    public void Tabs_PartitionByDock()
    {
        var datasets = new FakeTab { Id = "Datasets", Order = 30, Dock = TabDock.Left };
        var pick = new FakeTab { Id = "Pick", Order = 10, Dock = TabDock.Right };
        var timeline = new FakeTab { Id = "Timeline", Order = 10, Dock = TabDock.Bottom };

        var vm = CreateViewModel(new IActivityTab[] { datasets, pick, timeline });

        Assert.Equal(new[] { "Datasets" }, vm.LeftTabs.Select(t => t.Id));
        Assert.Equal(new[] { "Pick" }, vm.RightTabs.Select(t => t.Id));
        Assert.Equal(new[] { "Timeline" }, vm.BottomTabs.Select(t => t.Id));
    }

    [Fact]
    public void SelectingLeftTab_DoesNotChangeRightOrBottomSelection()
    {
        var datasets = new FakeTab { Id = "Datasets", Order = 30, Dock = TabDock.Left };
        var search = new FakeTab { Id = "Search", Order = 60, Dock = TabDock.Left };
        var pick = new FakeTab { Id = "Pick", Order = 10, Dock = TabDock.Right };
        var timeline = new FakeTab { Id = "Timeline", Order = 10, Dock = TabDock.Bottom };

        var vm = CreateViewModel(new IActivityTab[] { datasets, search, pick, timeline });

        var rightBefore = vm.SelectedRightTab;
        var bottomBefore = vm.SelectedBottomTab;

        vm.SelectTabCommand.Execute(search);

        Assert.Same(search, vm.SelectedLeftTab);
        Assert.Same(rightBefore, vm.SelectedRightTab);
        Assert.Same(bottomBefore, vm.SelectedBottomTab);
    }

    [Fact]
    public void CloseDockCommand_ClosesTheTargetedDock()
    {
        var datasets = new FakeTab { Id = "Datasets", Order = 30, Dock = TabDock.Left };
        var pick = new FakeTab { Id = "Pick", Order = 10, Dock = TabDock.Right };
        var timeline = new FakeTab { Id = "Timeline", Order = 10, Dock = TabDock.Bottom };

        var vm = CreateViewModel(new IActivityTab[] { datasets, pick, timeline });

        vm.IsRightDockOpen = true;
        vm.IsBottomDockOpen = true;

        vm.CloseDockCommand.Execute(TabDock.Right);

        Assert.False(vm.IsRightDockOpen);
        Assert.True(vm.IsBottomDockOpen);
        Assert.True(vm.IsLeftDockOpen);
    }

    [Fact]
    public void AutoOpen_RaisingContentBecameAvailable_OpensDockAndSelectsTab()
    {
        var datasets = new FakeTab { Id = "Datasets", Order = 30, Dock = TabDock.Left };
        var signal = new SignalingVm();
        var pick = new FakeTab
        {
            Id = "Pick",
            Order = 10,
            Dock = TabDock.Right,
            AutoOpenOnContentSignal = true,
            ViewModel = signal,
        };

        var vm = CreateViewModel(new IActivityTab[] { datasets, pick });

        Assert.False(vm.IsRightDockOpen);

        signal.Raise();

        Assert.True(vm.IsRightDockOpen);
        Assert.Same(pick, vm.SelectedRightTab);
    }

    [Fact]
    public void AutoOpen_DoesNotFire_WhenAutoOpenOnContentSignalIsFalse()
    {
        var datasets = new FakeTab { Id = "Datasets", Order = 30, Dock = TabDock.Left };
        var signal = new SignalingVm();
        var pick = new FakeTab
        {
            Id = "Pick",
            Order = 10,
            Dock = TabDock.Right,
            AutoOpenOnContentSignal = false,
            ViewModel = signal,
        };

        var vm = CreateViewModel(new IActivityTab[] { datasets, pick });

        signal.Raise();

        Assert.False(vm.IsRightDockOpen);
    }

    [Fact]
    public void AutoOpen_AfterUserClose_SubsequentSignalReopensDock()
    {
        var datasets = new FakeTab { Id = "Datasets", Order = 30, Dock = TabDock.Left };
        var signal = new SignalingVm();
        var pick = new FakeTab
        {
            Id = "Pick",
            Order = 10,
            Dock = TabDock.Right,
            AutoOpenOnContentSignal = true,
            ViewModel = signal,
        };

        var vm = CreateViewModel(new IActivityTab[] { datasets, pick });

        signal.Raise();
        Assert.True(vm.IsRightDockOpen);

        vm.CloseDockCommand.Execute(TabDock.Right);
        Assert.False(vm.IsRightDockOpen);

        signal.Raise();
        Assert.True(vm.IsRightDockOpen);
    }
}
