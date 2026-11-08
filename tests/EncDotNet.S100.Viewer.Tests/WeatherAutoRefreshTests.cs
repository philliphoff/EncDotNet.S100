using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Collections.Weather;
using EncDotNet.S100.Viewer.Library;
using EncDotNet.S100.Viewer.ViewModels;
using Microsoft.Extensions.Time.Testing;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Issue #1301 (V27): a weather source that refreshes on its own — WPC's
/// surface analysis, every 3 hours — is re-read once that long has passed
/// while its latest item is loaded, and the newer analysis is downloaded and
/// loaded in its place; one that isn't loaded is left alone.
/// </summary>
public sealed class WeatherAutoRefreshTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 11, 4, 13, 20, 0, TimeSpan.Zero);

    private readonly LibraryTestContext _context = new();
    private readonly FakeTimeProvider _time = new(Start);
    private readonly Indexer _indexer;
    private readonly Downloader _downloader = new();
    private readonly Loader _loader = new();
    private readonly CollectionLibrary _library;

    public WeatherAutoRefreshTests()
    {
        _indexer = new Indexer(_time);
        _library = _context.CreateService(new CollectionIndexer([_indexer]));
    }

    public void Dispose()
    {
        _library.Dispose();
        _context.Dispose();
    }

    private async Task<(LibraryPanelViewModel Panel, Guid SourceId)> PanelAsync()
    {
        var known = KnownCatalogueSources.Find("noaa-wpc-surface")!;
        var source = new WeatherFeedSource(Guid.NewGuid(), null, known.CatalogUri, known.Weather!, new WeatherSelection());
        _library.Initialize();
        _library.AddCollection("Weather", [source]);
        await _library.WhenIdle();
        var panel = new LibraryPanelViewModel(_library, new NoImporter(), _loader, _downloader, action => action(), time: _time, coalesce: action => action());
        panel.Sync();
        return (panel, source.Id);
    }

    [Fact]
    public async Task A_loaded_analysis_is_replaced_by_the_newer_one_3_hours_after_the_last_read()
    {
        _downloader.Local = Start.AddHours(-1).AddMinutes(-20); // the 12Z analysis, on disk and loaded
        _loader.Loaded = true;
        var (panel, _) = await PanelAsync();
        using var disposePanel = panel;
        Assert.Equal(1, _indexer.Reads);

        // Two hours on: not due, on the Library's minute clock or when asked.
        _time.Advance(TimeSpan.FromHours(2));
        Assert.Empty(await panel.RefreshDueWeatherAsync());
        Assert.Equal(1, _indexer.Reads);

        // Three hours on: the minute clock re-reads the source and replaces the analysis.
        _time.Advance(TimeSpan.FromHours(1));
        await WaitForAsync(() => _downloader.Requested.Count > 0);

        // Re-read once when due (and once more after the download, as any download does).
        Assert.True(_indexer.Reads >= 2);
        Assert.Equal(["wpc-analysis"], _downloader.Requested);
        Assert.Empty(await panel.RefreshDueWeatherAsync());
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
            await Task.Delay(25, TestContext.Current.CancellationToken);
        Assert.True(condition());
    }

    [Fact]
    public async Task An_analysis_that_isnt_loaded_isnt_refreshed()
    {
        _downloader.Local = Start.AddHours(-1).AddMinutes(-20);
        var (panel, _) = await PanelAsync();
        using var disposePanel = panel;

        _time.Advance(TimeSpan.FromHours(4));

        Assert.Empty(await panel.RefreshDueWeatherAsync());
        Assert.Equal(1, _indexer.Reads);
        Assert.Empty(_downloader.Requested);
    }

    /// <summary>Lists the latest analysis published at the clock: 12Z at the start, 15Z three hours on.</summary>
    private sealed class Indexer(TimeProvider time) : ICollectionSourceIndexer
    {
        public int Reads { get; private set; }

        public bool CanIndex(CollectionSource source) => source is WeatherFeedSource;

        public ValueTask<string?> GetFingerprintAsync(CollectionSource source, CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>(null);

        public ValueTask<SourceIndex> IndexAsync(CollectionSource source, IProgress<IndexProgress>? progress, CancellationToken cancellationToken)
        {
            Reads++;
            // Published about an hour after its synoptic time.
            var now = time.GetUtcNow();
            var published = now.AddHours(-1);
            var run = new DateTimeOffset(published.Year, published.Month, published.Day, published.Hour - (published.Hour % 3), 0, 0, TimeSpan.Zero);
            var item = new CollectionItem
            {
                Key = "wpc-analysis",
                ProductSpec = "CODSUS",
                Name = "wpc-analysis",
                Title = "Surface analysis · WPC",
                Bounds = WeatherFeedIndexer.WpcDomain,
                Location = new RemoteItemLocation(new Uri("https://tgftp.nws.noaa.gov/data/raw/as/asus02.kwbc.cod.sus.txt"), null, run),
                Properties = new Dictionary<string, string>
                {
                    [S100ForecastFeedIndexer.RunProperty] = run.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    [WeatherRuns.StaleAfterProperty] = "6",
                    [WeatherFeedIndexer.BulletinKindProperty] = "analysis",
                },
            };
            return ValueTask.FromResult(new SourceIndex(source.Id, now, null, [item], []));
        }
    }

    /// <summary>Has the analysis of <see cref="Local"/> on disk; a later run listed is outdated.</summary>
    private sealed class Downloader : ILibraryDownloader
    {
        public DateTimeOffset? Local { get; set; }

        public List<string> Requested { get; } = [];

        public event EventHandler? Changed { add { } remove { } }

        public CollectionItem Localize(CollectionItem item) => Local is null
            ? item
            : item with { Location = new LocalItemLocation("/tmp/wpc", item.Name + ".txt", []) };

        public bool IsOutdated(CollectionItem item) =>
            Local is { } local && S100ForecastFeedIndexer.RunOf(item) is { } run && run > local;

        public DateTimeOffset? LocalPublishedAtOf(CollectionItem item) => Local;

        public bool CanDownload(CollectionItem item) => item.Location is RemoteItemLocation;

        public Task<LibraryDownloadResult> DownloadAsync(IReadOnlyList<CollectionItem> items, CancellationToken cancellationToken = default)
        {
            Requested.AddRange(items.Select(i => i.Name));
            return Task.FromResult(new LibraryDownloadResult(items.Count, 0, false));
        }
    }

    private sealed class Loader : ILibraryLoader
    {
        public bool Loaded { get; set; }

        public event EventHandler? Changed { add { } remove { } }

        public LibraryLoadState StateOf(CollectionItem item) =>
            Loaded && item.Location is LocalItemLocation ? LibraryLoadState.Loaded : LibraryLoadState.None;

        public Task<LibraryLoadResult> LoadAsync(IReadOnlyList<CollectionItem> items, bool defer, CancellationToken cancellationToken = default) =>
            Task.FromResult(new LibraryLoadResult(items.Count, 0));
    }

    private sealed class NoImporter : ILibraryImporter
    {
        public Task AddFolderAsync(Guid? targetCollectionId) => Task.CompletedTask;

        public Task AddExchangeSetZipAsync(Guid? targetCollectionId) => Task.CompletedTask;

        public Task AddOnlineCatalogueAsync(Guid? targetCollectionId) => Task.CompletedTask;

        public Task AddSharedFeedAsync(Guid? targetCollectionId) => Task.CompletedTask;

        public Task AddKnownCatalogueAsync(KnownCatalogueSource source, Guid? targetCollectionId) => Task.CompletedTask;

        public Task AddS128CatalogueAsync(Guid? targetCollectionId) => Task.CompletedTask;

        public Task AddCollectionManifestAsync(Guid? targetCollectionId) => Task.CompletedTask;

        public Task ChooseManifestGroupsAsync(Guid collectionId, LocalManifestSource source) => Task.CompletedTask;

        public Task AddCurrentsForAreaAsync(GeoBounds area, Guid? targetCollectionId) => Task.CompletedTask;

        public Task AddPathAsync(string path, Guid? targetCollectionId) => Task.CompletedTask;

        public bool IsInLibrary(string path) => false;
    }
}
