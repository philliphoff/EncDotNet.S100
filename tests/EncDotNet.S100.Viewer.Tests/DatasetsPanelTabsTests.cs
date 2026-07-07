using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui.Layers;
using Xunit;
using System.Collections.ObjectModel;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Covers the Datasets-panel tabs refactor (issue #378): source→dataset
/// grouping, per-source visibility fan-out, inspector selection
/// coordination across the two tabs, and the conditional default tab.
/// </summary>
public class DatasetsPanelTabsTests
{
    private sealed class StubAssetSource : IAssetSource
    {
        public Task<Stream> OpenAsync(string relativePath, CancellationToken cancellationToken = default)
            => Task.FromResult<Stream>(new MemoryStream());
        public void Dispose() { }
    }

    private sealed class NoopLoader : IDatasetLoaderService
    {
        public IReadOnlyDictionary<DatasetEntry, IDatasetProcessor> Processors { get; }
            = new Dictionary<DatasetEntry, IDatasetProcessor>();
        public IReadOnlyDictionary<DatasetEntry, IReadOnlyList<ILayer>> EntryLayers { get; }
            = new Dictionary<DatasetEntry, IReadOnlyList<ILayer>>();
        public event Action<DatasetEntry>? DatasetLoaded { add { } remove { } }
        public event Action<DatasetEntry>? DatasetRemoved { add { } remove { } }
        public void Initialize(IMapHost host, ViewerCommandSettings? options) { }
        public Task LoadAsync(DatasetEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ReRenderAtTimeAsync(DateTime t, CancellationToken ct) => Task.CompletedTask;
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

    private static DatasetsViewModel NewVm() => new(new NoopLoader());

    // ── Grouping ─────────────────────────────────────────────────────

    [Fact]
    public void Grouping_NestsDatasetsUnderTheirSource()
    {
        var vm = NewVm();
        var srcA = new StubAssetSource();
        var headerA = vm.RegisterExchangeSetHeader(srcA, "/a", null, null, 2, _ => { });

        var d1 = vm.AddFromExchangeSet(srcA, "a/d1.000", "S-101");
        var d2 = vm.AddFromExchangeSet(srcA, "a/d2.000", "S-101");

        Assert.Equal(2, headerA.MemberCount);
        Assert.Contains(d1, headerA.Datasets);
        Assert.Contains(d2, headerA.Datasets);
    }

    [Fact]
    public void Grouping_LooseDatasets_AreNotNestedUnderAnySource()
    {
        var vm = NewVm();
        var srcA = new StubAssetSource();
        var headerA = vm.RegisterExchangeSetHeader(srcA, "/a", null, null, 1, _ => { });

        vm.AddFromExchangeSet(srcA, "a/d1.000", "S-101");
        var loose = vm.Add("/disk/loose.000", "S-101");

        Assert.DoesNotContain(loose, headerA.Datasets);
        Assert.Equal(1, headerA.MemberCount);
    }

    [Fact]
    public void Grouping_KeepsMembersInEntriesOrder()
    {
        var vm = NewVm();
        var srcA = new StubAssetSource();
        var headerA = vm.RegisterExchangeSetHeader(srcA, "/a", null, null, 2, _ => { });

        // AddFromExchangeSet inserts at index 0, so the most recently added
        // entry leads. The nested list must mirror that order.
        var first = vm.AddFromExchangeSet(srcA, "a/d1.000", "S-101");
        var second = vm.AddFromExchangeSet(srcA, "a/d2.000", "S-101");

        Assert.Same(second, headerA.Datasets[0]);
        Assert.Same(first, headerA.Datasets[1]);
    }

    [Fact]
    public void Grouping_TwoSources_DoNotCrossContaminate()
    {
        var vm = NewVm();
        var srcA = new StubAssetSource();
        var srcB = new StubAssetSource();
        var headerA = vm.RegisterExchangeSetHeader(srcA, "/a", null, null, 1, _ => { });
        var headerB = vm.RegisterExchangeSetHeader(srcB, "/b", null, null, 1, _ => { });

        var da = vm.AddFromExchangeSet(srcA, "a/d.000", "S-101");
        var db = vm.AddFromExchangeSet(srcB, "b/d.000", "S-102");

        Assert.Single(headerA.Datasets);
        Assert.Single(headerB.Datasets);
        Assert.Contains(da, headerA.Datasets);
        Assert.Contains(db, headerB.Datasets);
    }

    // ── Per-source visibility fan-out ────────────────────────────────

    [Fact]
    public void SourceVisibility_HidesAllMembers_WhenAnyVisible()
    {
        var vm = NewVm();
        var src = new StubAssetSource();
        var header = vm.RegisterExchangeSetHeader(src, "/a", null, null, 2, _ => { });
        var d1 = vm.AddFromExchangeSet(src, "a/d1.000", "S-101");
        var d2 = vm.AddFromExchangeSet(src, "a/d2.000", "S-101");

        Assert.True(header.IsAnyDatasetVisible);

        header.ToggleVisibilityCommand.Execute(null);

        Assert.False(d1.IsVisible);
        Assert.False(d2.IsVisible);
        Assert.False(header.IsAnyDatasetVisible);
    }

    [Fact]
    public void SourceVisibility_ShowsAllMembers_WhenAllHidden()
    {
        var vm = NewVm();
        var src = new StubAssetSource();
        var header = vm.RegisterExchangeSetHeader(src, "/a", null, null, 2, _ => { });
        var d1 = vm.AddFromExchangeSet(src, "a/d1.000", "S-101");
        var d2 = vm.AddFromExchangeSet(src, "a/d2.000", "S-101");
        d1.IsVisible = false;
        d2.IsVisible = false;

        Assert.False(header.IsAnyDatasetVisible);

        header.ToggleVisibilityCommand.Execute(null);

        Assert.True(d1.IsVisible);
        Assert.True(d2.IsVisible);
        Assert.True(header.IsAnyDatasetVisible);
    }

    [Fact]
    public void SourceVisibility_IsAnyDatasetVisible_TracksMemberChanges()
    {
        var vm = NewVm();
        var src = new StubAssetSource();
        var header = vm.RegisterExchangeSetHeader(src, "/a", null, null, 1, _ => { });
        var d1 = vm.AddFromExchangeSet(src, "a/d1.000", "S-101");

        var raised = false;
        header.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ExchangeSetHeader.IsAnyDatasetVisible)) raised = true;
        };

        d1.IsVisible = false;

        Assert.True(raised);
        Assert.False(header.IsAnyDatasetVisible);
    }

    // ── Inspector selection coordination ─────────────────────────────

    [Fact]
    public void Selection_SourceNode_DrivesExchangeSetInspector()
    {
        var vm = NewVm();
        var src = new StubAssetSource();
        var header = vm.RegisterExchangeSetHeader(src, "/a", "ACME", "2024-01-01", 1, _ => { });
        vm.AddFromExchangeSet(src, "a/d1.000", "S-101");

        vm.ActiveTabIndex = DatasetsViewModel.ExchangeSetsTabIndex;
        vm.SelectedSourceNode = header;

        Assert.True(vm.HasExchangeSetSelection);
        Assert.False(vm.HasSelection);
        Assert.False(vm.HasNoInspectorSelection);
        Assert.Same(header, vm.InspectedExchangeSet);
    }

    [Fact]
    public void Selection_NestedDataset_DrivesDatasetInspector()
    {
        var vm = NewVm();
        var src = new StubAssetSource();
        vm.RegisterExchangeSetHeader(src, "/a", null, null, 1, _ => { });
        var d1 = vm.AddFromExchangeSet(src, "a/d1.000", "S-101");

        vm.ActiveTabIndex = DatasetsViewModel.ExchangeSetsTabIndex;
        vm.SelectedSourceNode = d1;

        Assert.True(vm.HasSelection);
        Assert.False(vm.HasExchangeSetSelection);
        Assert.Same(d1, vm.SelectedEntry);
        Assert.Null(vm.InspectedExchangeSet);
    }

    [Fact]
    public void Selection_DatasetsTab_UsesFlatListSelection()
    {
        var vm = NewVm();
        var d1 = vm.Add("/disk/loose.000", "S-101");

        vm.ActiveTabIndex = DatasetsViewModel.DatasetsTabIndex;
        vm.SelectedDataset = d1;

        Assert.True(vm.HasSelection);
        Assert.Same(d1, vm.SelectedEntry);
        Assert.False(vm.HasExchangeSetSelection);
    }

    [Fact]
    public void Selection_SwitchingTabs_RetargetsInspector()
    {
        var vm = NewVm();
        var src = new StubAssetSource();
        var header = vm.RegisterExchangeSetHeader(src, "/a", null, null, 1, _ => { });
        var nested = vm.AddFromExchangeSet(src, "a/d1.000", "S-101");
        var loose = vm.Add("/disk/loose.000", "S-101");

        vm.SelectedSourceNode = header;
        vm.SelectedDataset = loose;

        // Exchange sets tab active → exchange set inspected.
        vm.ActiveTabIndex = DatasetsViewModel.ExchangeSetsTabIndex;
        Assert.Same(header, vm.InspectedExchangeSet);
        Assert.False(vm.HasSelection);

        // Datasets tab active → the flat-list selection is inspected.
        vm.ActiveTabIndex = DatasetsViewModel.DatasetsTabIndex;
        Assert.Same(loose, vm.SelectedEntry);
        Assert.Null(vm.InspectedExchangeSet);

        // Sanity: nested entry is part of the source's children.
        Assert.Contains(nested, header.Datasets);
    }

    [Fact]
    public void Selection_NothingSelected_ShowsPlaceholder()
    {
        var vm = NewVm();
        vm.Add("/disk/loose.000", "S-101");

        Assert.True(vm.HasNoInspectorSelection);
        Assert.False(vm.HasSelection);
        Assert.False(vm.HasExchangeSetSelection);
    }

    // ── Conditional default tab ──────────────────────────────────────

    [Fact]
    public void DefaultTab_WithExchangeSets_IsExchangeSets()
    {
        var vm = NewVm();
        var src = new StubAssetSource();
        vm.RegisterExchangeSetHeader(src, "/a", null, null, 1, _ => { });
        vm.AddFromExchangeSet(src, "a/d1.000", "S-101");

        Assert.True(vm.HasExchangeSets);
        Assert.Equal(DatasetsViewModel.ExchangeSetsTabIndex, vm.ActiveTabIndex);
    }

    [Fact]
    public void DefaultTab_OnlyLooseDatasets_IsDatasets()
    {
        var vm = NewVm();
        vm.Add("/disk/loose.000", "S-101");

        Assert.False(vm.HasExchangeSets);
        Assert.Equal(DatasetsViewModel.DatasetsTabIndex, vm.ActiveTabIndex);
    }

    [Fact]
    public void DefaultTab_UserPin_IsNotOverriddenByLaterLoads()
    {
        var vm = NewVm();
        vm.Add("/disk/loose.000", "S-101");
        Assert.Equal(DatasetsViewModel.DatasetsTabIndex, vm.ActiveTabIndex);

        // User explicitly switches to Exchange sets.
        vm.ActiveTabIndex = DatasetsViewModel.ExchangeSetsTabIndex;

        // A later loose load must not yank the tab back to Datasets.
        vm.Add("/disk/loose2.000", "S-101");

        Assert.Equal(DatasetsViewModel.ExchangeSetsTabIndex, vm.ActiveTabIndex);
    }

    [Fact]
    public void DefaultTab_PinResets_WhenPanelEmpties()
    {
        var vm = NewVm();
        var loose = vm.Add("/disk/loose.000", "S-101");
        vm.ActiveTabIndex = DatasetsViewModel.ExchangeSetsTabIndex;

        // Empty the panel; the pin resets so the default applies again.
        vm.Entries.Remove(loose);
        Assert.True(vm.IsEmpty);

        // Now load an exchange set; default should pick Exchange sets.
        var src = new StubAssetSource();
        vm.RegisterExchangeSetHeader(src, "/a", null, null, 1, _ => { });
        vm.AddFromExchangeSet(src, "a/d1.000", "S-101");

        Assert.Equal(DatasetsViewModel.ExchangeSetsTabIndex, vm.ActiveTabIndex);
    }

    [Fact]
    public void HasExchangeSets_FalseWhenNoneRegistered()
    {
        var vm = NewVm();
        Assert.False(vm.HasExchangeSets);
    }
}
