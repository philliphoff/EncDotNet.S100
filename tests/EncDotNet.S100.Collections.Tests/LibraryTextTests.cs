using EncDotNet.S100.Collections.Library;

namespace EncDotNet.S100.Collections.Tests;

/// <summary>
/// The Library's shared text (#792): item tags and details, and node kind tags,
/// names and status lines, as every host reports them. The viewer's Library
/// tests cover the same text through its view models.
/// </summary>
public sealed class LibraryTextTests
{
    private static readonly Guid SourceId = Guid.NewGuid();

    private static CollectionItem Item(int? edition = null, ItemLocation? location = null) => new()
    {
        Key = "US5CA5HI",
        ProductSpec = "S-57",
        Name = "US5CA5HI",
        Edition = edition,
        Bounds = new GeoBounds(37.5, -122.5, 37.75, -122.25),
        Location = location ?? new RemoteItemLocation(new Uri("https://charts.example.test/ENCs/US5CA5HI.zip"), SizeBytes: 1_700_000),
        Properties = new Dictionary<string, string> { ["riverMiles"] = "12" },
    };

    private static LibrarySource Source(LibrarySourceState state = LibrarySourceState.Ready, params CollectionItem[] items) =>
        new(new NoaaEncFeedSource(SourceId, null, new Uri("https://charts.example.test/catalog.xml"), NoaaEncFilter.All),
            new SourceIndex(SourceId, DateTimeOffset.UnixEpoch, null, items, []),
            state);

    private static LibraryCollection Collection(LibrarySource source) =>
        new(new DatasetCollection(Guid.NewGuid(), "Bay", [source.Definition], DateTimeOffset.UnixEpoch), [source]);

    [Fact]
    public void Resources_load_and_unknown_keys_fall_back()
    {
        Assert.False(string.IsNullOrEmpty(LibraryText.Find("Library_Tag_Queued")));
        Assert.False(string.IsNullOrEmpty(LibraryText.SpecDisplayName("S-101")));
        Assert.Null(LibraryText.Find("No_Such_Key"));
    }

    [Fact]
    public void An_outdated_item_is_tagged_with_the_edition_online()
    {
        var item = Item(edition: 46);
        var tags = LibraryItemText.Tags(new LibraryItemTextInput(item, Source(), LibraryAvailability.Outdated, item));

        var tag = Assert.Single(tags);
        Assert.Equal(LibraryTagKind.Update, tag.Kind);
        Assert.Contains("46", tag.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_failed_download_is_tagged_and_its_error_detailed()
    {
        var item = Item();
        var failed = new LibraryDownloadItemStatus(LibraryDownloadItemState.Failed, 0, null, "HTTP 503");
        var input = new LibraryItemTextInput(item, Source(), LibraryAvailability.Online, item) { Download = failed };

        Assert.Contains(LibraryItemText.Tags(input), t => t.Kind == LibraryTagKind.Failed);
        var source = LibraryItemText.Details(input).Single(g => g.Fields.Any(f => f.Value.Contains("HTTP 503", StringComparison.Ordinal)));
        Assert.Contains(source.Fields, f => f.Label == "River miles" && f.Value == "12");
    }

    [Fact]
    public void Details_group_product_coverage_and_source()
    {
        var item = Item(edition: 3);
        var groups = LibraryItemText.Details(new LibraryItemTextInput(item, Source(), LibraryAvailability.Online, item) { CollectionName = "Bay" });

        Assert.Equal(3, groups.Count);  // no Forecast group for a chart
        Assert.All(groups, g => Assert.NotEmpty(g.Fields));
        Assert.Contains(groups.SelectMany(g => g.Fields), f => f.Value == "37°45.000'N  122°15.000'W" && f.IsMono);
        Assert.Contains(groups.SelectMany(g => g.Fields), f => f.CopyValue == "https://charts.example.test/ENCs/US5CA5HI.zip");
    }

    [Fact]
    public void Nodes_are_named_tagged_and_given_a_status()
    {
        var source = Source(LibrarySourceState.Indexing);
        var collection = Collection(source);

        Assert.Equal("WEB", LibraryNodeText.KindOf(source.Definition));
        Assert.Equal("WEB", LibraryNodeText.KindOf(collection));
        Assert.Equal(LibraryText.Find("Library_NoaaFeed"), LibraryNodeText.SourceName(source, collection));

        var (line, kind) = LibraryNodeText.Status(new LibraryNodeStatusInput(collection, collection.Sources));
        Assert.Equal(LibraryNodeStatusKind.Busy, kind);
        Assert.Equal(LibraryText.Find("Library_Status_Indexing"), line);
    }

    [Fact]
    public void A_shared_feed_url_keeps_only_the_end_of_its_token()
    {
        Assert.Equal(
            "http://bridge-pc:8100/••••3f9a/feed.json",
            LibraryTextFormat.MaskToken(new Uri("http://bridge-pc:8100/0123456789abcdef3f9a/feed.json")));
    }
}
