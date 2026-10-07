using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Collections.Noaa;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

public sealed class AddToLibraryDialogViewModelTests : IDisposable
{
    private readonly LibraryTestContext _context = new();
    private readonly CollectionLibrary _library;

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

        await vm.LoadCatalogAsync(TestContext.Current.CancellationToken);

        Assert.True(vm.ConfirmCommand.CanExecute(null));
        var alaska = vm.States.Single(s => s.Value == "AK");
        Assert.Equal("Alaska (AK)", alaska.Label);
        Assert.Contains("All 6 datasets", vm.SelectionSummary);

        alaska.IsSelected = true;

        Assert.StartsWith("2 datasets", vm.SelectionSummary);
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

        await vm.LoadCatalogAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["Allegheny", "Arkansas", "Ohio"], vm.Rivers.Select(r => r.Value));
        Assert.Contains("All 4 datasets", vm.SelectionSummary);

        vm.Rivers.Single(r => r.Value == "Ohio").IsSelected = true;

        Assert.StartsWith("2 datasets", vm.SelectionSummary);
        Assert.Equal("USACE Inland ENC — Ohio", vm.NewCollectionName);

        vm.ConfirmCommand.Execute(null);

        var source = Assert.IsType<UsaceIencFeedSource>(Assert.Single(Assert.Single(_library.Collections).Sources).Definition);
        Assert.Equal(["Ohio"], source.Filter.Rivers);
        Assert.Equal("Ohio", source.DisplayName);
    }

    [Fact]
    public async Task An_unscoped_feed_source_is_named_after_the_catalogue()
    {
        var vm = new AddToLibraryDialogViewModel(_library, LoadFixtureCatalog);
        vm.Initialize(AddToLibraryKind.NoaaFeed, null, null);
        await vm.LoadCatalogAsync(TestContext.Current.CancellationToken);

        vm.ConfirmCommand.Execute(null);

        var source = Assert.IsType<NoaaEncFeedSource>(Assert.Single(Assert.Single(_library.Collections).Sources).Definition);
        Assert.True(source.Filter.IsUnscoped);
        Assert.Equal("NOAA ENC", source.DisplayName);
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
        await vm.LoadCatalogAsync(TestContext.Current.CancellationToken);

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
        await vm.LoadCatalogAsync(TestContext.Current.CancellationToken);

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
        Assert.Equal("Packages", Assert.Single(vm.FacetGroups).Title);

        await vm.LoadCatalogAsync(TestContext.Current.CancellationToken);

        // The repeated entry is listed once.
        Assert.Equal(["Base1", "Base2", "XX5RIV01"], vm.Charts.Select(c => c.Value));
        Assert.Equal("Published 2024-06-12", vm.Charts[0].Detail);
        Assert.StartsWith("All 3 packages", vm.SelectionSummary);
        Assert.Contains("2026-09-20", vm.CatalogueDateText);

        vm.ChartSearchText = "1750";
        var match = Assert.Single(vm.Charts);
        match.IsSelected = true;
        vm.ChartSearchText = string.Empty;

        Assert.Equal(3, vm.Charts.Count);
        Assert.StartsWith("1 packages", vm.SelectionSummary);
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

        await vm.LoadCatalogAsync(TestContext.Current.CancellationToken);

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
    public async Task Secom_service_lists_products_and_builds_a_scoped_source()
    {
        var known = EncDotNet.S100.Collections.KnownSources.KnownCatalogueSources.FromUrl(
            new Uri("https://secom.test/api/secom/v2/capability"), EncDotNet.S100.Collections.KnownSources.KnownCatalogueFormat.Secom);
        var description = new EncDotNet.S100.Collections.Indexing.SecomServiceDescription(
            [new CatalogFacetValue("S-122", 2, 2048), new CatalogFacetValue("S-124", 5, 5120)],
            7, 7, Truncated: false, EncDotNet.S100.Collections.Secom.SecomApiVersion.V2);
        Uri? described = null;
        var vm = new AddToLibraryDialogViewModel(_library, null, describeSecom: (uri, _, _) =>
        {
            described = uri;
            return Task.FromResult(description);
        });

        vm.Initialize(known, targetCollectionId: null);
        Assert.Equal(AddToLibraryKind.Secom, vm.Kind);
        Assert.True(vm.IsOnlineFeed);
        Assert.Equal("secom.test", vm.NewCollectionName);

        await vm.LoadCatalogAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new Uri("https://secom.test/api/secom/"), described);
        Assert.Equal(["S-122", "S-124"], vm.Products.Select(p => p.Value));
        Assert.StartsWith("5 objects", vm.Products[1].Detail);
        Assert.StartsWith("All 7 objects", vm.SelectionSummary);

        vm.Products.Single(p => p.Value == "S-124").IsSelected = true;
        Assert.StartsWith("5 objects", vm.SelectionSummary);
        Assert.Equal("secom.test — S-124", vm.NewCollectionName);

        vm.ConfirmCommand.Execute(null);

        var source = Assert.IsType<SecomSource>(Assert.Single(Assert.Single(_library.Collections).Sources).Definition);
        Assert.Equal(new Uri("https://secom.test/api/secom/"), source.ServiceUri);
        Assert.Equal(["S-124"], source.Filter.ProductSpecs);
    }

    [Fact]
    public async Task Secom_service_syncs_by_default_when_small_and_can_be_narrowed_to_the_map_view()
    {
        var known = EncDotNet.S100.Collections.KnownSources.KnownCatalogueSources.FromUrl(
            new Uri("https://secom.test/api/secom/"), EncDotNet.S100.Collections.KnownSources.KnownCatalogueFormat.Secom);
        var areas = new List<string?>();
        var vm = new AddToLibraryDialogViewModel(_library, null,
            describeSecom: (_, area, _) =>
            {
                areas.Add(area);
                var bytes = area is null ? 500L * 1024 * 1024 : 5120;  // the whole service is too big to sync
                return Task.FromResult(new EncDotNet.S100.Collections.Indexing.SecomServiceDescription(
                    [new CatalogFacetValue("S-124", area is null ? 90_000 : 5, bytes)], area is null ? 5_000 : 5, area is null ? 90_000 : 5,
                    Truncated: area is null, EncDotNet.S100.Collections.Secom.SecomApiVersion.V2));
            },
            currentMapView: () => new GeoBounds(49, -124, 49.6, -122.8));

        vm.Initialize(known, targetCollectionId: null);
        await vm.LoadCatalogAsync(TestContext.Current.CancellationToken);
        Assert.True(vm.IsSecom);
        Assert.False(vm.SecomSync);  // too big (and capped) to sync by default
        Assert.True(vm.CanScopeSecomToMapView);

        await vm.SetSecomInMapViewAsync(true, TestContext.Current.CancellationToken);

        Assert.Equal([null, "POLYGON((-124 49,-122.8 49,-122.8 49.6,-124 49.6,-124 49))"], areas);
        Assert.True(vm.SecomInMapView);
        Assert.True(vm.SecomSync);  // now small: synced by default
        Assert.StartsWith("Downloads ", vm.SecomSyncHint);
        Assert.Equal("secom.test — map area", vm.NewCollectionName);

        vm.ConfirmCommand.Execute(null);

        var source = Assert.IsType<SecomSource>(Assert.Single(Assert.Single(_library.Collections).Sources).Definition);
        Assert.True(source.Sync);
        Assert.Equal(areas[1], source.Filter.GeometryWkt);
        Assert.EndsWith("(map area)", source.DisplayName);
    }

    [Fact]
    public async Task S102_regions_list_their_areas_sized_when_opened_and_build_a_scoped_source()
    {
        var known = EncDotNet.S100.Collections.KnownSources.KnownCatalogueSources.Find("noaa-s102")!;
        var listed = new List<string>();
        var vm = new AddToLibraryDialogViewModel(
            _library,
            null,
            loadS100Catalogue: (uri, _) =>
            {
                using var stream = File.OpenRead(LibraryTestContext.RepoFile(
                    "tests", "EncDotNet.S100.Collections.Tests", "Fixtures", "noaa-s102-catalog.xml"));
                return Task.FromResult(EncDotNet.S100.Collections.RemoteCatalogues.RemoteS100CatalogueReader.Read(stream, uri));
            },
            listS100Folders: (catalogue, folders, _) =>
            {
                listed.AddRange(folders);
                IReadOnlyDictionary<Uri, EncDotNet.S100.Collections.RemoteCatalogues.S3Object>? sizes = catalogue.Items
                    .Where(i => folders.Any(f => EncDotNet.S100.Collections.RemoteCatalogues.RemoteS100Catalogue.FolderOf(i).StartsWith(f, StringComparison.Ordinal)))
                    .Select(i => ((RemoteItemLocation)i.Location).Uri)
                    .ToDictionary(u => u, u => new EncDotNet.S100.Collections.RemoteCatalogues.S3Object(u, 3_000_000, null));
                return Task.FromResult<IReadOnlyDictionary<Uri, EncDotNet.S100.Collections.RemoteCatalogues.S3Object>?>(sizes);
            });

        var one = LibraryItemViewModel.FormatBytes(3_000_000);
        var two = LibraryItemViewModel.FormatBytes(6_000_000);
        vm.Initialize(known, targetCollectionId: null);
        Assert.Equal(AddToLibraryKind.S100Catalogue, vm.Kind);
        Assert.True(vm.IsOnlineFeed);
        Assert.True(vm.HasReviewUse);

        await vm.LoadCatalogAsync(TestContext.Current.CancellationToken);

        Assert.True(vm.IsRegionPicker);
        Assert.False(vm.HasFacetTabs);
        Assert.Equal(["California", "Northeast", "Oregon", "Southeast"], vm.FacetGroups.Select(g => g.Title));
        Assert.Equal(["Port and Transit", "Port 4 m", "Transit 16 m"], vm.Resolutions.Select(r => r.Label));
        Assert.True(vm.HasResolutions);
        Assert.Contains("2026-09-30", vm.CatalogueDateText);
        Assert.Equal("5 tiles", vm.EverythingSummary);

        // The first region is listed on load; the others say so until opened.
        Assert.Equal(["California"], listed);
        Assert.Equal($"1 tiles · {one}", vm.FacetGroups[0].Options.Single().Detail);
        var northeast = vm.FacetGroups[1];
        Assert.Equal("2 tiles · sizing…", northeast.Options.Single().Detail);

        vm.SelectedFacetGroup = northeast;
        await Task.Yield();
        Assert.Equal(["California", "Northeast"], listed);
        Assert.Equal($"2 tiles · {two}", northeast.Options.Single().Detail);
        Assert.Equal("NORTHEAST · 1 AREAS", vm.AreasHeader);

        northeast.Options.Single().IsSelected = true;
        Assert.False(vm.IncludeAll);
        Assert.Equal(1, northeast.SelectedCount);
        Assert.Equal($"1 areas · 2 tiles · {two}", vm.SelectionSummary);
        Assert.Equal($"1 areas · 2 tiles · {two} · nothing downloads yet", vm.ScopeSummary);
        Assert.Equal("NOAA S-102 Bathymetry — Boston", vm.NewCollectionName);

        vm.SelectedResolution = vm.Resolutions[1];
        Assert.Equal($"1 areas · 1 tiles · {one}", vm.SelectionSummary);
        Assert.Equal($"1 tiles · {one}", northeast.Options.Single().Detail);
        Assert.Equal("NOAA S-102 Bathymetry — Boston · Port 4 m", vm.NewCollectionName);
        Assert.True(vm.CanContinueFromScope);

        vm.ConfirmCommand.Execute(null);

        var source = Assert.IsType<S100CatalogueFeedSource>(Assert.Single(Assert.Single(_library.Collections).Sources).Definition);
        Assert.Equal(known.CatalogUri, source.CatalogUri);
        Assert.Equal(["Northeast/Boston"], source.Filter.Folders);
        Assert.Equal(["port"], source.Filter.NavigationPurposes);
        Assert.Equal("Boston · Port 4 m", source.DisplayName);
    }

    [Fact]
    public async Task Ticking_a_value_chooses_only_what_is_selected()
    {
        var vm = new AddToLibraryDialogViewModel(_library, LoadFixtureCatalog);
        vm.Initialize(AddToLibraryKind.NoaaFeed, null, null);
        await vm.LoadCatalogAsync(TestContext.Current.CancellationToken);
        Assert.True(vm.IncludeAll);
        Assert.Equal("Everything", vm.ScopeDescription);
        Assert.Equal("Nothing selected yet", vm.OnlySelectedSummary);
        Assert.StartsWith("6 cells · ", vm.EverythingSummary);

        vm.States.Single(s => s.Value == "AK").IsSelected = true;

        Assert.False(vm.IncludeAll);
        Assert.True(vm.OnlySelected);
        Assert.Equal("Alaska", vm.ScopeDescription);
        Assert.Equal(1, vm.FacetGroups[0].SelectedCount);
        Assert.True(vm.CanContinueFromScope);

        vm.SelectNoneCommand.Execute(null);

        Assert.False(vm.IncludeAll);
        Assert.False(vm.CanContinueFromScope);
        Assert.True(vm.IsScopeSummaryWarning);
        Assert.Equal("Tick at least one item, or choose Everything.", vm.ScopeSummary);
    }

    [Fact]
    public async Task Include_all_builds_an_unscoped_source_and_keeps_the_ticks()
    {
        var vm = new AddToLibraryDialogViewModel(_library, LoadFixtureCatalog);
        vm.Initialize(AddToLibraryKind.NoaaFeed, null, null);
        await vm.LoadCatalogAsync(TestContext.Current.CancellationToken);
        var alaska = vm.States.Single(s => s.Value == "AK");
        alaska.IsSelected = true;
        Assert.Equal("NOAA ENC — Alaska", vm.NewCollectionName);

        vm.IncludeAll = true;

        Assert.True(alaska.IsSelected);
        Assert.Equal("NOAA ENC", vm.NewCollectionName);
        Assert.Equal("Everything is included; your 1 picks are kept if you switch back", vm.ScopeSummary);
        var source = Assert.IsType<NoaaEncFeedSource>(vm.BuildSource());
        Assert.True(source.Filter.IsUnscoped);
        Assert.Equal("NOAA ENC", source.DisplayName);

        vm.OnlySelected = true;

        Assert.Equal(["AK"], Assert.IsType<NoaaEncFeedSource>(vm.BuildSource()).Filter.States);
    }

    [Fact]
    public async Task A_typed_name_is_kept_and_chooses_a_new_collection()
    {
        var existing = _library.AddCollection("Mine", []);
        var vm = new AddToLibraryDialogViewModel(_library, LoadFixtureCatalog);
        vm.Initialize(AddToLibraryKind.NoaaFeed, null, existing.Id);
        await vm.LoadCatalogAsync(TestContext.Current.CancellationToken);
        Assert.True(vm.AddToExisting);
        Assert.Equal("Mine", vm.TargetDescription);
        Assert.Equal("Follows your selection until you type your own.", vm.NameHint);

        vm.NewCollectionName = "My charts";
        vm.States.Single(s => s.Value == "AK").IsSelected = true;

        Assert.True(vm.CreateNew);
        Assert.Equal("New collection", vm.TargetDescription);
        Assert.Equal("My charts", vm.NewCollectionName);
        Assert.Equal("Your name is kept.", vm.NameHint);

        vm.SelectedCollection = null;
        vm.SelectedCollection = vm.ExistingCollections.Single(c => c.Id == existing.Id);
        Assert.True(vm.AddToExisting);
    }

    [Fact]
    public async Task A_one_option_catalogue_is_a_single_entry()
    {
        var buoys = EncDotNet.S100.Collections.KnownSources.KnownCatalogueSources.Find("usace-ienc-buoys")!;
        var fixture = LibraryTestContext.RepoFile("tests", "EncDotNet.S100.Collections.Tests", "Fixtures", "usace-ienc-buoy.xml");
        var vm = new AddToLibraryDialogViewModel(_library, null, (_, _) =>
            Task.FromResult(EncDotNet.S100.Collections.Usace.UsaceIencProductCatalogReader.Read(fixture)));
        vm.Initialize(buoys, targetCollectionId: null);
        Assert.False(vm.IsSingleEntry);

        await vm.LoadCatalogAsync(TestContext.Current.CancellationToken);

        Assert.True(vm.IsSingleEntry);
        Assert.False(vm.ShowsChoices);
        Assert.NotNull(vm.SingleEntry);
        Assert.Equal("Its one download", vm.ScopeDescription);
        Assert.True(vm.CanContinueFromScope);
    }

    [Fact]
    public async Task Noaa_load_failure_is_reported_and_blocks_confirmation()
    {
        var vm = new AddToLibraryDialogViewModel(_library, (_, _) => throw new HttpRequestException("offline"));
        vm.Initialize(AddToLibraryKind.NoaaFeed, null, null);

        await vm.LoadCatalogAsync(TestContext.Current.CancellationToken);

        Assert.True(vm.HasLoadError);
        Assert.Equal("offline", vm.LoadError);
        Assert.False(vm.ConfirmCommand.CanExecute(null));
    }
}
