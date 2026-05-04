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
        public Task ReRenderTimeStepAsync(DatasetEntry entry) => Task.CompletedTask;
        public Task ReRenderAllAsync() => Task.CompletedTask;
        public void RemoveEntry(DatasetEntry entry) { }
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
}
