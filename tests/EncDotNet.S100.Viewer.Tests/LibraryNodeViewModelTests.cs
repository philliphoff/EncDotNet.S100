using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Viewer.Library;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>The Library tree's kind tags and status lines (UX refinement §2).</summary>
public sealed class LibraryNodeViewModelTests
{
    private static readonly Uri FeedUri = new("http://bridge-pc.local:8100/tok/feed.json");

    private static SourceIndex Index(int items, int problems = 0) => new(
        Guid.NewGuid(), DateTimeOffset.UnixEpoch, "fp",
        Enumerable.Range(0, items).Select(i => new CollectionItem { Key = $"k{i}", ProductSpec = "S-57", Name = $"N{i}", Location = NoItemLocation.Instance }).ToArray(),
        Enumerable.Range(0, problems).Select(_ => new IndexDiagnostic(IndexDiagnosticSeverity.Warning, "bad", null)).ToArray());

    private static LibraryNodeViewModel Node(
        CollectionSource source, SourceIndex? index, LibrarySourceState state = LibrarySourceState.Ready,
        bool session = false, Func<CollectionSource, FeedHealth?>? health = null)
    {
        var librarySource = new LibrarySource(source, index, state);
        var collection = new LibraryCollection(
            new DatasetCollection(Guid.NewGuid(), "C", [source], DateTimeOffset.UnixEpoch), [librarySource], session);
        return LibraryNodeViewModel.ForCollection(collection, health).Children.Single();
    }

    public static TheoryData<string, string> Kinds => new()
    {
        { "folder", "DIR" }, { "zip", "ZIP" }, { "noaa", "WEB" }, { "list", "LIST" }, { "feed", "FEED" }, { "s128", "S-128" },
    };

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Sources_are_tagged_by_kind(string kind, string tag)
    {
        CollectionSource source = kind switch
        {
            "folder" => new LocalFolderSource(Guid.NewGuid(), null, "/charts"),
            "zip" => new ExchangeSetSource(Guid.NewGuid(), null, "/charts/set.ZIP"),
            "noaa" => new NoaaEncFeedSource(Guid.NewGuid(), null, NoaaEncFeedSource.DefaultCatalogUri, NoaaEncFilter.All),
            "list" => new ChartCatalogsFeedSource(Guid.NewGuid(), null, new Uri("https://example.test/RO_IENC_Catalog.xml"), ChartCatalogsFilter.All),
            "feed" => new S100FeedSource(Guid.NewGuid(), null, FeedUri, S100FeedFilter.All),
            _ => new S128CatalogueSource(Guid.NewGuid(), null, "/c.gml"),
        };

        Assert.Equal(tag, Node(source, Index(1)).KindTag);
    }

    [Theory]
    [InlineData("https://noaa-s102-pds.s3.amazonaws.com/ed3.0.0/S100_ROOT/CATALOG.XML", "AWS")]
    [InlineData("https://charts.example.test/s102/CATALOG.XML", "WEB")]
    public void Remote_S100_catalogues_are_tagged_by_host(string url, string tag)
    {
        var source = new S100CatalogueFeedSource(Guid.NewGuid(), null, new Uri(url), S100CatalogueFilter.All);

        Assert.Equal(tag, Node(source, Index(1)).KindTag);
    }

    [Fact]
    public void A_remote_S100_catalogue_shows_its_areas_and_its_date()
    {
        CollectionItem Tile(string name, string folder) => new()
        {
            Key = folder + "/" + name,
            ProductSpec = "S-102",
            Name = name,
            Location = NoItemLocation.Instance,
            Properties = new Dictionary<string, string> { [LocalManifestIndexer.GroupProperty] = folder, ["notForNavigation"] = "true" },
        };
        var published = new DateTimeOffset(2026, 9, 30, 18, 0, 0, TimeSpan.Zero);
        var index = new SourceIndex(
            Guid.NewGuid(), DateTimeOffset.UnixEpoch, "fp",
            [Tile("A", "Northeast/Boston"), Tile("B", "Northeast/Boston"), Tile("C", "Southeast/Wilmington")], [])
        {
            Groups = [new SourceIndexGroup("Northeast/Boston", "Boston"), new SourceIndexGroup("Southeast/Wilmington", "Wilmington")],
            PublishedAt = published,
        };
        var source = new S100CatalogueFeedSource(
            Guid.NewGuid(), null, new Uri("https://noaa-s102-pds.s3.amazonaws.com/ed3.0.0/S100_ROOT/CATALOG.XML"), S100CatalogueFilter.All);

        var node = Node(source, index);

        Assert.Equal("NOAA S-102 Bathymetry", node.Name);
        Assert.Equal(["Boston", "Wilmington"], node.Children.Select(c => c.Name));
        Assert.Equal(["2", "1"], node.Children.Select(c => c.Status));
        Assert.All(node.Children, c => Assert.Equal("AWS", c.KindTag));
        Assert.Equal(LibraryNodeStatusKind.Info, node.StatusKind);
        Assert.Equal(
            $"Catalogue {published.ToLocalTime().ToString("d", System.Globalization.CultureInfo.CurrentCulture)} · not for navigation",
            node.StatusLine);

        // Offline with a cached catalogue.
        var offline = Node(source, index, health: _ => new FeedHealth(
            DateTimeOffset.UtcNow, "Connection refused", DateTimeOffset.UtcNow, new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero)));
        Assert.StartsWith("Offline · catalogue cached ", offline.StatusLine, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("All downloads")]
    [InlineData("C")]  // the collection's name
    [InlineData(null)]
    public void A_community_list_holding_one_package_is_named_by_it(string? displayName)
    {
        var items = Enumerable.Range(0, 3).Select(i => new CollectionItem
        {
            Key = $"269/c{i}",
            ProductSpec = "S-57",
            Name = $"c{i}",
            Location = NoItemLocation.Instance,
            Properties = new Dictionary<string, string> { ["package"] = "269", ["packageTitle"] = "23.10.2025 15:17 - New IENCs and bIENCs (269)" },
        }).ToArray();
        var index = new SourceIndex(Guid.NewGuid(), DateTimeOffset.UnixEpoch, "fp", items, []);
        var source = new ChartCatalogsFeedSource(Guid.NewGuid(), displayName, new Uri("https://example.test/AT_IENC_Catalog.xml"), ChartCatalogsFilter.All);

        Assert.Equal("New IENCs and bIENCs (269)", Node(source, index).Name);
    }

    [Fact]
    public void Legacy_generic_names_give_way_to_the_catalogue_name_but_user_names_stay()
    {
        var noaa = new NoaaEncFeedSource(Guid.NewGuid(), "All ENCs", NoaaEncFeedSource.DefaultCatalogUri, NoaaEncFilter.All);
        Assert.Equal(EncDotNet.S100.Collections.KnownSources.KnownCatalogueSources.Find("noaa-enc")!.Name, Node(noaa, Index(1)).Name);

        var named = noaa with { DisplayName = "Alaska" };
        Assert.Equal("Alaska", Node(named, Index(1)).Name);
    }

    [Fact]
    public void Normal_nodes_are_one_line_with_a_count()
    {
        var node = Node(new LocalFolderSource(Guid.NewGuid(), null, "/charts"), Index(1234));

        Assert.Equal(1234.ToString("N0", System.Globalization.CultureInfo.CurrentCulture), node.Status);
        Assert.False(node.HasStatusLine);
        Assert.Equal(LibraryNodeStatusKind.None, node.StatusKind);
    }

    [Fact]
    public void Indexing_problems_session_and_downloads_get_a_status_line()
    {
        var folder = new LocalFolderSource(Guid.NewGuid(), null, "/charts");

        var indexing = Node(folder, null, LibrarySourceState.Indexing);
        Assert.Equal("—", indexing.Status);
        Assert.Equal(("Indexing…", LibraryNodeStatusKind.Busy), (indexing.StatusLine, indexing.StatusKind));

        var problems = Node(folder, Index(10, problems: 3));
        Assert.Equal(("3 problems", LibraryNodeStatusKind.Warning), (problems.StatusLine, problems.StatusKind));

        var session = Node(new S128CatalogueSource(Guid.NewGuid(), null, "/c.gml"), Index(2), session: true);
        Assert.Equal(("Temporary · Pin to keep", LibraryNodeStatusKind.Info), (session.StatusLine, session.StatusKind));

        problems.DownloadStatus = "Downloading 2 of 5 · 4 MB left";
        Assert.Equal(("Downloading 2 of 5 · 4 MB left", LibraryNodeStatusKind.Busy), (problems.StatusLine, problems.StatusKind));
    }

    [Fact]
    public void A_shared_feed_never_shows_its_token_but_copies_it()
    {
        var node = Node(new S100FeedSource(Guid.NewGuid(), "bridge-pc", new Uri("http://bridge-pc.local:8100/Zm9vYmFyMTIz3f9a/feed.json"), S100FeedFilter.All), Index(1));

        Assert.Equal("bridge-pc", node.Name);
        Assert.DoesNotContain("Zm9vYmFy", node.Tooltip);
        Assert.Contains("http://bridge-pc.local:8100/••••3f9a/feed.json", node.Tooltip);
        Assert.Equal(new Uri("http://bridge-pc.local:8100/Zm9vYmFyMTIz3f9a/feed.json"), node.SourceUrl);
        Assert.False(Node(new LocalFolderSource(Guid.NewGuid(), null, "/charts"), Index(1)).HasSourceUrl);
    }

    [Fact]
    public void A_shared_feed_says_whether_its_server_is_reachable()
    {
        var feed = new S100FeedSource(Guid.NewGuid(), null, FeedUri, S100FeedFilter.All);
        var now = new DateTimeOffset(2026, 9, 28, 14, 31, 0, TimeSpan.Zero);
        FeedHealth? health = new FeedHealth(now, null, null, null);
        var node = Node(feed, Index(312), health: _ => health);

        Assert.Equal(("Reachable · bridge-pc.local:8100", LibraryNodeStatusKind.Ok), (node.StatusLine, node.StatusKind));

        health = new FeedHealth(now, "Connection refused", now.AddMinutes(-29), now.AddHours(-2));
        Assert.StartsWith("Unreachable since ", node.StatusLine);
        Assert.EndsWith("cached index, 2 h old", node.StatusLine);
        Assert.Equal(LibraryNodeStatusKind.Warning, node.StatusKind);

        health = new FeedHealth(now, "404 Not Found", now, now.AddHours(-2));
        Assert.StartsWith("Access denied", node.StatusLine);
        Assert.Equal(LibraryNodeStatusKind.Error, node.StatusKind);
    }
}
