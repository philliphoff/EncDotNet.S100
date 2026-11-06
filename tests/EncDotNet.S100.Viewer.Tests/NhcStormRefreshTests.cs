using System.Globalization;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Collections.Weather;
using EncDotNet.S100.Datasets.TropicalCyclones;
using EncDotNet.S100.Viewer.Library;
using EncDotNet.S100.Viewer.Services.Notifications;
using EncDotNet.S100.Viewer.Tests.Notifications;
using EncDotNet.S100.Viewer.ViewModels;
using Microsoft.Extensions.Time.Testing;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Issue #1298 (V27, V28, decision Q4, §7): NHC's storms in the Library. The
/// source is re-read hourly; a new advisory for a loaded storm raises one
/// Information notification with Load latest run as its action, and isn't
/// loaded on its own; a storm that isn't loaded, or newly active, doesn't
/// notify; a loaded storm NHC stops listing is marked final and reopened.
/// The storm's row is named by the storm and says its advisory.
/// </summary>
public sealed class NhcStormRefreshTests : IDisposable
{
    // Ian's advisory 20: 03:00 UTC 28 Sep 2022.
    private static readonly DateTimeOffset Issued = new(2022, 9, 28, 3, 0, 0, TimeSpan.Zero);

    private readonly LibraryTestContext _context = new();
    private readonly FakeTimeProvider _time = new(Issued.AddMinutes(20));
    private readonly Indexer _indexer;
    private readonly Downloader _downloader;
    private readonly Loader _loader = new();
    private readonly NotificationService _notifications = TestNotifications.Create();
    private readonly CollectionLibrary _library;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "nhc-refresh-" + Guid.NewGuid().ToString("N"));

    public NhcStormRefreshTests()
    {
        _downloader = new Downloader(_root);
        _indexer = new Indexer(_time);
        _library = _context.CreateService(new CollectionIndexer([_indexer]));
    }

    public void Dispose()
    {
        _library.Dispose();
        _context.Dispose();
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private async Task<(LibraryPanelViewModel Panel, Guid SourceId)> PanelAsync()
    {
        var known = KnownCatalogueSources.Find("noaa-nhc-storms")!;
        var source = new WeatherFeedSource(Guid.NewGuid(), null, known.CatalogUri, known.Weather!, new WeatherSelection());
        _library.Initialize();
        _library.AddCollection("Weather", [source]);
        await _library.WhenIdle();
        var panel = new LibraryPanelViewModel(_library, new NoImporter(), _loader, _downloader, action => action(), notifications: _notifications, time: _time);
        panel.Sync();
        return (panel, source.Id);
    }

    private static CollectionItem Storm(
        string key, string name, string advisory, DateTimeOffset issued, string wind = "105", string classification = "HU", string latitude = "24.1") =>
        new()
        {
            Key = key,
            ProductSpec = "TC",
            Name = key,
            Title = $"{name} ({key.ToUpperInvariant()}), advisory {advisory}",
            Location = new RemoteItemLocation(new Uri($"https://www.nhc.noaa.gov/gis/forecast/archive/{key}_5day_{advisory.PadLeft(3, '0')}.zip"), null, issued),
            Properties = new Dictionary<string, string>
            {
                [S100ForecastFeedIndexer.RunProperty] = issued.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                [S100ForecastFeedIndexer.ValidToProperty] = issued.AddHours(117).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                [WeatherRuns.StaleAfterProperty] = "7",
                [WeatherRuns.ServiceProperty] = "nhcStorms",
                [WeatherFeedIndexer.AdvisoryProperty] = advisory,
                [WeatherFeedIndexer.StormNameProperty] = name,
                [WeatherFeedIndexer.StormClassProperty] = classification,
                [WeatherFeedIndexer.IntensityProperty] = wind,
                [WeatherFeedIndexer.LatitudeProperty] = latitude,
                [WeatherFeedIndexer.BasinProperty] = "Atlantic",
            },
        };

    [Fact]
    public async Task A_new_advisory_for_a_loaded_storm_is_announced_once_with_Load_latest_run_and_not_loaded()
    {
        _indexer.Storms = [Storm("al092022", "Ian", "20", Issued)];
        _downloader.Local["al092022"] = (Issued, "020");
        _loader.Loaded.Add("al092022");
        var (panel, _) = await PanelAsync();
        using var disposePanel = panel;

        // NHC issues advisory 21; an hour on, the source is due.
        _indexer.Storms = [Storm("al092022", "Ian", "21", Issued.AddHours(6), wind: "110")];
        _time.Advance(TimeSpan.FromHours(1));
        await RefreshAsync(panel, () => _notifications.Active.Count > 0);

        var notification = Assert.Single(_notifications.Active);
        Assert.Equal("HURRICANE IAN: advisory 21 is available", notification.Title);
        Assert.Equal(NotificationSeverity.Info, notification.Severity);
        Assert.Contains("Not for navigation", notification.Message, StringComparison.Ordinal);
        var action = Assert.Single(notification.Actions);
        Assert.Equal("Load latest run", action.Label);
        Assert.True(action.IsPrimary);
        Assert.Empty(_downloader.Requested);

        // Read again later: the same advisory isn't announced twice.
        var reads = _indexer.Reads;
        _time.Advance(TimeSpan.FromHours(1));
        await RefreshAsync(panel, () => _indexer.Reads > reads);
        Assert.Single(_notifications.Active);

        // Its action downloads the new advisory, which replaces the old one.
        action.Command.Execute(null);
        await WaitForAsync(() => _downloader.Requested.Count > 0);
        Assert.Equal(["al092022"], _downloader.Requested);
    }

    [Fact]
    public async Task A_storm_that_isnt_loaded_or_a_storm_newly_active_isnt_announced()
    {
        _indexer.Storms = [Storm("al092022", "Ian", "20", Issued)];
        _downloader.Local["al092022"] = (Issued, "020");
        var (panel, _) = await PanelAsync();
        using var disposePanel = panel;

        _indexer.Storms = [Storm("al092022", "Ian", "21", Issued.AddHours(6)), Storm("al102022", "Julia", "1", Issued.AddHours(6))];
        _library.Refresh();
        await _library.WhenIdle();
        _time.Advance(TimeSpan.FromHours(2));
        await panel.RefreshDueWeatherAsync();

        Assert.Empty(_notifications.Active);
        Assert.Empty(_downloader.Requested);
    }

    [Fact]
    public async Task A_loaded_storm_NHC_stops_listing_is_marked_final_and_reopened()
    {
        _indexer.Storms = [Storm("al092022", "Ian", "20", Issued)];
        _downloader.Local["al092022"] = (Issued, "020");
        _loader.Loaded.Add("al092022");
        var (panel, _) = await PanelAsync();
        using var disposePanel = panel;

        _indexer.Storms = [];
        _time.Advance(TimeSpan.FromHours(1));
        var marker = Path.Combine(_root, "al092022", NhcAdvisoryFiles.FinalMarkerName("al092022"));
        await RefreshAsync(panel, () => _loader.Opened.Count > 0);

        Assert.True(File.Exists(marker));
        Assert.Equal(["al092022"], _loader.Closed);
        Assert.Equal(["al092022"], _loader.Opened);
        Assert.Empty(_notifications.Active);
    }

    [Fact]
    public async Task A_storm_isnt_marked_final_while_NHC_cant_be_reached()
    {
        _indexer.Storms = [Storm("al092022", "Ian", "20", Issued)];
        _downloader.Local["al092022"] = (Issued, "020");
        _loader.Loaded.Add("al092022");
        var (panel, _) = await PanelAsync();
        using var disposePanel = panel;

        _indexer.Storms = [];
        _indexer.Failing = true;
        var reads = _indexer.Reads;
        _time.Advance(TimeSpan.FromHours(1));
        await RefreshAsync(panel, () => _indexer.Reads > reads);

        Assert.False(Directory.Exists(Path.Combine(_root, "al092022")) && File.Exists(Path.Combine(_root, "al092022", NhcAdvisoryFiles.FinalMarkerName("al092022"))));
        Assert.Empty(_loader.Closed);
    }

    [Fact]
    public async Task A_storms_row_is_named_by_the_storm_and_says_its_advisory_forecast_and_age()
    {
        _indexer.Storms = [Storm("al092022", "Ian", "21", Issued.AddHours(6), wind: "110")];
        _downloader.Local["al092022"] = (Issued, "020");
        var (panel, sourceId) = await PanelAsync();
        using var disposePanel = panel;
        var source = _library.Collections.SelectMany(c => c.Sources).Single(s => s.Id == sourceId);
        var item = Assert.Single(source.Index!.Items);
        var row = new LibraryItemViewModel(item, source, _loader.StateOf, _downloader, "Weather", time: _time);

        // The source says how many storms are active (V27).
        Assert.Equal("1 active", panel.Nodes.Single().StatusLine);

        Assert.True(row.IsStorm);
        Assert.False(row.ShowsOutlineSwatch);
        Assert.False(row.IsNameMono);
        Assert.Equal("HURRICANE IAN · AL09", row.Name);
        // Advisory 20 is downloaded and NHC lists 21.
        Assert.StartsWith("Advisory 20 · ", row.Subtitle, StringComparison.Ordinal);
        Assert.EndsWith(" · advisory 21 available", row.Subtitle, StringComparison.Ordinal);
        Assert.StartsWith("Forecast to ", row.Summary, StringComparison.Ordinal);
        Assert.EndsWith(" · 110 kn", row.Summary, StringComparison.Ordinal);
        Assert.True(row.HasForecastWindow);
        // 110 kt is category 3: a major hurricane.
        Assert.Equal("WXTCMN", StormItemText.Symbol(item));

        // Eight hours after advisory 20, it's stale.
        _time.Advance(TimeSpan.FromHours(8));
        var stale = new LibraryItemViewModel(item, source, _loader.StateOf, _downloader, "Weather", time: _time);
        Assert.True(stale.IsStaleRun);
        Assert.Equal("Advisory 20 is over 7 hours old", stale.StaleRunText);
    }

    [Theory]
    [InlineData("HU", "64", "WXTCHN")]
    [InlineData("HU", "115", "WXTCMN")]
    [InlineData("TS", "50", "WXTCSN")]
    [InlineData("TD", "30", "WXTCDN")]
    [InlineData("PTC", "40", "WXTCPTN")]
    public void The_swatch_is_the_storms_class_symbol(string classification, string wind, string symbol)
    {
        var item = Storm("al092022", "Ian", "20", Issued, wind, classification);

        Assert.Equal(symbol, StormItemText.Symbol(item));
    }

    [Fact]
    public void A_southern_storm_has_the_mirrored_symbol_and_a_storm_without_a_newer_advisory_says_when_the_next_is_due()
    {
        var item = Storm("al092022", "Ian", "20", Issued, wind: "80", latitude: "-14.0");

        Assert.Equal("WXTCHS", StormItemText.Symbol(item));
        var subtitle = StormItemText.Subtitle(item, item, Issued, newerListed: false, t => t.UtcDateTime.ToString("HH:mm", CultureInfo.InvariantCulture));
        Assert.Equal("Advisory 20 · 03:00 · next 09:00", subtitle);
    }

    /// <summary>
    /// Refreshes what's due (the panel's minute clock may already be doing so
    /// after the clock moved) and waits for <paramref name="condition"/>.
    /// </summary>
    private async Task RefreshAsync(LibraryPanelViewModel panel, Func<bool> condition)
    {
        await panel.RefreshDueWeatherAsync();
        await WaitForAsync(condition);
        await _library.WhenIdle();
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
            await Task.Delay(25, TestContext.Current.CancellationToken);
        Assert.True(condition());
    }

    /// <summary>Lists <see cref="Storms"/>; with <see cref="Failing"/>, an error and nothing listed.</summary>
    private sealed class Indexer(TimeProvider time) : ICollectionSourceIndexer
    {
        public IReadOnlyList<CollectionItem> Storms { get; set; } = [];

        public int Reads { get; private set; }

        public bool Failing { get; set; }

        public bool CanIndex(CollectionSource source) => source is WeatherFeedSource;

        public ValueTask<string?> GetFingerprintAsync(CollectionSource source, CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>(null);

        public ValueTask<SourceIndex> IndexAsync(CollectionSource source, IProgress<IndexProgress>? progress, CancellationToken cancellationToken)
        {
            Reads++;
            return ValueTask.FromResult(Failing
                ? new SourceIndex(source.Id, time.GetUtcNow(), null, [],
                    [new IndexDiagnostic(IndexDiagnosticSeverity.Error, "NHC can't be reached.", "https://www.nhc.noaa.gov/CurrentStorms.json")])
                : new SourceIndex(source.Id, time.GetUtcNow(), null, Storms, []));
        }
    }

    /// <summary>Has each storm of <see cref="Local"/> on disk at its advisory, in a folder of its own under the root.</summary>
    private sealed class Downloader(string root) : ILibraryDownloader
    {
        public Dictionary<string, (DateTimeOffset Run, string Advisory)> Local { get; } = new(StringComparer.Ordinal);

        public List<string> Requested { get; } = [];

        public event EventHandler? Changed { add { } remove { } }

        public CollectionItem Localize(CollectionItem item)
        {
            if (!Local.TryGetValue(item.Key, out var local))
                return item;
            Directory.CreateDirectory(Path.Combine(root, item.Key));
            return item with { Location = new LocalItemLocation(root, $"{item.Key}/{item.Key}_5day_{local.Advisory}.zip", []) };
        }

        public bool IsOutdated(CollectionItem item) =>
            Local.TryGetValue(item.Key, out var local) && S100ForecastFeedIndexer.RunOf(item) is { } run && run > local.Run;

        public DateTimeOffset? LocalPublishedAtOf(CollectionItem item) => Local.TryGetValue(item.Key, out var local) ? local.Run : null;

        public bool CanDownload(CollectionItem item) => item.Location is RemoteItemLocation;

        public Task<LibraryDownloadResult> DownloadAsync(IReadOnlyList<CollectionItem> items, CancellationToken cancellationToken = default)
        {
            Requested.AddRange(items.Select(i => i.Name));
            return Task.FromResult(new LibraryDownloadResult(items.Count, 0, false));
        }
    }

    /// <summary>Has the storms in <see cref="Loaded"/> open; records what's closed and opened.</summary>
    private sealed class Loader : ILibraryLoader
    {
        public HashSet<string> Loaded { get; } = new(StringComparer.Ordinal);

        public List<string> Closed { get; } = [];

        public List<string> Opened { get; } = [];

        public event EventHandler? Changed { add { } remove { } }

        public LibraryLoadState StateOf(CollectionItem item) =>
            item.Location is LocalItemLocation && Loaded.Contains(item.Key) ? LibraryLoadState.Loaded : LibraryLoadState.None;

        public Task<LibraryLoadResult> LoadAsync(IReadOnlyList<CollectionItem> items, bool defer, CancellationToken cancellationToken = default)
        {
            Opened.AddRange(items.Select(i => i.Key));
            return Task.FromResult(new LibraryLoadResult(items.Count, 0));
        }

        public int Close(IReadOnlyList<CollectionItem> items)
        {
            Closed.AddRange(items.Select(i => i.Key));
            return items.Count;
        }
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
