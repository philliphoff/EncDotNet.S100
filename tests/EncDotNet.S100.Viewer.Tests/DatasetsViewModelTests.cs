using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui.Layers;

namespace EncDotNet.S100.Viewer.Tests;

public class DatasetsViewModelTests
{
    private sealed class RecordingLoader : IDatasetLoaderService
    {
        public List<DatasetEntry> LoadCalls { get; } = new();
        public List<IReadOnlyList<DatasetEntry>> OrderCalls { get; } = new();
        public IReadOnlyDictionary<DatasetEntry, IDatasetProcessor> Processors { get; }
            = new Dictionary<DatasetEntry, IDatasetProcessor>();
        public IReadOnlyDictionary<DatasetEntry, IReadOnlyList<ILayer>> EntryLayers { get; }
            = new Dictionary<DatasetEntry, IReadOnlyList<ILayer>>();
        public event Action<DatasetEntry>? DatasetLoaded { add { } remove { } }
        public event Action<string?>? StatusChanged { add { } remove { } }
        public void Initialize(IMapHost host, ViewerCommandSettings? options) { }
        public Task LoadAsync(DatasetEntry entry)
        {
            LoadCalls.Add(entry);
            return Task.CompletedTask;
        }
        public Task ReRenderAtTimeAsync(System.DateTime t, System.Threading.CancellationToken ct) => Task.CompletedTask;
        public Task ReRenderAllAsync() => Task.CompletedTask;
        public void RemoveEntry(DatasetEntry entry) { }
        public void SetEntryOrder(IReadOnlyList<DatasetEntry> orderedEntries) { OrderCalls.Add(orderedEntries); }
    }

    [Fact]
    public async Task LoadFromPathAsync_UnknownExtension_RaisesEvent_AndDoesNotAddEntry()
    {
        var loader = new RecordingLoader();
        var vm = new DatasetsViewModel(loader);
        string? observedExtension = null;
        vm.UnrecognizedFileEncountered += ext => observedExtension = ext;

        var result = await vm.LoadFromPathAsync("/tmp/something.xyz");

        Assert.Null(result);
        Assert.Equal(".xyz", observedExtension);
        Assert.Empty(vm.Entries);
        Assert.Empty(loader.LoadCalls);
    }

    [Fact]
    public async Task LoadFromPathAsync_KnownExtension_AddsEntryAndCallsLoader()
    {
        // S-101 ISO 8211 (.000) is detected purely by extension, so we don't
        // need the file to actually exist for the unit under test.
        var loader = new RecordingLoader();
        var vm = new DatasetsViewModel(loader);

        var result = await vm.LoadFromPathAsync("/tmp/sample.000");

        Assert.NotNull(result);
        Assert.Equal("S-101", result!.ProductSpec);
        Assert.Single(vm.Entries);
        Assert.Same(result, vm.Entries[0]);
        Assert.Single(loader.LoadCalls);
        Assert.Same(result, loader.LoadCalls[0]);
    }

    [Fact]
    public void Add_InsertsAtTopOfList()
    {
        // Photoshop/QGIS convention: newest layer goes to the top of
        // the list (index 0), which is the top of the paint stack.
        var loader = new RecordingLoader();
        var vm = new DatasetsViewModel(loader);

        var first = vm.Add("/tmp/a.000", "S-101");
        var second = vm.Add("/tmp/b.000", "S-101");

        Assert.Same(second, vm.Entries[0]);
        Assert.Same(first, vm.Entries[1]);
    }

    [Fact]
    public void MoveUp_DecrementsIndex_AndCallsSetEntryOrder()
    {
        var loader = new RecordingLoader();
        var vm = new DatasetsViewModel(loader);
        var a = vm.Add("/a.000", "S-101");
        var b = vm.Add("/b.000", "S-101"); // b at 0, a at 1
        loader.OrderCalls.Clear();

        vm.MoveUpCommand.Execute(a);

        // a moved from index 1 to index 0.
        Assert.Same(a, vm.Entries[0]);
        Assert.Same(b, vm.Entries[1]);
        Assert.Single(loader.OrderCalls);
        Assert.Equal(new[] { a, b }, loader.OrderCalls[0]);
    }

    [Fact]
    public void MoveUp_AtTop_IsNoOp()
    {
        var loader = new RecordingLoader();
        var vm = new DatasetsViewModel(loader);
        var a = vm.Add("/a.000", "S-101"); // a at 0
        loader.OrderCalls.Clear();

        vm.MoveUpCommand.Execute(a);

        Assert.Empty(loader.OrderCalls);
    }

    [Fact]
    public void BringToFront_MovesToIndexZero()
    {
        var loader = new RecordingLoader();
        var vm = new DatasetsViewModel(loader);
        var a = vm.Add("/a.000", "S-101");
        var b = vm.Add("/b.000", "S-101");
        var c = vm.Add("/c.000", "S-101"); // c, b, a

        vm.BringToFrontCommand.Execute(a);

        Assert.Same(a, vm.Entries[0]);
    }

    [Fact]
    public void SendToBack_MovesToLastIndex()
    {
        var loader = new RecordingLoader();
        var vm = new DatasetsViewModel(loader);
        var a = vm.Add("/a.000", "S-101");
        var b = vm.Add("/b.000", "S-101");
        var c = vm.Add("/c.000", "S-101"); // c, b, a

        vm.SendToBackCommand.Execute(c);

        Assert.Same(c, vm.Entries[^1]);
    }

    [Fact]
    public void ShowAll_SetsEveryEntryVisible()
    {
        var loader = new RecordingLoader();
        var vm = new DatasetsViewModel(loader);
        var a = vm.Add("/a.000", "S-101");
        var b = vm.Add("/b.000", "S-101");
        a.IsVisible = false;
        b.IsVisible = false;

        vm.ShowAllCommand.Execute(null);

        Assert.True(a.IsVisible);
        Assert.True(b.IsVisible);
    }

    [Fact]
    public void HideAll_SetsEveryEntryHidden()
    {
        var loader = new RecordingLoader();
        var vm = new DatasetsViewModel(loader);
        var a = vm.Add("/a.000", "S-101");
        var b = vm.Add("/b.000", "S-101");

        vm.HideAllCommand.Execute(null);

        Assert.False(a.IsVisible);
        Assert.False(b.IsVisible);
    }

    [Fact]
    public void Isolate_HidesEveryOtherEntry()
    {
        var loader = new RecordingLoader();
        var vm = new DatasetsViewModel(loader);
        var a = vm.Add("/a.000", "S-101");
        var b = vm.Add("/b.000", "S-101");
        var c = vm.Add("/c.000", "S-101");

        vm.IsolateCommand.Execute(b);

        Assert.False(a.IsVisible);
        Assert.True(b.IsVisible);
        Assert.False(c.IsVisible);
    }

    [Fact]
    public void ResetOpacity_RestoresUnityOnEntriesAndSubLayers()
    {
        var loader = new RecordingLoader();
        var vm = new DatasetsViewModel(loader);
        var a = vm.Add("/a.000", "S-101");
        a.Opacity = 0.3;
        var sub = new DatasetSubLayer("k", "K") { Opacity = 0.5 };
        a.SubLayers.Add(sub);

        vm.ResetOpacityCommand.Execute(null);

        Assert.Equal(1.0, a.Opacity);
        Assert.Equal(1.0, sub.Opacity);
    }
}
