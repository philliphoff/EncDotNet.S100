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
