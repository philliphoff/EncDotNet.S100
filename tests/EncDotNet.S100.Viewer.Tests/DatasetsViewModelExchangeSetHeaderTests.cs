using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui.Layers;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

public class DatasetsViewModelExchangeSetHeaderTests
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
        public event Action<string?>? StatusChanged { add { } remove { } }
        public void Initialize(IMapHost host, ViewerCommandSettings? options) { }
        public Task LoadAsync(DatasetEntry entry) => Task.CompletedTask;
        public Task ReRenderAtTimeAsync(DateTime t, CancellationToken ct) => Task.CompletedTask;
        public Task ReRenderAllAsync() => Task.CompletedTask;
        public void RemoveEntry(DatasetEntry entry) { }
        public void SetEntryOrder(IReadOnlyList<DatasetEntry> ordered) { }
    }

    private static DatasetsViewModel NewVm() => new(new NoopLoader());

    [Fact]
    public void RegisterExchangeSetHeader_AddsToCollection_AndReturnsSameInstance()
    {
        var vm = NewVm();
        var src = new StubAssetSource();

        var header = vm.RegisterExchangeSetHeader(
            src, "/tmp/eset", "ACME", "2024-05-01", 7, _ => { });

        Assert.Single(vm.ExchangeSetHeaders);
        Assert.Same(header, vm.ExchangeSetHeaders[0]);
        Assert.Equal("ACME", header.Producer);
        Assert.Equal("2024-05-01", header.IssueDate);
        Assert.Equal(7, header.DatasetCount);
        Assert.Equal("/tmp/eset", header.SourcePath);
    }

    [Fact]
    public void RemoveExchangeSetHeader_RemovesIt()
    {
        var vm = NewVm();
        var src = new StubAssetSource();
        var header = vm.RegisterExchangeSetHeader(src, "/p", null, null, 1, _ => { });

        vm.RemoveExchangeSetHeader(header);

        Assert.Empty(vm.ExchangeSetHeaders);
    }

    [Fact]
    public void RemoveExchangeSetHeader_IsIdempotent()
    {
        var vm = NewVm();
        var header = vm.RegisterExchangeSetHeader(
            new StubAssetSource(), "/p", null, null, 0, _ => { });

        vm.RemoveExchangeSetHeader(header);
        vm.RemoveExchangeSetHeader(header);

        Assert.Empty(vm.ExchangeSetHeaders);
    }

    [Fact]
    public void CloseCommand_InvokesCloseAction_WithSelf()
    {
        var vm = NewVm();
        ExchangeSetHeader? captured = null;
        var header = vm.RegisterExchangeSetHeader(
            new StubAssetSource(), "/p", null, null, 0, h => captured = h);

        Assert.True(header.CloseCommand.CanExecute(null));
        header.CloseCommand.Execute(null);

        Assert.Same(header, captured);
    }

    [Fact]
    public void DisplayName_DerivedFromSourcePath_FolderForm()
    {
        var vm = NewVm();
        var sep = Path.DirectorySeparatorChar;
        var path = $"{sep}data{sep}exchange{sep}MyEset";

        var header = vm.RegisterExchangeSetHeader(
            new StubAssetSource(), path, null, null, 0, _ => { });

        Assert.Equal("MyEset", header.DisplayName);
    }

    [Fact]
    public void DisplayName_DerivedFromSourcePath_ZipForm()
    {
        var vm = NewVm();
        var path = Path.Combine(Path.GetTempPath(), "MyEset.zip");

        var header = vm.RegisterExchangeSetHeader(
            new StubAssetSource(), path, null, null, 0, _ => { });

        Assert.Equal("MyEset.zip", header.DisplayName);
    }

    [Fact]
    public void RegisterExchangeSetHeader_RejectsNullArgs()
    {
        var vm = NewVm();
        var src = new StubAssetSource();

        Assert.Throws<ArgumentNullException>(() =>
            vm.RegisterExchangeSetHeader(null!, "/p", null, null, 0, _ => { }));
        Assert.Throws<ArgumentException>(() =>
            vm.RegisterExchangeSetHeader(src, "", null, null, 0, _ => { }));
        Assert.Throws<ArgumentNullException>(() =>
            vm.RegisterExchangeSetHeader(src, "/p", null, null, 0, null!));
    }
}
