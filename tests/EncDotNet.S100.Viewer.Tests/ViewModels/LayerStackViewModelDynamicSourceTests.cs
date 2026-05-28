using System;
using System.Collections.Generic;
using System.Linq;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Viewer.Services.DynamicSources;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui.Layers;
using Xunit;
using ControllableLoader = EncDotNet.S100.Viewer.Tests.LayerStackViewModelTests.ControllableLoader;

namespace EncDotNet.S100.Viewer.Tests.ViewModels;

/// <summary>
/// PR-D2.1: dynamic-source rows in <see cref="LayerStackViewModel"/>.
/// </summary>
public class LayerStackViewModelDynamicSourceTests
{
    [Fact]
    public void Rebuild_NoDynamicSources_NoDynamicArrowsPlane()
    {
        var loader = new ControllableLoader();
        loader.SetEntries(Entry("a.000", S98DisplayPlane.BaseChartUnder, 10));
        var registry = new FakeRegistry();

        var vm = new LayerStackViewModel(loader, registry);

        Assert.DoesNotContain(vm.Planes, p => p.Plane == S98DisplayPlane.DynamicArrows);
    }

    [Fact]
    public void Rebuild_DynamicSources_AppearInDynamicArrowsPlane_InRegistrationOrder()
    {
        var loader = new ControllableLoader();
        loader.SetEntries(Entry("a.000", S98DisplayPlane.BaseChartUnder, 10));
        var registry = new FakeRegistry();
        registry.Add(new DynamicSourceRegistrationInfo("ownship", "Own Ship", null));
        registry.Add(new DynamicSourceRegistrationInfo("ais", "AIS", "Other vessels"));

        var vm = new LayerStackViewModel(loader, registry);

        var plane = vm.Planes.Single(p => p.Plane == S98DisplayPlane.DynamicArrows);
        var dyn = plane.Children.OfType<LayerStackDynamicEntryViewModel>().ToList();
        Assert.Equal(new[] { "ownship", "ais" }, dyn.Select(d => d.SourceId));
        Assert.Equal("Own Ship", dyn[0].DisplayName);
    }

    [Fact]
    public void DynamicEntry_IsActive_TogglesThroughRegistry()
    {
        var loader = new ControllableLoader();
        var registry = new FakeRegistry();
        registry.Add(new DynamicSourceRegistrationInfo("ownship", "Own Ship", null));

        var vm = new LayerStackViewModel(loader, registry);
        var entry = vm.Planes.Single(p => p.Plane == S98DisplayPlane.DynamicArrows)
            .Children.OfType<LayerStackDynamicEntryViewModel>().Single();

        Assert.True(entry.IsActive);
        entry.IsActive = false;
        Assert.False(registry.GetVisible("ownship"));
        Assert.False(entry.IsActive);
    }

    [Fact]
    public void SourcesChanged_TriggersRebuild_AddingNewRow()
    {
        var loader = new ControllableLoader();
        var registry = new FakeRegistry();
        var vm = new LayerStackViewModel(loader, registry);

        Assert.DoesNotContain(vm.Planes, p => p.Plane == S98DisplayPlane.DynamicArrows);

        registry.Add(new DynamicSourceRegistrationInfo("ownship", "Own Ship", null));

        var plane = vm.Planes.Single(p => p.Plane == S98DisplayPlane.DynamicArrows);
        Assert.Single(plane.Children.OfType<LayerStackDynamicEntryViewModel>());
    }

    [Fact]
    public void SourcesChanged_TriggersRebuild_RemovingRow()
    {
        var loader = new ControllableLoader();
        var registry = new FakeRegistry();
        registry.Add(new DynamicSourceRegistrationInfo("ownship", "Own Ship", null));
        var vm = new LayerStackViewModel(loader, registry);

        registry.RemoveAt(0);

        Assert.DoesNotContain(vm.Planes, p => p.Plane == S98DisplayPlane.DynamicArrows);
    }

    [Fact]
    public void MixedPlane_DatasetAndDynamicRowsCoexist_DatasetsFirst()
    {
        var loader = new ControllableLoader();
        loader.SetEntries(Entry("s111-arrows", S98DisplayPlane.DynamicArrows, 50));
        var registry = new FakeRegistry();
        registry.Add(new DynamicSourceRegistrationInfo("ownship", "Own Ship", null));

        var vm = new LayerStackViewModel(loader, registry);
        var plane = vm.Planes.Single(p => p.Plane == S98DisplayPlane.DynamicArrows);

        Assert.Equal(2, plane.Children.Count);
        Assert.IsType<LayerStackEntryViewModel>(plane.Children[0]);
        Assert.IsType<LayerStackDynamicEntryViewModel>(plane.Children[1]);
    }

    private static LayerStackEntry Entry(string id, S98DisplayPlane plane, int priority)
        => new(new MemoryLayer(id), plane, priority, id);

    private sealed class FakeRegistry : IDynamicFeatureSourceRegistry
    {
        private readonly List<DynamicSourceRegistrationInfo> _list = new();
        private readonly Dictionary<string, bool> _visible = new(StringComparer.Ordinal);

        public IReadOnlyList<DynamicSourceRegistrationInfo> Sources => _list;
        public event Action? SourcesChanged;

        public bool GetVisible(string sourceId) =>
            !_visible.TryGetValue(sourceId, out var v) || v;

        public void SetVisible(string sourceId, bool visible)
        {
            _visible[sourceId] = visible;
            SourcesChanged?.Invoke();
        }

        public void Add(DynamicSourceRegistrationInfo info)
        {
            _list.Add(info);
            SourcesChanged?.Invoke();
        }

        public void RemoveAt(int index)
        {
            _list.RemoveAt(index);
            SourcesChanged?.Invoke();
        }
    }
}
