using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.Services.Sources;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests.Sources;

/// <summary>
/// Live feeds in the Library (data source design note D8, slice 9): the
/// Library holds the subscription and the Datasets panel holds what's on the map.
/// </summary>
public sealed class LiveFeedLibraryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"live-feeds-{Guid.NewGuid():N}");
    private readonly CollectionLibrary _library;
    private readonly SourceDatasetService _sources = new(SourceRegistry.Default);
    private readonly FakeDatasetLoaderService _loader = new();
    private readonly List<DatasetEntry> _loaded = [];
    private readonly DatasetsViewModel _datasets;
    private readonly ViewerSettings _settings;
    private readonly List<string> _removedFromMap = [];
    private readonly LiveFeedLibrary _feeds;

    public LiveFeedLibraryTests()
    {
        Directory.CreateDirectory(_root);
        _loader.LoadHook = (entry, _) =>
        {
            _loaded.Add(entry);
            return Task.CompletedTask;
        };
        _datasets = new DatasetsViewModel(_loader);
        _library = new CollectionLibrary(
            new CollectionIndexer([new LiveFeedIndexer()]),
            new CollectionLibraryOptions(Path.Combine(_root, "collections.json"), Path.Combine(_root, "index-cache")));
        _library.Initialize();
        _settings = new ViewerSettings { SettingsFilePath = Path.Combine(_root, "settings.json") };
        // The Library raises Changed on its indexing thread; the app posts to
        // the UI thread. Here the tests call Sync themselves, on theirs.
        _feeds = new LiveFeedLibrary(_library, _sources, _datasets, _settings, post: static _ => { }, removedFromMap: _removedFromMap.Add);
    }

    public void Dispose()
    {
        _feeds.Dispose();
        _sources.Dispose();
        _library.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort.
        }
    }

    private LiveFeedSource[] FeedsInLibrary() =>
        [.. _library.Collections.SelectMany(c => c.Sources).Select(s => s.Definition).OfType<LiveFeedSource>()];

    [Fact]
    public void Subscribing_adds_the_feed_to_the_Library_and_the_map_once()
    {
        _feeds.Start();

        Assert.True(_feeds.Subscribe(S100Sources.NdbcObservations));
        Assert.False(_feeds.Subscribe(S100Sources.NdbcObservations));

        var feed = Assert.Single(FeedsInLibrary());
        Assert.Equal(S100Sources.NdbcObservationsId, feed.RegistrationId);
        var collection = Assert.Single(_library.Collections, c => !c.IsSession);
        Assert.Equal("Live feeds", collection.Definition.Name);
        Assert.Single(_datasets.Entries, e => e.SourceId == S100Sources.NdbcObservationsId);
        Assert.Contains(S100Sources.NdbcObservationsId, _feeds.Subscribed);
    }

    [Fact]
    public void Feeds_saved_before_the_Library_held_them_move_into_it()
    {
        _settings.SourceDatasets.Add(S100Sources.NdbcObservationsId);
        _settings.SourceDatasets.Add("acme.gone");

        _feeds.Start();

        Assert.Equal(S100Sources.NdbcObservationsId, Assert.Single(FeedsInLibrary()).RegistrationId);
        Assert.Empty(_settings.SourceDatasets);
        Assert.NotNull(_datasets.FindSource(S100Sources.NdbcObservationsId));
    }

    [Fact]
    public void Removing_from_the_map_keeps_the_subscription_until_the_next_start()
    {
        _feeds.Start();
        _feeds.Subscribe(S100Sources.NdbcObservations);

        _datasets.Entries.Remove(_datasets.FindSource(S100Sources.NdbcObservationsId)!);
        _feeds.Sync();

        Assert.Null(_datasets.FindSource(S100Sources.NdbcObservationsId));
        Assert.Single(FeedsInLibrary());
        Assert.Equal(["NDBC observations"], _removedFromMap);

        // Choosing it again puts it back.
        Assert.False(_feeds.Subscribe(S100Sources.NdbcObservations));
        Assert.NotNull(_datasets.FindSource(S100Sources.NdbcObservationsId));

        // A new start puts it on the map too.
        using var next = new LiveFeedLibrary(_library, _sources, new DatasetsViewModel(new FakeDatasetLoaderService()), _settings, post: static _ => { });
        next.Start();
        Assert.Single(FeedsInLibrary());
    }

    [Fact]
    public void Unsubscribing_in_the_Library_takes_the_feed_off_the_map()
    {
        _feeds.Start();
        _feeds.Subscribe(S100Sources.NdbcObservations);
        var collection = Assert.Single(_library.Collections, c => !c.IsSession);

        _library.RemoveSource(collection.Id, Assert.Single(FeedsInLibrary()).Id);
        _feeds.Sync();

        Assert.Null(_datasets.FindSource(S100Sources.NdbcObservationsId));
        Assert.Empty(_removedFromMap);
    }

    [Fact]
    public void A_subscription_is_saved_with_the_Library()
    {
        _feeds.Start();
        _feeds.Subscribe(S100Sources.NdbcObservations);

        using var reopened = new CollectionLibrary(
            new CollectionIndexer([new LiveFeedIndexer()]),
            new CollectionLibraryOptions(Path.Combine(_root, "collections.json"), Path.Combine(_root, "index-cache-2")));
        reopened.Initialize();

        var feed = Assert.IsType<LiveFeedSource>(Assert.Single(reopened.Collections.SelectMany(c => c.Sources)).Definition);
        Assert.Equal(S100Sources.NdbcObservationsId, feed.RegistrationId);
    }

    [Fact]
    public void Every_feed_is_offered_including_ones_that_need_settings()
    {
        Assert.Contains(S100Sources.NdbcObservations, _feeds.Offered);
        Assert.Contains(S100Sources.NmeaWeather, _feeds.Offered);
    }

    [Fact]
    public void A_feed_missing_a_required_option_says_so_and_waits_for_it()
    {
        _feeds.Start();
        _feeds.Subscribe(S100Sources.NmeaWeather);

        var waiting = _datasets.FindSource(S100Sources.NmeaWeatherId)!;
        Assert.Equal(EncDotNet.S100.Viewer.Services.Sources.SourceStatusKind.NeedsSettings, waiting.SourceStatus!.Kind);
        Assert.Equal("Needs settings", waiting.Subtitle);
        Assert.Contains("NMEA input", waiting.SubtitleTooltip, StringComparison.Ordinal);
        Assert.True(waiting.NeedsAttention);
        Assert.DoesNotContain(waiting, _loaded);

        var collection = Assert.Single(_library.Collections, c => !c.IsSession);
        _library.UpdateSource(collection.Id, Assert.Single(FeedsInLibrary()) with
        {
            Options = new Dictionary<string, string> { ["Input"] = "udp:10110" },
        });
        _feeds.Sync();

        var started = _datasets.FindSource(S100Sources.NmeaWeatherId)!;
        Assert.Null(started.SourceStatus);
        Assert.Contains(started, _loaded);
    }

    [Fact]
    public void New_settings_put_the_feed_back_with_them()
    {
        _feeds.Start();
        _feeds.Subscribe(S100Sources.NmeaWeather);
        var first = _datasets.FindSource(S100Sources.NmeaWeatherId)!;
        Assert.Equal(string.Empty, Assert.IsType<NmeaWeatherSourceSettings>(first.SourceOptions).Input);
        var collection = Assert.Single(_library.Collections, c => !c.IsSession);

        _library.UpdateSource(collection.Id, Assert.Single(FeedsInLibrary()) with
        {
            Options = new Dictionary<string, string> { ["Input"] = "udp:10110" },
        });
        _feeds.Sync();

        var second = _datasets.FindSource(S100Sources.NmeaWeatherId)!;
        Assert.NotSame(first, second);
        Assert.Equal("udp:10110", Assert.IsType<NmeaWeatherSourceSettings>(second.SourceOptions).Input);
        Assert.Empty(_removedFromMap);

        // The same settings again change nothing.
        _feeds.Sync();
        Assert.Same(second, _datasets.FindSource(S100Sources.NmeaWeatherId));
    }

    [Fact]
    public void A_live_feed_is_described_by_its_registration()
    {
        var feed = new LiveFeedSource(Guid.NewGuid(), "NDBC observations", S100Sources.NdbcObservationsId);

        Assert.Equal("LIVE", LibraryNodeText.KindOf(feed));
        Assert.Equal(S100Sources.NdbcObservationsId, LibraryNodeText.SourceLocation(feed));
    }
}
