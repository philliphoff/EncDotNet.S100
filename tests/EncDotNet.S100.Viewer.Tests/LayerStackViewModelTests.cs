using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui.Layers;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Tests for <see cref="LayerStackViewModel"/> (PR-L3).
/// </summary>
public class LayerStackViewModelTests
{
    [Fact]
    public void Rebuild_GroupsByPlane_TopFirst()
    {
        var loader = new ControllableLoader();
        loader.SetEntries(
            Entry("a.000", S98DisplayPlane.BaseChartUnder, 10),
            Entry("b.h5", S98DisplayPlane.Bathymetry, 20),
            Entry("c.gml", S98DisplayPlane.MarinerOverlay, 30));

        var vm = new LayerStackViewModel(loader);

        var planes = vm.Planes.Select(p => p.Plane).ToList();
        // Top-first order: MarinerOverlay before Bathymetry before BaseChartUnder.
        Assert.Equal(new[]
        {
            S98DisplayPlane.MarinerOverlay,
            S98DisplayPlane.Bathymetry,
            S98DisplayPlane.BaseChartUnder,
        }, planes);
    }

    [Fact]
    public void Rebuild_OrdersChildren_ByWithinPlanePriorityDescending()
    {
        var loader = new ControllableLoader();
        loader.SetEntries(
            Entry("low.gml", S98DisplayPlane.MarinerOverlay, 1),
            Entry("high.gml", S98DisplayPlane.MarinerOverlay, 99),
            Entry("mid.gml", S98DisplayPlane.MarinerOverlay, 50));

        var vm = new LayerStackViewModel(loader);

        var plane = Assert.Single(vm.Planes);
        var ids = plane.Children.Select(c => c.DatasetId).ToList();
        Assert.Equal(new[] { "high.gml", "mid.gml", "low.gml" }, ids);
    }

    [Fact]
    public void EmptyPlanes_HiddenByDefault_ShownWhenToggled()
    {
        var loader = new ControllableLoader();
        loader.SetEntries(Entry("a.000", S98DisplayPlane.BaseChartUnder, 10));

        var vm = new LayerStackViewModel(loader);
        Assert.Single(vm.Planes); // only the populated plane

        vm.ShowEmptyPlanes = true;
        Assert.Equal(9, vm.Planes.Count); // all canonical planes

        vm.ShowEmptyPlanes = false;
        Assert.Single(vm.Planes);
    }

    [Fact]
    public void LayerStackChanged_TriggersRebuild()
    {
        var loader = new ControllableLoader();
        var vm = new LayerStackViewModel(loader);
        Assert.Empty(vm.Planes);

        loader.SetEntries(Entry("a.000", S98DisplayPlane.BaseChartUnder, 10));
        loader.FireLayerStackChanged();

        Assert.Single(vm.Planes);
        Assert.Equal(S98DisplayPlane.BaseChartUnder, vm.Planes[0].Plane);
    }

    [Fact]
    public void ExpansionState_PreservedAcrossRebuild()
    {
        var loader = new ControllableLoader();
        loader.SetEntries(Entry("a.000", S98DisplayPlane.BaseChartUnder, 10));
        var vm = new LayerStackViewModel(loader);

        // Collapse the plane.
        var plane = vm.Planes[0];
        plane.IsExpanded = false;

        // Trigger a rebuild by changing the entry set (still BaseChartUnder).
        loader.SetEntries(Entry("a.000", S98DisplayPlane.BaseChartUnder, 10),
                          Entry("b.000", S98DisplayPlane.BaseChartUnder, 20));
        loader.FireLayerStackChanged();

        var rebuilt = Assert.Single(vm.Planes);
        Assert.False(rebuilt.IsExpanded);
        Assert.Equal(2, rebuilt.Children.Count);
    }

    [Fact]
    public void IsActive_TogglesViaLoader()
    {
        var loader = new ControllableLoader();
        loader.SetEntries(Entry("a.000", S98DisplayPlane.BaseChartUnder, 10));
        var vm = new LayerStackViewModel(loader);

        var entry = vm.Planes[0].Children[0];
        Assert.True(entry.IsActive); // default

        entry.IsActive = false;
        Assert.False(loader.GetActive("a.000"));
        Assert.False(entry.IsActive);

        entry.IsActive = true;
        Assert.True(loader.GetActive("a.000"));
    }

    private static LayerStackEntry Entry(string id, S98DisplayPlane plane, int priority)
        => new(new MemoryLayer(id), plane, priority, id);

    /// <summary>
    /// Test loader stub with a settable entry list and a public
    /// <see cref="FireLayerStackChanged"/> method.
    /// </summary>
    internal sealed class ControllableLoader : IDatasetLoaderService
    {
        private IReadOnlyList<LayerStackEntry> _entries = Array.Empty<LayerStackEntry>();
        private readonly Dictionary<string, bool> _active = new();

        public void SetEntries(params LayerStackEntry[] entries) => _entries = entries;
        public void FireLayerStackChanged() => LayerStackChanged?.Invoke();

        public IReadOnlyList<LayerStackEntry> CurrentStackEntries => _entries;
        public IReadOnlyList<ILayer> CurrentStackedLayers => _entries.Select(e => e.Layer).ToList();
        public event Action? LayerStackChanged;

        public bool GetActive(string datasetId) =>
            !_active.TryGetValue(datasetId, out var v) || v;
        public void SetActive(string datasetId, bool active)
        {
            _active[datasetId] = active;
            ActiveChanged?.Invoke(datasetId);
            LayerStackChanged?.Invoke();
        }
        public event Action<string>? ActiveChanged;

        public IReadOnlyDictionary<DatasetEntry, IDatasetProcessor> Processors { get; } =
            new Dictionary<DatasetEntry, IDatasetProcessor>();
        public IReadOnlyDictionary<DatasetEntry, IReadOnlyList<ILayer>> EntryLayers { get; } =
            new Dictionary<DatasetEntry, IReadOnlyList<ILayer>>();

        public event Action<DatasetEntry>? DatasetLoaded { add { } remove { } }
        public event Action<DatasetEntry>? DatasetRemoved { add { } remove { } }
        public event Action<string?>? StatusChanged { add { } remove { } }

        public void Initialize(IMapHost host, ViewerCommandSettings? options) { }
        public Task LoadAsync(DatasetEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ReRenderAtTimeAsync(DateTime t, CancellationToken ct) => Task.CompletedTask;
        public Task ReRenderAllAsync() => Task.CompletedTask;
        public void RemoveEntry(DatasetEntry entry) { }
        public void SetEntryOrder(IReadOnlyList<DatasetEntry> orderedEntries) { }
    }
}
