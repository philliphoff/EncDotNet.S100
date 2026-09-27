using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Noaa;
using EncDotNet.S100.Viewer.Library;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

public sealed class AddToLibraryDialogViewModelTests : IDisposable
{
    private readonly LibraryTestContext _context = new();
    private readonly LibraryService _library;

    public AddToLibraryDialogViewModelTests()
    {
        // Not initialized: no background indexing (and no network) runs.
        _library = _context.CreateService();
    }

    public void Dispose()
    {
        _library.Dispose();
        _context.Dispose();
    }

    private static Task<NoaaEncProductCatalog> LoadFixtureCatalog(Uri _, CancellationToken __) =>
        Task.FromResult(NoaaEncProductCatalogReader.Read(LibraryTestContext.RepoFile(
            "tests", "EncDotNet.S100.Collections.Tests", "Fixtures", "noaa-enc-prodcat.xml")));

    [Fact]
    public void Folder_defaults_to_a_new_collection_named_after_the_folder()
    {
        var folder = Path.Combine(_context.Root, "Alaska Charts");
        var vm = new AddToLibraryDialogViewModel(_library, null);
        bool? closed = null;
        vm.Closed += (_, ok) => closed = ok;

        vm.Initialize(AddToLibraryKind.Folder, folder, targetCollectionId: null);

        Assert.True(vm.CreateNew);
        Assert.Equal("Alaska Charts", vm.NewCollectionName);
        Assert.False(vm.HasExistingCollections);
        Assert.True(vm.ConfirmCommand.CanExecute(null));

        vm.ConfirmCommand.Execute(null);

        Assert.True(closed);
        var collection = Assert.Single(_library.Collections);
        Assert.Equal("Alaska Charts", collection.Definition.Name);
        var source = Assert.IsType<LocalFolderSource>(Assert.Single(collection.Sources).Definition);
        Assert.Equal(folder, source.Path);
    }

    [Fact]
    public void A_target_collection_is_preselected_and_receives_the_source()
    {
        var existing = _library.AddCollection("Mine", []);
        var zip = Path.Combine(_context.Root, "set.zip");
        var vm = new AddToLibraryDialogViewModel(_library, null);

        vm.Initialize(AddToLibraryKind.ExchangeSet, zip, existing.Id);

        Assert.False(vm.CreateNew);
        Assert.True(vm.AddToExisting);
        Assert.Equal(existing.Id, vm.SelectedCollection!.Id);

        vm.ConfirmCommand.Execute(null);

        var collection = Assert.Single(_library.Collections);
        Assert.IsType<ExchangeSetSource>(Assert.Single(collection.Sources).Definition);
    }

    [Fact]
    public void An_empty_new_name_cannot_be_confirmed()
    {
        var vm = new AddToLibraryDialogViewModel(_library, null);
        vm.Initialize(AddToLibraryKind.Folder, _context.Root, null);

        vm.NewCollectionName = "  ";

        Assert.False(vm.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public async Task Noaa_feed_lists_facets_and_builds_a_scoped_source()
    {
        var vm = new AddToLibraryDialogViewModel(_library, LoadFixtureCatalog);
        vm.Initialize(AddToLibraryKind.NoaaFeed, null, null);
        Assert.False(vm.ConfirmCommand.CanExecute(null));

        await vm.LoadCatalogAsync();

        Assert.True(vm.ConfirmCommand.CanExecute(null));
        var alaska = vm.States.Single(s => s.Value == "AK");
        Assert.Equal("Alaska (AK)", alaska.Label);
        Assert.Contains("All 6 cells", vm.SelectionSummary);

        alaska.IsSelected = true;

        Assert.StartsWith("2 cells", vm.SelectionSummary);
        Assert.Equal("NOAA ENC — Alaska", vm.NewCollectionName);

        vm.ConfirmCommand.Execute(null);

        var source = Assert.IsType<NoaaEncFeedSource>(Assert.Single(Assert.Single(_library.Collections).Sources).Definition);
        Assert.Equal(["AK"], source.Filter.States);
        Assert.Equal("Alaska", source.DisplayName);
    }

    [Fact]
    public async Task Usace_feed_lists_rivers_and_builds_a_scoped_source()
    {
        var fixture = LibraryTestContext.RepoFile("tests", "EncDotNet.S100.Collections.Tests", "Fixtures", "usace-ienc-u37.xml");
        var vm = new AddToLibraryDialogViewModel(
            _library, null, (_, _) => Task.FromResult(EncDotNet.S100.Collections.Usace.UsaceIencProductCatalogReader.Read(fixture)));
        vm.Initialize(AddToLibraryKind.UsaceFeed, null, null);

        Assert.True(vm.IsOnlineFeed);
        Assert.Equal("USACE Inland ENC", vm.NewCollectionName);
        var group = Assert.Single(vm.FacetGroups);
        Assert.Equal("Rivers", group.Title);

        await vm.LoadCatalogAsync();

        Assert.Equal(["Allegheny", "Arkansas", "Ohio"], vm.Rivers.Select(r => r.Value));
        Assert.Contains("All 4 cells", vm.SelectionSummary);

        vm.Rivers.Single(r => r.Value == "Ohio").IsSelected = true;

        Assert.StartsWith("2 cells", vm.SelectionSummary);
        Assert.Equal("USACE Inland ENC — Ohio", vm.NewCollectionName);

        vm.ConfirmCommand.Execute(null);

        var source = Assert.IsType<UsaceIencFeedSource>(Assert.Single(Assert.Single(_library.Collections).Sources).Definition);
        Assert.Equal(["Ohio"], source.Filter.Rivers);
        Assert.Equal("Ohio", source.DisplayName);
    }

    [Fact]
    public void Noaa_feed_has_three_facet_groups()
    {
        var vm = new AddToLibraryDialogViewModel(_library, LoadFixtureCatalog);
        vm.Initialize(AddToLibraryKind.NoaaFeed, null, null);

        Assert.Equal(3, vm.FacetGroups.Count);
        Assert.Same(vm.States, vm.FacetGroups[0].Options);
    }

    [Fact]
    public async Task Known_source_sets_the_catalogue_title_and_default_name()
    {
        var buoys = EncDotNet.S100.Collections.KnownSources.KnownCatalogueSources.Find("usace-ienc-buoys")!;
        Uri? requested = null;
        var fixture = LibraryTestContext.RepoFile("tests", "EncDotNet.S100.Collections.Tests", "Fixtures", "usace-ienc-buoy.xml");
        var vm = new AddToLibraryDialogViewModel(_library, null, (uri, _) =>
        {
            requested = uri;
            return Task.FromResult(EncDotNet.S100.Collections.Usace.UsaceIencProductCatalogReader.Read(fixture));
        });

        vm.Initialize(buoys, targetCollectionId: null);
        await vm.LoadCatalogAsync();

        Assert.Equal(UsaceIencFeedSource.BuoysCatalogUri, requested);
        Assert.Equal(buoys.Name, vm.Title);
        Assert.Equal(buoys.Name, vm.NewCollectionName);
        Assert.Equal(UsaceIencFeedSource.BuoysCatalogUri.AbsoluteUri, vm.SourceDescription);

        vm.ConfirmCommand.Execute(null);

        var source = Assert.IsType<UsaceIencFeedSource>(Assert.Single(Assert.Single(_library.Collections).Sources).Definition);
        Assert.Equal(UsaceIencFeedSource.BuoysCatalogUri, source.CatalogUri);
    }

    [Theory]
    [InlineData("2026-10-01", false)]
    [InlineData("2027-12-31", true)]
    public async Task Catalogue_date_is_shown_and_flagged_when_over_a_year_old(string today, bool stale)
    {
        // The U37 fixture is dated 2026-09-17.
        var fixture = LibraryTestContext.RepoFile("tests", "EncDotNet.S100.Collections.Tests", "Fixtures", "usace-ienc-u37.xml");
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(
            DateTimeOffset.Parse(today + "T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        var vm = new AddToLibraryDialogViewModel(
            _library, null, (_, _) => Task.FromResult(EncDotNet.S100.Collections.Usace.UsaceIencProductCatalogReader.Read(fixture)), clock);
        vm.Initialize(AddToLibraryKind.UsaceFeed, null, null);

        Assert.Null(vm.CatalogueDateText);
        await vm.LoadCatalogAsync();

        Assert.Contains("2026-09-17", vm.CatalogueDateText);
        Assert.Equal(stale, vm.IsCatalogueStale);
    }

    [Fact]
    public async Task Community_list_entries_are_searchable_and_build_a_scoped_source()
    {
        var known = EncDotNet.S100.Collections.KnownSources.KnownCatalogueSources.Find("chartcatalogs-ro-ienc")!;
        var fixture = LibraryTestContext.RepoFile("tests", "EncDotNet.S100.Collections.Tests", "Fixtures", "chartcatalogs-list.xml");
        var vm = new AddToLibraryDialogViewModel(_library, null, loadCommunityCatalog: (_, _) =>
            Task.FromResult(EncDotNet.S100.Collections.ChartCatalogs.ChartCatalogsProductCatalogReader.Read(fixture)));

        vm.Initialize(known, targetCollectionId: null);
        Assert.Equal(AddToLibraryKind.CommunityFeed, vm.Kind);
        Assert.True(vm.IsSearchable);
        Assert.Equal("Downloads", Assert.Single(vm.FacetGroups).Title);

        await vm.LoadCatalogAsync();

        // The repeated entry is listed once.
        Assert.Equal(["Base1", "Base2", "XX5RIV01"], vm.Charts.Select(c => c.Value));
        Assert.Equal("Published 2024-06-12", vm.Charts[0].Detail);
        Assert.StartsWith("All 3 downloads", vm.SelectionSummary);
        Assert.Contains("2026-09-20", vm.CatalogueDateText);

        vm.ChartSearchText = "1750";
        var match = Assert.Single(vm.Charts);
        match.IsSelected = true;
        vm.ChartSearchText = string.Empty;

        Assert.Equal(3, vm.Charts.Count);
        Assert.StartsWith("1 downloads", vm.SelectionSummary);
        Assert.Equal($"{known.Name} — River 1750 - 790 (Base2)", vm.NewCollectionName);

        vm.ConfirmCommand.Execute(null);

        var source = Assert.IsType<ChartCatalogsFeedSource>(Assert.Single(Assert.Single(_library.Collections).Sources).Definition);
        Assert.Equal(["Base2"], source.Filter.Charts);
        Assert.Equal(known.CatalogUri, source.CatalogUri);
    }

    [Fact]
    public async Task S100_feed_lists_products_and_builds_a_scoped_source()
    {
        var feedUri = new Uri("http://machine.test:8100/feed.json");
        var known = EncDotNet.S100.Collections.KnownSources.KnownCatalogueSources.FromUrl(
            feedUri, EncDotNet.S100.Collections.KnownSources.KnownCatalogueFormat.S100Feed, "Shared charts");
        CollectionItem Item(string name, string spec, long size) => new()
        {
            Key = name,
            ProductSpec = spec,
            Name = name,
            Location = new RemoteItemLocation(new Uri(feedUri, $"items/{name}.zip"), size),
        };
        var feed = new EncDotNet.S100.Collections.Feeds.S100FeedDocument(
            EncDotNet.S100.Collections.Feeds.S100Feed.FormatName, 1, "Shared charts",
            new DateTimeOffset(2026, 9, 25, 0, 0, 0, TimeSpan.Zero), "f1",
            [Item("A", "S-57", 1024), Item("B", "S-101", 2048), Item("C", "S-101", 2048)]);
        var vm = new AddToLibraryDialogViewModel(_library, null, loadS100Feed: (_, _) => Task.FromResult(feed));

        vm.Initialize(known, targetCollectionId: null);
        Assert.Equal(AddToLibraryKind.S100Feed, vm.Kind);
        Assert.True(vm.IsOnlineFeed);
        Assert.Equal("Shared charts", vm.NewCollectionName);
        Assert.Equal("Products", Assert.Single(vm.FacetGroups).Title);

        await vm.LoadCatalogAsync();

        Assert.Equal(["S-101", "S-57"], vm.Products.Select(p => p.Value));
        Assert.StartsWith("2 datasets", vm.Products[0].Detail);
        Assert.StartsWith("All 3 datasets", vm.SelectionSummary);
        Assert.Contains("2026-09-25", vm.CatalogueDateText);

        vm.Products.Single(p => p.Value == "S-101").IsSelected = true;

        Assert.StartsWith("2 datasets", vm.SelectionSummary);
        Assert.Equal("Shared charts — S-101", vm.NewCollectionName);

        vm.ConfirmCommand.Execute(null);

        var source = Assert.IsType<S100FeedSource>(Assert.Single(Assert.Single(_library.Collections).Sources).Definition);
        Assert.Equal(feedUri, source.FeedUri);
        Assert.Equal(["S-101"], source.Filter.ProductSpecs);
    }

    [Fact]
    public async Task Noaa_load_failure_is_reported_and_blocks_confirmation()
    {
        var vm = new AddToLibraryDialogViewModel(_library, (_, _) => throw new HttpRequestException("offline"));
        vm.Initialize(AddToLibraryKind.NoaaFeed, null, null);

        await vm.LoadCatalogAsync();

        Assert.True(vm.HasLoadError);
        Assert.Equal("offline", vm.LoadError);
        Assert.False(vm.ConfirmCommand.CanExecute(null));
    }
}
