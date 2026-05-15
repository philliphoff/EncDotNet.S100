using System.Collections.Generic;
using System.Threading.Tasks;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Portrayals;
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
        public event System.Action<string?>? StatusChanged { add { } remove { } }
        public void Initialize(IMapHost host, ViewerCommandSettings? options) { }
        public Task LoadAsync(DatasetEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ReRenderAtTimeAsync(System.DateTime t, System.Threading.CancellationToken ct) => Task.CompletedTask;
        public Task ReRenderAllAsync() => Task.CompletedTask;
        public void RemoveEntry(DatasetEntry entry) { }
        public void SetEntryOrder(IReadOnlyList<DatasetEntry> ordered) { }
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
}
