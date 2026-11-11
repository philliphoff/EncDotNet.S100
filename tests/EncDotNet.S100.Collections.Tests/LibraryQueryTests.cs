using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Collections.Tests;

public sealed class LibraryQueryTests
{
    private static CollectionItem Item(string name, string spec, GeoBounds? bounds, int? band = null, Dictionary<string, string>? properties = null) => new()
    {
        Key = name,
        ProductSpec = spec,
        Name = name,
        Bounds = bounds,
        UsageBand = band,
        Location = new RemoteItemLocation(new Uri("https://example.test/" + name)),
        Properties = properties ?? [],
    };

    private static LibrarySource Source(params CollectionItem[] items)
    {
        var id = Guid.NewGuid();
        return new LibrarySource(
            new LocalFolderSource(id, null, "/data"),
            new SourceIndex(id, DateTimeOffset.UnixEpoch, null, items, []),
            LibrarySourceState.Ready);
    }

    private static LibraryCollection Collection(params LibrarySource[] sources) =>
        new(new DatasetCollection(Guid.NewGuid(), "c", sources.Select(s => s.Definition).ToArray(), DateTimeOffset.UnixEpoch), sources);

    private static readonly LibrarySource Harbour = Source(
        Item("HARBOUR", "S-57", new GeoBounds(37.8, -122.5, 37.85, -122.45), band: 5),
        Item("APPROACH", "S-57", new GeoBounds(37.5, -123, 38, -122), band: 3),
        Item("CURRENTS", "S-111", new GeoBounds(37.8, -122.7, 38.1, -122.4),
            properties: new() { ["timeStart"] = "2026-10-05T22:00:00Z", ["timeEnd"] = "2026-10-07T21:00:00Z" }));

    private static readonly LibrarySource Elsewhere = Source(Item("FAR", "S-57", new GeoBounds(10, 10, 11, 11), band: 4));

    private static readonly IReadOnlyList<LibraryCollection> Library = [Collection(Harbour), Collection(Elsewhere)];

    private static List<string>? Names(LibraryItemQuery query) =>
        LibraryQuery.Find(Library, query, (item, source) => new LibraryItemState(item, source), state => state)
            ?.Select(state => state.Item.Name)
            .ToList();

    [Fact]
    public void No_filters_find_every_item()
    {
        Assert.Equal(["HARBOUR", "APPROACH", "CURRENTS", "FAR"], Names(new LibraryItemQuery()));
    }

    [Fact]
    public void A_collection_or_source_id_scopes_the_search_and_an_unknown_id_finds_nothing()
    {
        Assert.Equal(["FAR"], Names(new LibraryItemQuery(Library[1].Id)));
        Assert.Equal(["FAR"], Names(new LibraryItemQuery(Elsewhere.Id)));
        Assert.Null(Names(new LibraryItemQuery(Guid.NewGuid())));
    }

    [Fact]
    public void Spec_bounds_text_and_state_filter_together()
    {
        Assert.Equal(["CURRENTS"], Names(new LibraryItemQuery(Spec: "s-111")));
        Assert.Equal(["HARBOUR", "APPROACH", "CURRENTS"], Names(new LibraryItemQuery(Bounds: new GeoBounds(37.82, -122.48, 37.83, -122.47))));
        Assert.Equal(["APPROACH"], Names(new LibraryItemQuery(Text: "approach")));
        Assert.Equal(["HARBOUR", "APPROACH", "CURRENTS", "FAR"], Names(new LibraryItemQuery(States: new HashSet<LibraryAvailability> { LibraryAvailability.Online })));
        Assert.Empty(Names(new LibraryItemQuery(States: new HashSet<LibraryAvailability> { LibraryAvailability.Loaded }))!);
    }

    [Fact]
    public void A_point_finds_covering_items_most_detailed_first()
    {
        var point = new GeoPosition(37.82, -122.478);

        Assert.Equal(["HARBOUR", "APPROACH", "CURRENTS"], Names(new LibraryItemQuery(Point: point)));
        Assert.Equal(["CURRENTS"], Names(new LibraryItemQuery(Point: point, Spec: "S-111")));
        Assert.Equal(["HARBOUR", "APPROACH", "CURRENTS"], LibraryQuery.HitsAt(Library, point).Select(p => p.Item.Name));
    }

    [Fact]
    public void Valid_at_keeps_items_whose_data_covers_the_time()
    {
        Assert.Equal(["CURRENTS"], Names(new LibraryItemQuery { ValidAt = new DateTime(2026, 10, 6, 22, 0, 0, DateTimeKind.Utc) }));
        Assert.Empty(Names(new LibraryItemQuery { ValidAt = new DateTime(2026, 10, 9, 22, 0, 0, DateTimeKind.Utc) })!);
    }

    [Fact]
    public void Items_are_found_by_id()
    {
        var id = LibraryItemState.FormatId(Elsewhere.Id, "FAR");

        Assert.Equal("FAR", LibraryQuery.FindById(Library, id)?.Item.Name);
        Assert.Null(LibraryQuery.FindById(Library, LibraryItemState.FormatId(Elsewhere.Id, "MISSING")));
        Assert.Null(LibraryQuery.FindById(Library, "nonsense"));
    }
}
