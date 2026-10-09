using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Collections.Noaa;
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

        Assert.Null(LibrarySourceDraft.ForPath(LibrarySourceKind.LocalManifest, "/charts/set.s100collection.json"));
        Assert.Null(LibrarySourceDraft.ForCatalogue(Known("noaa-s111"), Readers));
    }
}
