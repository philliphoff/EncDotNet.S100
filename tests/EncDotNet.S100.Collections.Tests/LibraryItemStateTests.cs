using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.DataModel;
using Microsoft.Extensions.Time.Testing;

namespace EncDotNet.S100.Collections.Tests;

public sealed class LibraryItemStateTests : IDisposable
{
    private static readonly DateTimeOffset Run = new(2026, 10, 5, 21, 0, 0, TimeSpan.Zero);

    private readonly TempDirectory _root = new();

    public void Dispose() => _root.Dispose();

    private static readonly LibrarySource Source = new(
        new LocalFolderSource(Guid.NewGuid(), null, "/data"), null, LibrarySourceState.Ready);

    private static CollectionItem Online(string name, Dictionary<string, string>? properties = null, GeoBounds? bounds = null) => new()
    {
        Key = name,
        ProductSpec = "S-111",
        Name = name,
        Bounds = bounds,
        Location = new RemoteItemLocation(new Uri("https://example.test/" + name + ".h5"), 1000),
        Properties = properties ?? [],
    };

    private static CollectionItem Forecast(string name) => Online(name, new()
    {
        [S100ForecastFeedIndexer.ModelProperty] = "sfbofs",
        [S100ForecastFeedIndexer.RunProperty] = Run.ToString("O"),
        [S100ForecastFeedIndexer.ValidToProperty] = Run.AddHours(48).ToString("O"),
    });

    private CollectionItem Local(string name, bool exists = true)
    {
        if (exists)
            File.WriteAllText(Path.Combine(_root.Path, name + ".000"), "");
        return new()
        {
            Key = name,
            ProductSpec = "S-57",
            Name = name,
            Location = new LocalItemLocation(_root.Path, name + ".000", []),
        };
    }

    [Fact]
    public void Online_local_and_missing_items_resolve_from_their_location()
    {
        Assert.Equal(LibraryAvailability.Online, new LibraryItemState(Online("A"), Source).Availability);
        Assert.Equal(LibraryAvailability.Local, new LibraryItemState(Local("B"), Source).Availability);
        Assert.Equal(LibraryAvailability.Missing, new LibraryItemState(Local("C", exists: false), Source).Availability);
    }

    [Fact]
    public void A_downloaded_copy_is_what_opens_and_its_load_state_wins()
    {
        var online = Online("A");
        var copy = Local("A");
        var copies = new FakeCopies { Copy = copy };

        var state = new LibraryItemState(online, Source, copies);
        Assert.Same(copy, state.EffectiveItem);
        Assert.Equal(LibraryAvailability.Local, state.Availability);

        var loaded = new LibraryItemState(online, Source, copies, item => item == copy ? LibraryLoadState.Loaded : LibraryLoadState.None);
        Assert.Equal(LibraryAvailability.Loaded, loaded.Availability);
        Assert.True(loaded.IsLoadedNow);

        var deferred = new LibraryItemState(online, Source, copies, _ => LibraryLoadState.Deferred);
        Assert.Equal(LibraryAvailability.Deferred, deferred.Availability);
    }

    [Fact]
    public void A_newer_edition_online_makes_an_unloaded_copy_an_update()
    {
        var copies = new FakeCopies { Copy = Local("A"), Outdated = true };

        Assert.Equal(LibraryAvailability.Outdated, new LibraryItemState(Online("A"), Source, copies).Availability);
        Assert.Equal(
            LibraryAvailability.Loaded,
            new LibraryItemState(Online("A"), Source, copies, _ => LibraryLoadState.Loaded).Availability);
    }

    [Fact]
    public void A_forecast_with_a_newer_run_online_is_an_update_even_when_loaded()
    {
        var copies = new FakeCopies { Copy = Local("T"), Outdated = true, LocalRun = Run.AddHours(-6) };

        var state = new LibraryItemState(Forecast("T"), Source, copies, _ => LibraryLoadState.Loaded);

        Assert.Equal(LibraryAvailability.Outdated, state.Availability);
    }

    [Fact]
    public void A_downloaded_forecast_run_expires_when_its_window_ends()
    {
        var copies = new FakeCopies { Copy = Local("T"), LocalRun = Run };
        var clock = new FakeTimeProvider(Run.AddHours(47));

        Assert.Equal(LibraryAvailability.Local, new LibraryItemState(Forecast("T"), Source, copies, time: clock).Availability);

        clock.Advance(TimeSpan.FromHours(2));
        Assert.Equal(LibraryAvailability.Expired, new LibraryItemState(Forecast("T"), Source, copies, time: clock).Availability);
    }

    [Fact]
    public void A_forecast_is_valid_over_its_downloaded_run_else_the_catalogue_run()
    {
        var catalogue = new LibraryItemState(Forecast("T"), Source);
        Assert.Equal((Run.UtcDateTime, Run.AddHours(48).UtcDateTime), catalogue.ValidWindow);
        Assert.Equal((Run.UtcDateTime, Run.AddHours(48).UtcDateTime), catalogue.CatalogueRun);

        var older = Run.AddHours(-6);
        var downloaded = new LibraryItemState(Forecast("T"), Source, new FakeCopies { LocalRun = older });
        Assert.Equal((older.UtcDateTime, older.AddHours(48).UtcDateTime), downloaded.ValidWindow);
    }

    [Fact]
    public void A_dataset_is_valid_over_its_indexed_time_coverage()
    {
        var item = Online("W", new() { ["timeStart"] = "2026-10-05T00:00:00Z", ["timeEnd"] = "2026-10-06T00:00:00Z" });

        Assert.Equal(
            (new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 6, 0, 0, 0, DateTimeKind.Utc)),
            new LibraryItemState(item, Source).ValidWindow);
        Assert.Null(new LibraryItemState(Online("X"), Source).ValidWindow);
    }

    [Fact]
    public void Text_matches_name_title_spec_and_property_values()
    {
        var state = new LibraryItemState(Forecast("111US00_SFBOFS_US4CA2CJ"), Source);

        Assert.True(state.Matches("ca2cj"));
        Assert.True(state.Matches("s-111"));
        Assert.True(state.Matches("SFBOFS"));
        Assert.False(state.Matches("cbofs"));
    }

    [Theory]
    [InlineData("06530005-abb6-48ab-897c-c3a4690ff341:sfbofs/111US00_SFBOFS_US4CA2CJ", true)]
    [InlineData("06530005-abb6-48ab-897c-c3a4690ff341:", false)]
    [InlineData("not-a-guid:key", false)]
    [InlineData("no-colon", false)]
    public void Item_ids_round_trip(string id, bool valid)
    {
        Assert.Equal(valid, LibraryItemState.TryParseId(id, out var sourceId, out var key));
        if (valid)
            Assert.Equal(id, LibraryItemState.FormatId(sourceId, key));
    }

    [Fact]
    public void Availability_wire_names_round_trip()
    {
        foreach (var state in Enum.GetValues<LibraryAvailability>())
        {
            Assert.True(LibraryAvailabilityNames.TryParse(LibraryAvailabilityNames.Of(state), out var parsed));
            Assert.Equal(state, parsed);
        }

        Assert.Equal("on_pan", LibraryAvailabilityNames.Of(LibraryAvailability.Deferred));
        Assert.Equal("update", LibraryAvailabilityNames.Of(LibraryAvailability.Outdated));
        Assert.False(LibraryAvailabilityNames.TryParse("deferred", out _));
    }

    private sealed class FakeCopies : ILibraryLocalCopies
    {
        public CollectionItem? Copy { get; init; }

        public bool Outdated { get; init; }

        public DateTimeOffset? LocalRun { get; init; }

        public CollectionItem Localize(CollectionItem item) => Copy ?? item;

        public bool IsOutdated(CollectionItem item) => Outdated;

        public DateTimeOffset? LocalPublishedAtOf(CollectionItem item) => LocalRun;
    }
}
