using EncDotNet.S100.Collections.ChartCatalogs;
using EncDotNet.S100.Collections.Feeds;
using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Collections.Noaa;
using EncDotNet.S100.Collections.RemoteCatalogues;
using EncDotNet.S100.Collections.Usace;

namespace EncDotNet.S100.Collections.Tests;

/// <summary>
/// Adding a Library source without the viewer's dialog (#792 chunk 3c): the
/// core scopes and draft, for the kinds moved so far (local paths, NOAA ENC,
/// USACE Inland ENC).
/// </summary>
public sealed class LibrarySourceDraftTests
{
    private static readonly LibraryCatalogueReaders Readers = new()
    {
        NoaaEnc = (_, _) => Task.FromResult(NoaaEncProductCatalogReader.Read(TestPaths.Fixture("noaa-enc-prodcat.xml"))),
        UsaceIenc = (_, _) => Task.FromResult(UsaceIencProductCatalogReader.Read(TestPaths.Fixture("usace-ienc-u37.xml"))),
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static KnownCatalogueSource Known(string id) => KnownCatalogueSources.Find(id) ?? throw new InvalidOperationException(id);

    [Fact]
    public async Task A_noaa_catalogue_offers_states_districts_and_regions_and_follows_the_selection()
    {
        var draft = LibrarySourceDraft.ForCatalogue(Known("noaa-enc"), Readers)!;
        Assert.Equal(LibrarySourceKind.NoaaFeed, draft.Kind);
        Assert.False(draft.IsLoaded);
        Assert.Null(draft.CatalogueDetail);

        Assert.Null(await draft.LoadAsync(Ct));

        Assert.True(draft.IsLoaded);
        Assert.Equal(3, draft.Groups.Count);
        Assert.Contains(draft.Groups[0].Options, o => o.Value == "AK" && o.Label == "Alaska (AK)");
        Assert.StartsWith("charts.noaa.gov", draft.CatalogueDetail, StringComparison.Ordinal);
        Assert.Equal(draft.Known!.Name, draft.SuggestedName);

        draft.Groups[0].Options.Single(o => o.Value == "AK").IsSelected = true;
        draft.IncludeAll = false;

        Assert.Equal($"{draft.Known.Name} — Alaska", draft.SuggestedName);
        var source = Assert.IsType<NoaaEncFeedSource>(draft.Build());
        Assert.Equal(["AK"], source.Filter.States);
        Assert.Equal("Alaska", source.DisplayName);
        Assert.Equal(draft.Scope!.SelectionSummary, draft.ScopeSummary);
    }

    [Fact]
    public async Task Ticks_are_kept_while_everything_is_included()
    {
        var draft = LibrarySourceDraft.ForCatalogue(Known("noaa-enc"), Readers)!;
        await draft.LoadAsync(Ct);
        draft.Groups[0].Options[0].IsSelected = true;

        Assert.True(draft.IncludeAll);
        Assert.NotEqual(draft.Scope!.SelectionSummary, draft.ScopeSummary);  // "your 1 pick is kept…"
        var source = Assert.IsType<NoaaEncFeedSource>(draft.Build());
        Assert.True(source.Filter.IsUnscoped);
        Assert.Equal(draft.Known!.Name, source.DisplayName);
    }

    [Fact]
    public async Task A_usace_catalogue_offers_rivers()
    {
        var draft = LibrarySourceDraft.ForCatalogue(Known("usace-ienc-rivers"), Readers)!;
        Assert.Null(await draft.LoadAsync(Ct));

        var rivers = Assert.Single(draft.Groups);
        Assert.NotEmpty(rivers.Options);
        rivers.Options[0].IsSelected = true;
        draft.IncludeAll = false;
        draft.KeepDownloaded = true;

        var source = Assert.IsType<UsaceIencFeedSource>(draft.Build());
        Assert.Equal([rivers.Options[0].Value], source.Filter.Rivers);
        Assert.True(source.Sync);
    }

    [Fact]
    public async Task A_catalogue_that_cannot_be_read_says_why()
    {
        var failing = new LibraryCatalogueReaders { NoaaEnc = (_, _) => throw new HttpRequestException("offline") };
        var draft = LibrarySourceDraft.ForCatalogue(Known("noaa-enc"), failing)!;

        Assert.Equal("offline", await draft.LoadAsync(Ct));
        Assert.False(draft.CanBuild);
    }

    [Fact]
    public void Local_paths_are_drafted_by_kind_and_unmoved_kinds_are_not()
    {
        var folder = LibrarySourceDraft.ForPath(LibrarySourceKind.Folder, "/charts/harbour")!;
        Assert.Equal("harbour", folder.SuggestedName);
        Assert.IsType<LocalFolderSource>(folder.Build());
        Assert.False(folder.CanKeepDownloaded);

        Assert.Null(LibrarySourceDraft.ForCatalogue(Known("noaa-s111"), Readers));
    }

    [Fact]
    public async Task A_manifest_offers_its_groups_and_names_the_source_after_the_selection()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Path, "BE"));
        Directory.CreateDirectory(Path.Combine(temp.Path, "DE"));
        var path = Path.Combine(temp.Path, "icenc.s100collection.json");
        File.WriteAllText(path, """
            { "format": "encdotnet-s100-collection", "version": 1, "title": "IC-ENC",
              "groups": [ { "id": "BE", "name": "Belgium", "paths": ["BE"] },
                          { "id": "DE", "name": "Germany", "paths": ["DE"] },
                          { "id": "NL", "name": "Netherlands", "paths": ["NL"] } ] }
            """);
        var draft = LibrarySourceDraft.ForPath(LibrarySourceKind.LocalManifest, path)!;

        Assert.Null(await draft.LoadAsync(Ct));

        Assert.Null(draft.CatalogueDetail);  // a local source has no catalogue host
        var groups = Assert.Single(draft.Groups);
        Assert.Equal(["BE", "DE", "NL"], groups.Options.Select(o => o.Value));
        Assert.True(groups.Options.Single(o => o.Value == "NL").IsMissing);
        Assert.Equal("IC-ENC", draft.SuggestedName);

        groups.Options[0].IsSelected = true;
        groups.Options[1].IsSelected = true;
        draft.IncludeAll = false;

        Assert.Equal("IC-ENC — Belgium and Germany", draft.SuggestedName);
        var source = Assert.IsType<LocalManifestSource>(draft.Build());
        Assert.Equal(["BE", "DE"], source.Filter.Groups);
        Assert.Equal("IC-ENC — Belgium and Germany", source.DisplayName);
        Assert.Equal("Mine", ((LocalManifestSource)draft.Build("Mine")).DisplayName);

        groups.Options[0].IsSelected = false;
        groups.Options[1].IsSelected = false;
        Assert.False(draft.CanBuild);  // nothing ticked
    }

    [Fact]
    public async Task A_manifest_that_cannot_be_read_lists_its_problems()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "broken.s100collection.json");
        File.WriteAllText(path, """{ "format": "encdotnet-s100-collection", "version": 1, "groups": [ { "name": "No id" } ] }""");
        var draft = LibrarySourceDraft.ForPath(LibrarySourceKind.LocalManifest, path)!;

        Assert.NotNull(await draft.LoadAsync(Ct));
        Assert.NotEmpty(((CollectionManifestScope)draft.Scope!).Problems);
        Assert.False(draft.CanBuild);
    }

    [Fact]
    public async Task An_s100_feed_offers_its_products()
    {
        var items = new[] { "S-102", "S-102", "S-124" }.Select((spec, i) => new CollectionItem
        {
            Key = $"item{i}",
            ProductSpec = spec,
            Name = $"ITEM{i}",
            Location = new RemoteItemLocation(new Uri($"https://feed.example.test/items/{i}.zip"), 1_000),
        }).ToArray();
        var feed = new S100FeedDocument(S100Feed.FormatName, S100Feed.CurrentVersion, "Bridge", DateTimeOffset.UnixEpoch, null, items);
        var readers = new LibraryCatalogueReaders { S100Feed = (_, _) => Task.FromResult(feed) };
        var known = KnownCatalogueSources.FromUrl(new Uri("https://feed.example.test/feed.json"), KnownCatalogueFormat.S100Feed, "Bridge");
        var draft = LibrarySourceDraft.ForCatalogue(known, readers)!;

        Assert.Null(await draft.LoadAsync(Ct));

        var products = Assert.Single(draft.Groups);
        Assert.Equal(["S-102", "S-124"], products.Options.Select(o => o.Value));
        products.Options[0].IsSelected = true;
        draft.IncludeAll = false;
        var source = Assert.IsType<S100FeedSource>(draft.Build());
        Assert.Equal(["S-102"], source.Filter.ProductSpecs);
        Assert.Equal("S-102", source.DisplayName);
    }

    [Fact]
    public async Task A_community_list_offers_its_entries()
    {
        var readers = new LibraryCatalogueReaders
        {
            CommunityList = (_, _) => Task.FromResult(ChartCatalogsProductCatalogReader.Read(TestPaths.Fixture("chartcatalogs-list.xml"))),
        };
        var known = KnownCatalogueSources.FromUrl(new Uri("https://lists.example.test/list.xml"), KnownCatalogueFormat.ChartCatalogs);
        var draft = LibrarySourceDraft.ForCatalogue(known, readers)!;

        Assert.Null(await draft.LoadAsync(Ct));

        var entries = Assert.Single(draft.Groups);
        Assert.NotEmpty(entries.Options);
        entries.Options[0].IsSelected = true;
        draft.IncludeAll = false;
        var source = Assert.IsType<ChartCatalogsFeedSource>(draft.Build());
        Assert.Equal([entries.Options[0].Value], source.Filter.Charts);
    }

    [Fact]
    public async Task A_remote_s100_catalogue_offers_regions_areas_and_resolutions()
    {
        var readers = new LibraryCatalogueReaders
        {
            S100Catalogue = (uri, _) =>
            {
                using var stream = File.OpenRead(TestPaths.Fixture("noaa-s102-catalog.xml"));
                return Task.FromResult(RemoteS100CatalogueReader.Read(stream, uri));
            },
        };
        var draft = LibrarySourceDraft.ForCatalogue(Known("noaa-s102"), readers)!;

        Assert.Null(await draft.LoadAsync(Ct));

        var scope = Assert.IsType<S100CatalogueScope>(draft.Scope);
        Assert.NotEmpty(draft.Groups);
        Assert.All(draft.Groups, g => Assert.False(string.IsNullOrEmpty(g.Key)));
        Assert.Null(scope.Resolutions[0].Value);  // "all" comes first

        // Nothing is downloaded on adding: the summary says so.
        Assert.NotEqual(scope.SelectionSummary, draft.ScopeSummary);

        var area = draft.Groups[0].Options[0];
        area.IsSelected = true;
        draft.IncludeAll = false;
        var source = Assert.IsType<S100CatalogueFeedSource>(draft.Build());
        Assert.Contains(source.Filter.Folders, f => f == area.Value || f == draft.Groups[0].Key);
        Assert.StartsWith(area.Label, source.DisplayName, StringComparison.Ordinal);
    }
}
