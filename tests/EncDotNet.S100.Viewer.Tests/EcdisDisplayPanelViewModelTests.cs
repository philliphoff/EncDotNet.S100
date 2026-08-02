using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Specifications;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui.Layers;

namespace EncDotNet.S100.Viewer.Tests;

public class EcdisDisplayPanelViewModelTests
{
    private sealed class StubDatasetLoaderService : IDatasetLoaderService
    {
        public IReadOnlyDictionary<DatasetEntry, IDatasetProcessor> Processors { get; } =
            new Dictionary<DatasetEntry, IDatasetProcessor>();
        public IReadOnlyDictionary<DatasetEntry, IReadOnlyList<ILayer>> EntryLayers { get; } =
            new Dictionary<DatasetEntry, IReadOnlyList<ILayer>>();
        public event System.Action<DatasetEntry>? DatasetLoaded { add { } remove { } }
        public event System.Action<DatasetEntry>? DatasetRemoved { add { } remove { } }
        public void Initialize(IMapLayerCollection layers, IMapViewportController viewport, ViewerCommandSettings? options) { }
        public Task LoadAsync(DatasetEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
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

    private static PortrayalCatalogue CreateSyntheticCatalogue() => new()
    {
        ProductId = "S-101",
        Version = "1.0",
        ViewingGroups =
        [
            new ViewingGroup { Id = "11010", Description = new Description { Name = "Land area" } },
            new ViewingGroup { Id = "12210", Description = new Description { Name = "Depth contour" } },
            new ViewingGroup { Id = "21010", Description = new Description { Name = "Buoy" } },
        ],
        ViewingGroupLayers =
        [
            new ViewingGroupLayer
            {
                Id = "BaseLayers",
                Description = new Description { Name = "Base Layers" },
                ViewingGroupIds = ["11010"],
            },
        ],
        DisplayModes =
        [
            new DisplayMode
            {
                Id = "DisplayBase",
                Description = new Description { Name = "Display Base" },
                ViewingGroupLayerIds = ["BaseLayers"],
            },
        ],
    };

    [Fact]
    public void IsEmpty_WhenNoDatasets()
    {
        var state = new EcdisDisplayState();
        var catalogues = new PortrayalCatalogueManager();
        var datasets = new DatasetsViewModel(new StubDatasetLoaderService());

        using var vm = new EcdisDisplayPanelViewModel(state, catalogues, datasets);

        Assert.True(vm.IsEmpty);
        Assert.Empty(vm.Specs);
    }

    [Fact]
    public void ActiveCategory_TwoWaySyncWithState()
    {
        var state = new EcdisDisplayState();
        var catalogues = new PortrayalCatalogueManager();
        var datasets = new DatasetsViewModel(new StubDatasetLoaderService());

        using var vm = new EcdisDisplayPanelViewModel(state, catalogues, datasets);

        vm.ActiveCategory = EcdisDisplayCategory.All;
        Assert.Equal(EcdisDisplayCategory.All, state.Category);
        Assert.True(vm.IsAll);

        state.SetCategory(EcdisDisplayCategory.DisplayBase);
        Assert.Equal(EcdisDisplayCategory.DisplayBase, vm.ActiveCategory);
        Assert.True(vm.IsDisplayBase);
    }

    [Fact]
    public void ResetAllOverrides_ClearsState()
    {
        var state = new EcdisDisplayState();
        state.HideViewingGroup("S-101", 11010);
        state.HideViewingGroup("S-124", 22010);

        var catalogues = new PortrayalCatalogueManager();
        var datasets = new DatasetsViewModel(new StubDatasetLoaderService());

        using var vm = new EcdisDisplayPanelViewModel(state, catalogues, datasets);

        vm.ResetAllOverridesCommand.Execute(null);

        Assert.Empty(state.GetHidden("S-101"));
        Assert.Empty(state.GetHidden("S-124"));
    }

    [Fact]
    public void CategoryCommands_SwitchCategory()
    {
        var state = new EcdisDisplayState();
        var catalogues = new PortrayalCatalogueManager();
        var datasets = new DatasetsViewModel(new StubDatasetLoaderService());

        using var vm = new EcdisDisplayPanelViewModel(state, catalogues, datasets);

        vm.SetDisplayBaseCommand.Execute(null);
        Assert.Equal(EcdisDisplayCategory.DisplayBase, state.Category);

        vm.SetAllCommand.Execute(null);
        Assert.Equal(EcdisDisplayCategory.All, state.Category);
    }

    [Fact]
    public void DisplayModeToolbar_IsNull_WhenNotInjected()
    {
        var state = new EcdisDisplayState();
        var catalogues = new PortrayalCatalogueManager();
        var datasets = new DatasetsViewModel(new StubDatasetLoaderService());

        using var vm = new EcdisDisplayPanelViewModel(state, catalogues, datasets);

        Assert.Null(vm.DisplayModeToolbar);
    }

    [Fact]
    public void DisplayModeToolbar_ReturnsInjectedInstance()
    {
        var state = new EcdisDisplayState();
        var catalogues = new PortrayalCatalogueManager();
        var loader = new StubDatasetLoaderService();
        var datasets = new DatasetsViewModel(loader);
        using var toolbar = new DisplayModeToolbarViewModel(state, loader);

        using var vm = new EcdisDisplayPanelViewModel(
            state,
            catalogues,
            datasets,
            labelOverrides: null,
            displayModeToolbar: toolbar);

        Assert.Same(toolbar, vm.DisplayModeToolbar);
    }

    [Fact]
    public void RebuildSpecs_S57Entry_BuildsS101ControlGroup()
    {
        // S-57 datasets are portrayed with the S-101 catalogue, so the ECDIS
        // panel must expose the S-101 controls for them (issue #450).
        var state = new EcdisDisplayState();
        var catalogues = new PortrayalCatalogueManager();
        catalogues.SetSource("S-101", Specification.CreatePortrayalCatalogueSource("S-101"));
        var datasets = new DatasetsViewModel(new StubDatasetLoaderService());

        using var vm = new EcdisDisplayPanelViewModel(state, catalogues, datasets);
        datasets.Add("US5MA1BO.000", "S-57");

        var spec = Assert.Single(vm.Specs);
        Assert.Equal("S-101", spec.SpecCode);
    }

    [Fact]
    public void RebuildSpecs_S57AndNativeS101_ShareSingleS101ControlGroup()
    {
        // An S-57 entry and a native S-101 entry both portray as S-101 and must
        // collapse into one control group rather than two (issue #450 dedup).
        var state = new EcdisDisplayState();
        var catalogues = new PortrayalCatalogueManager();
        catalogues.SetSource("S-101", Specification.CreatePortrayalCatalogueSource("S-101"));
        var datasets = new DatasetsViewModel(new StubDatasetLoaderService());

        using var vm = new EcdisDisplayPanelViewModel(state, catalogues, datasets);
        datasets.Add("US5MA1BO.000", "S-57");
        datasets.Add("101AA00DS0008.000", "S-101");

        var spec = Assert.Single(vm.Specs);
        Assert.Equal("S-101", spec.SpecCode);
    }
}
