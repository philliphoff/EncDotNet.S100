using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Viewer.Services;
using ExchangeSetProgress = EncDotNet.S100.Viewer.Services.ExchangeSetProgress;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui.Layers;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// End-to-end coverage of <see cref="ExchangeSetService"/> against
/// the synthetic CATALOG.XML fixtures under
/// <c>tests/datasets/ExchangeSets/</c>. These tests use a no-op
/// <see cref="IDatasetLoaderService"/> so the loader never opens the
/// (non-existent) referenced dataset files; we are exercising the
/// catalogue → entry dispatch + header lifecycle, not real
/// rasterisation.
/// </summary>
public class ExchangeSetServiceLoaderTests
{
    private static string FixturesRoot([CallerFilePath] string callerFilePath = "")
        => Path.Combine(
            Path.GetDirectoryName(callerFilePath)!,
            "..", "datasets", "ExchangeSets");

    private static string MixedFixture() =>
        Path.Combine(FixturesRoot(), "Synthetic-Mixed");

    private static string AllUnsupportedFixture() =>
        Path.Combine(FixturesRoot(), "Synthetic-AllUnsupported");

    private sealed class StubStatus : IStatusPresenter
    {
        public string? StatusText { get; set; }
        public event PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
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

    private static (DatasetsViewModel datasets, ExchangeSetService service) CreateSystem()
    {
        var datasets = new DatasetsViewModel(new NoopLoader());
        var service = new ExchangeSetService(datasets, new StubStatus(), new StubToastService());
        return (datasets, service);
    }

    [Fact]
    public async Task OpenAsync_MixedFixture_LoadsSupportedAndSkipsUnsupported()
    {
        var (datasets, service) = CreateSystem();
        using var _ = service;

        var result = await service.OpenAsync(MixedFixture());

        Assert.Equal(3, result.Total);
        Assert.Equal(2, result.Loaded);
        Assert.Equal(1, result.SkippedUnsupported);
        Assert.False(result.Cancelled);
        Assert.Single(result.SkipMessages);
        Assert.Contains("S-999", result.SkipMessages[0]);

        // Both supported entries are surfaced in the panel.
        Assert.Equal(2, datasets.Entries.Count);
        Assert.Contains(datasets.Entries, e => e.ProductSpec == "S-101");
        Assert.Contains(datasets.Entries, e => e.ProductSpec == "S-102");
    }

    [Fact]
    public async Task OpenAsync_MixedFixture_RegistersHeaderWithCatalogueMetadata()
    {
        var (datasets, service) = CreateSystem();
        using var _ = service;

        var result = await service.OpenAsync(MixedFixture());

        var header = Assert.Single(datasets.ExchangeSetHeaders);
        Assert.Equal("Synthetic Hydrographic Office", header.Producer);
        // Latest issueDate across the 3 datasets is 2026-01-12.
        Assert.Equal("2026-01-12", header.IssueDate);
        // Header reports the catalogue total, not the loaded count.
        Assert.Equal(3, header.DatasetCount);
        Assert.Equal("Synthetic-Mixed", header.DisplayName);
        Assert.Equal(MixedFixture(), header.SourcePath);
        // UnionBoundingBox is null because none of the fixtures declare one.
        Assert.Null(result.UnionBoundingBox);
    }

    [Fact]
    public async Task OpenAsync_MixedFixture_CloseCommand_RemovesEveryEntry_AndUnregistersHeader()
    {
        var (datasets, service) = CreateSystem();
        using var _ = service;

        await service.OpenAsync(MixedFixture());
        Assert.Equal(2, datasets.Entries.Count);
        var header = Assert.Single(datasets.ExchangeSetHeaders);

        header.CloseCommand.Execute(null);

        // CloseCommand removes every entry contributed by this set;
        // the service's collection-changed listener disposes the set
        // and unregisters the header in the same pass.
        Assert.Empty(datasets.Entries);
        Assert.Empty(datasets.ExchangeSetHeaders);
    }

    [Fact]
    public async Task OpenAsync_MixedFixture_RemovingEveryEntryByHand_AlsoRemovesHeader()
    {
        var (datasets, service) = CreateSystem();
        using var _ = service;

        await service.OpenAsync(MixedFixture());

        // Simulate the user removing each row individually rather than
        // using the header's Close button.
        foreach (var entry in datasets.Entries.ToArray())
        {
            datasets.Entries.Remove(entry);
        }

        Assert.Empty(datasets.Entries);
        Assert.Empty(datasets.ExchangeSetHeaders);
    }

    [Fact]
    public async Task OpenAsync_AllUnsupportedFixture_DisposesSetImmediately()
    {
        var (datasets, service) = CreateSystem();
        using var _ = service;

        var result = await service.OpenAsync(AllUnsupportedFixture());

        Assert.Equal(2, result.Total);
        Assert.Equal(0, result.Loaded);
        Assert.Equal(2, result.SkippedUnsupported);
        Assert.Equal(2, result.SkipMessages.Count);

        // No entries dispatched, and the header must be cleaned up so
        // the underlying file handle is released right away.
        Assert.Empty(datasets.Entries);
        Assert.Empty(datasets.ExchangeSetHeaders);
    }

    private sealed class SynchronousProgress<T> : IProgress<T>
    {
        public List<T> Reports { get; } = new();
        public void Report(T value) => Reports.Add(value);
    }

    [Fact]
    public async Task OpenAsync_MixedFixture_ProgressReportsEveryStep()
    {
        var (datasets, service) = CreateSystem();
        using var _ = service;
        var progress = new SynchronousProgress<ExchangeSetProgress>();

        await service.OpenAsync(MixedFixture(), progress);

        // Initial total + one per dataset (3) = 4 reports.
        Assert.Equal(4, progress.Reports.Count);
        Assert.Equal(3, progress.Reports[^1].Total);
        Assert.Equal(3, progress.Reports[^1].Completed);
        Assert.Equal(1, progress.Reports[^1].Failed);
    }
}
