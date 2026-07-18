using System.Runtime.CompilerServices;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui.Layers;
using ExchangeSetProgress = EncDotNet.S100.Viewer.Services.ExchangeSetProgress;

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

    private static string S101UpdatesFixture() =>
        Path.Combine(FixturesRoot(), "Synthetic-S101Updates");

    private static string S101OrphanFixture() =>
        Path.Combine(FixturesRoot(), "Synthetic-S101Orphan");

    private static string S411NoProductIdFixture() =>
        Path.Combine(FixturesRoot(), "Synthetic-S411NoProductId");

    private static string FramedFixture() =>
        Path.Combine(FixturesRoot(), "Synthetic-Framed");

    private static string S57FramedFixture() =>
        Path.Combine(FixturesRoot(), "Synthetic-S57-Framed");

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

    private static (DatasetsViewModel datasets, ExchangeSetService service) CreateSystem()
    {
        var datasets = new DatasetsViewModel(new NoopLoader());
        var service = new ExchangeSetService(datasets, Notifications.TestNotifications.Create());
        return (datasets, service);
    }

    [Fact]
    public async Task OpenAsync_S57FramedFixture_ReopenServesCatalogueFromCache()
    {
        var cacheDir = Path.Combine(Path.GetTempPath(), "s57cat-e2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cacheDir);
        try
        {
            var cache = new Services.Caching.DiskS57CatalogCache(cacheDir, 1_000_000);

            var datasets1 = new DatasetsViewModel(new NoopLoader());
            using (var service1 = new ExchangeSetService(
                datasets1, Notifications.TestNotifications.Create(), s57CatalogCache: cache))
            {
                var first = await service1.OpenAsync(S57FramedFixture());
                Assert.Equal(2, datasets1.Entries.Count);
                Assert.NotNull(first.UnionBoundingBox);
            }

            Assert.Equal(1, cache.Misses);
            Assert.Equal(0, cache.Hits);

            // A second, independent open of the same set must serve the
            // catalogue descriptors from the sidecar (no re-parse) and still
            // produce the same entries + framing.
            var datasets2 = new DatasetsViewModel(new NoopLoader());
            using (var service2 = new ExchangeSetService(
                datasets2, Notifications.TestNotifications.Create(), s57CatalogCache: cache))
            {
                var second = await service2.OpenAsync(S57FramedFixture());
                Assert.Equal(2, datasets2.Entries.Count);
                Assert.NotNull(second.UnionBoundingBox);
                Assert.Equal(-123.0, second.UnionBoundingBox!.WestBoundLongitude);
            }

            Assert.Equal(1, cache.Hits);
            Assert.Equal(1, cache.Misses);
        }
        finally
        {
            try { Directory.Delete(cacheDir, recursive: true); } catch { /* best effort */ }
        }
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

    /// <summary>
    /// Reproduces the real-world JCOMM S-411 (Canadian Ice Service) exchange
    /// set whose <c>productSpecification</c> declares only a human-readable
    /// <c>name</c> ("Ice Information Product Specification (JCOMM S-411)") with
    /// no <c>productIdentifier</c> or number. The declared spec cannot be
    /// mapped, so the loader must content-sniff the GML root element
    /// (<c>ice:IceDataSet</c>) and still dispatch the dataset as S-411 rather
    /// than skipping it as unsupported.
    /// </summary>
    [Fact]
    public async Task OpenAsync_S411NoProductId_ContentSniffsAndLoadsAsS411()
    {
        var (datasets, service) = CreateSystem();
        using var _ = service;

        var result = await service.OpenAsync(S411NoProductIdFixture());

        Assert.Equal(1, result.Total);
        Assert.Equal(1, result.Loaded);
        Assert.Equal(0, result.SkippedUnsupported);
        Assert.False(result.Cancelled);
        Assert.Empty(result.SkipMessages);

        var entry = Assert.Single(datasets.Entries);
        Assert.Equal("S-411", entry.ProductSpec);
        Assert.Equal("S-411/ice.gml", entry.RelativePath);
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

    [Fact]
    public async Task OpenAsync_FramedFixture_InvokesOnFramingReady_WithUnionBoundingBox()
    {
        var (datasets, service) = CreateSystem();
        using var _ = service;

        EncDotNet.S100.ExchangeSets.BoundingBox? framed = null;
        var framedBeforeReturn = false;
        int? entryCountWhenFramed = null;

        var result = await service.OpenAsync(
            FramedFixture(),
            onFramingReady: bbox =>
            {
                framed = bbox;
                framedBeforeReturn = true;
                // Capture how many datasets have been dispatched at the
                // instant framing is emitted — it must be zero.
                entryCountWhenFramed = datasets.Entries.Count;
            });

        // The callback fired with the union of both dataset boxes,
        // computed up front from catalogue metadata (issue #448).
        Assert.True(framedBeforeReturn);
        Assert.NotNull(framed);
        Assert.Equal(10, framed!.WestBoundLongitude);
        Assert.Equal(14, framed.EastBoundLongitude);
        Assert.Equal(48, framed.SouthBoundLatitude);
        Assert.Equal(52, framed.NorthBoundLatitude);

        // Timing guarantee: framing must be emitted *before* any dataset
        // is dispatched, so early per-dataset paints land in the framed
        // viewport (issue #448). This fails if framing is moved after the
        // dispatch loop. The fixture declares two datasets that are both
        // surfaced as entries by the end of the open.
        Assert.Equal(0, entryCountWhenFramed);
        Assert.Equal(2, datasets.Entries.Count);

        // The same union is echoed on the result.
        Assert.NotNull(result.UnionBoundingBox);
        Assert.Equal(10, result.UnionBoundingBox!.WestBoundLongitude);
        Assert.Equal(14, result.UnionBoundingBox.EastBoundLongitude);
        Assert.Equal(48, result.UnionBoundingBox.SouthBoundLatitude);
        Assert.Equal(52, result.UnionBoundingBox.NorthBoundLatitude);
    }

    [Fact]
    public async Task OpenAsync_S57FramedFixture_InvokesOnFramingReady_WithUnionBoundingBox()
    {
        var (datasets, service) = CreateSystem();
        using var _ = service;

        EncDotNet.S100.ExchangeSets.BoundingBox? framed = null;
        int? entryCountWhenFramed = null;

        var result = await service.OpenAsync(
            S57FramedFixture(),
            onFramingReady: bbox =>
            {
                framed = bbox;
                entryCountWhenFramed = datasets.Entries.Count;
            });

        Assert.NotNull(framed);
        Assert.Equal(0, entryCountWhenFramed);
        Assert.Equal(2, datasets.Entries.Count);
        Assert.All(datasets.Entries, e => Assert.Equal("S-57", e.ProductSpec));

        Assert.NotNull(result.UnionBoundingBox);
        Assert.Equal(result.UnionBoundingBox!.WestBoundLongitude, framed!.WestBoundLongitude);
        Assert.Equal(result.UnionBoundingBox.EastBoundLongitude, framed.EastBoundLongitude);
        Assert.Equal(result.UnionBoundingBox.SouthBoundLatitude, framed.SouthBoundLatitude);
        Assert.Equal(result.UnionBoundingBox.NorthBoundLatitude, framed.NorthBoundLatitude);

        Assert.Equal(-123.0, framed.WestBoundLongitude);
        Assert.Equal(-121.5, framed.EastBoundLongitude);
        Assert.Equal(47.5, framed.SouthBoundLatitude);
        Assert.Equal(49.0, framed.NorthBoundLatitude);
    }

    [Fact]
    public async Task OpenAsync_MixedFixture_DoesNotInvokeOnFramingReady_WhenNoBoundingBox()
    {
        var (_, service) = CreateSystem();
        using var _ = service;

        var invoked = false;

        var result = await service.OpenAsync(
            MixedFixture(),
            onFramingReady: _ => invoked = true);

        // No dataset declares a bounding box, so there is nothing to
        // frame early; the caller falls back to its debounce path.
        Assert.False(invoked);
        Assert.Null(result.UnionBoundingBox);
    }

    [Fact]
    public async Task OpenAsync_S101Updates_CollapsesBaseAndUpdatesIntoOneEntry()
    {
        var (datasets, service) = CreateSystem();
        using var _ = service;

        var result = await service.OpenAsync(S101UpdatesFixture());

        // The base cell plus its two updates form a single load item.
        Assert.Equal(1, result.Total);
        Assert.Equal(1, result.Loaded);
        Assert.Equal(0, result.SkippedUnsupported);
        Assert.False(result.Cancelled);

        var entry = Assert.Single(datasets.Entries);
        Assert.Equal("S-101", entry.ProductSpec);
        // The base cell backs the entry; the two updates are carried as
        // ordered relative paths for the loader to apply.
        Assert.Equal("S-101/SYNTH101.000", entry.RelativePath);
        Assert.True(entry.HasUpdates);
        Assert.Equal(
            new[] { "S-101/SYNTH101.001", "S-101/SYNTH101.002" },
            entry.UpdateRelativePaths);
    }

    [Fact]
    public async Task OpenAsync_S101Updates_HeaderCountsCollapsedCell()
    {
        var (datasets, service) = CreateSystem();
        using var _ = service;

        await service.OpenAsync(S101UpdatesFixture());

        var header = Assert.Single(datasets.ExchangeSetHeaders);
        // Three catalogue entries collapse to one renderable cell.
        Assert.Equal(1, header.DatasetCount);
        Assert.Equal(1, header.LoadedCount);
        Assert.Equal(0, header.UnsupportedCount);
    }

    [Fact]
    public async Task OpenAsync_S101Orphan_SkipsUpdateWithNoBase()
    {
        var (datasets, service) = CreateSystem();
        using var _ = service;

        var result = await service.OpenAsync(S101OrphanFixture());

        // An orphan update cannot be applied on its own; it is skipped
        // best-effort with a warning and dispatches no dataset entry.
        Assert.Equal(1, result.Total);
        Assert.Equal(0, result.Loaded);
        Assert.Equal(1, result.SkippedUnsupported);
        Assert.Single(result.SkipMessages);
        Assert.Contains("SYNTH101.001", result.SkipMessages[0]);

        // No entry dispatched, so the set is released immediately.
        Assert.Empty(datasets.Entries);
        Assert.Empty(datasets.ExchangeSetHeaders);
    }

    /// <summary>
    /// End-to-end S-57 exchange-set load against a real <c>CATALOG.031</c>
    /// when one is available via the <c>ENCDOTNET_S57_EXCHANGE_SET</c>
    /// environment variable (the folder containing <c>CATALOG.031</c>).
    /// Skipped otherwise so CI never depends on (or commits) real ENC data.
    /// </summary>
    [SkippableFact]
    public async Task OpenAsync_RealS57ExchangeSet_DispatchesCellsAsS57Entries()
    {
        var root = Environment.GetEnvironmentVariable("ENCDOTNET_S57_EXCHANGE_SET");
        Skip.If(string.IsNullOrEmpty(root), "ENCDOTNET_S57_EXCHANGE_SET not set.");
        Skip.IfNot(
            File.Exists(Path.Combine(root!, "CATALOG.031")),
            $"No CATALOG.031 in {root}.");

        var (datasets, service) = CreateSystem();
        using var _ = service;

        var result = await service.OpenAsync(root!);

        Assert.True(result.Loaded > 0);
        Assert.Equal(result.Total, result.Loaded);
        Assert.Equal(0, result.SkippedUnsupported);
        Assert.All(datasets.Entries, e => Assert.Equal("S-57", e.ProductSpec));

        var header = Assert.Single(datasets.ExchangeSetHeaders);
        Assert.Equal(ExchangeSetDetection.ResolveS57Root(root!), header.SourcePath);
        Assert.Equal(result.Loaded, header.LoadedCount);
    }

    // ── Catalogue-less loose-cell folders (issue #449) ────────────────

    private static string MakeLooseCellFolder(string name, params string[] files)
    {
        var folder = Path.Combine(
            Path.GetTempPath(), $"loose-{name}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        foreach (var file in files)
            File.WriteAllText(Path.Combine(folder, file), "synthetic");
        return folder;
    }

    [Fact]
    public async Task OpenAsync_LooseCellFolder_LoadsBaseWithFilesystemUpdates()
    {
        // A catalogue-less folder: a base cell plus its two sequential
        // updates, no CATALOG.XML / CATALOG.031. The fake ENC bytes fail
        // the S-57 DSPM sniff, so the cell is dispatched as S-101 (the
        // NoopLoader never parses it).
        var folder = MakeLooseCellFolder(
            "updates", "US5WA01M.000", "US5WA01M.001", "US5WA01M.002");
        try
        {
            var (datasets, service) = CreateSystem();
            using var _ = service;

            var result = await service.OpenAsync(folder);

            Assert.Equal(1, result.Total);
            Assert.Equal(1, result.Loaded);
            Assert.Equal(0, result.SkippedUnsupported);
            Assert.False(result.Cancelled);
            Assert.Null(result.UnionBoundingBox);

            var entry = Assert.Single(datasets.Entries);
            Assert.Equal("S-101", entry.ProductSpec);
            Assert.Equal("US5WA01M.000", entry.RelativePath);
            Assert.True(entry.IsFromExchangeSet);
            Assert.True(entry.HasUpdates);
            Assert.Equal(
                new[] { "US5WA01M.001", "US5WA01M.002" },
                entry.UpdateRelativePaths);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task OpenAsync_LooseCellFolder_LoadsEveryBaseCell()
    {
        var folder = MakeLooseCellFolder(
            "multi", "US5WA01M.000", "US5WA02M.000", "US5WA02M.001");
        try
        {
            var (datasets, service) = CreateSystem();
            using var _ = service;

            var result = await service.OpenAsync(folder);

            Assert.Equal(2, result.Total);
            Assert.Equal(2, result.Loaded);
            Assert.Equal(2, datasets.Entries.Count);

            // The second cell carries its update; the first has none.
            var cell2 = Assert.Single(
                datasets.Entries, e => e.RelativePath == "US5WA02M.000");
            Assert.Equal(new[] { "US5WA02M.001" }, cell2.UpdateRelativePaths);
            var cell1 = Assert.Single(
                datasets.Entries, e => e.RelativePath == "US5WA01M.000");
            Assert.False(cell1.HasUpdates);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task OpenAsync_LooseCellFolder_RegistersHeaderAndClosesCleanly()
    {
        var folder = MakeLooseCellFolder("header", "US5WA01M.000");
        try
        {
            var (datasets, service) = CreateSystem();
            using var _ = service;

            await service.OpenAsync(folder);

            var header = Assert.Single(datasets.ExchangeSetHeaders);
            Assert.Equal(folder, header.SourcePath);
            Assert.Equal(1, header.LoadedCount);

            header.CloseCommand.Execute(null);

            Assert.Empty(datasets.Entries);
            Assert.Empty(datasets.ExchangeSetHeaders);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
