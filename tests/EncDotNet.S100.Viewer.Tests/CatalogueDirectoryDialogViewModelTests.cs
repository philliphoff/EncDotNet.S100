using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Viewer.Library;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

public class CatalogueDirectoryDialogViewModelTests
{
    private static KnownCatalogueSource Source(
        string id, string region, KnownCatalogueCoverage coverage = KnownCatalogueCoverage.Polygons, bool editions = true,
        string provider = "Provider", string? country = null, KnownCatalogueFormat format = KnownCatalogueFormat.NoaaEnc,
        KnownCatalogueCategory? category = null)
    {
        var source = new KnownCatalogueSource(id, id.ToUpperInvariant(), provider, country is null ? [region] : [region, country], format,
            new Uri($"https://{id}.test/catalog.xml"), new Uri($"https://{id}.test/"), coverage, editions, Sizes: false);
        return category is { } c ? source with { Category = c } : source;
    }

    private static int CountOf(CatalogueDirectoryDialogViewModel vm, KnownCatalogueCategory category) =>
        vm.Categories.Single(c => c.Category == category).Count;

    [Fact]
    public void Lists_entries_by_region_then_name_and_preselects_the_first()
    {
        var vm = new CatalogueDirectoryDialogViewModel([Source("b", "Europe"), Source("a", "North America"), Source("c", "Europe")], openUrl: null, userCatalogues: null, probe: null, loadSecomRegistry: null, probeSecom: null, session: null);

        Assert.Equal(["b", "c", "a"], vm.Entries.Select(e => e.Source.Id));
        Assert.Same(vm.Entries[0], vm.SelectedEntry);
        Assert.Equal("Provider · Europe", vm.Entries[0].ProviderAndRegion);
    }

    [Fact]
    public void Rows_group_catalogues_under_region_headers_in_list_order()
    {
        var vm = new CatalogueDirectoryDialogViewModel(
        [
            Source("noaa", "North America", country: "United States"),
            Source("usace", "North America", country: "United States"),
            Source("at", "Europe", country: "Austria"),
            Source("bg", "Europe", country: "Bulgaria"),
            Source("br", "South America", country: "Brazil"),
        ],
        openUrl: null,
        userCatalogues: null,
        probe: null,
        loadSecomRegistry: null,
        probeSecom: null,
        session: null);

        Assert.Equal(
            ["NORTH AMERICA · UNITED STATES", "NOAA", "USACE", "EUROPE", "AT", "BG", "SOUTH AMERICA", "BR"],
            vm.Rows.Select(r => r.IsGroupHeader ? r.HeaderText : r.Name));
        Assert.Equal("5", vm.Categories[0].CountText);
    }

    [Fact]
    public void Group_headers_are_not_selectable_and_not_counted()
    {
        var vm = new CatalogueDirectoryDialogViewModel([Source("a", "Europe"), Source("b", "Europe")], openUrl: null, userCatalogues: null, probe: null, loadSecomRegistry: null, probeSecom: null, session: null);
        var header = vm.Rows[0];
        Assert.True(header.IsGroupHeader);

        vm.SelectedEntry = vm.Rows[2];
        vm.SelectedEntry = header;
        vm.SelectedEntry = null;

        Assert.Equal("b", vm.SelectedEntry!.Source.Id);
        Assert.True(vm.SelectedEntry.IsSelected);
        Assert.False(vm.Rows[1].IsSelected);
        Assert.Equal(2, CountOf(vm, KnownCatalogueCategory.Charts));
        Assert.Equal(2, vm.Entries.Count);
    }

    [Theory]
    [InlineData("bulg", new[] { "bg" })]
    [InlineData("waterways", new[] { "at" })]
    [InlineData("south", new[] { "br" })]
    [InlineData("chartcatalogs", new[] { "at", "bg" })]
    public void Search_filters_by_name_provider_region_and_format(string text, string[] expected)
    {
        var vm = new CatalogueDirectoryDialogViewModel(
        [
            Source("at", "Europe", provider: "Waterways Austria", country: "Austria", format: KnownCatalogueFormat.ChartCatalogs),
            Source("bg", "Europe", country: "Bulgaria", format: KnownCatalogueFormat.ChartCatalogs),
            Source("br", "South America", country: "Brazil"),
        ],
        openUrl: null,
        userCatalogues: null,
        probe: null,
        loadSecomRegistry: null,
        probeSecom: null,
        session: null);

        vm.SearchText = text;

        Assert.Equal(expected, vm.Rows.Where(r => !r.IsGroupHeader).Select(r => r.Source.Id));
        Assert.Equal(expected.Length, CountOf(vm, KnownCatalogueCategory.Charts));
        Assert.False(vm.HasNoMatches);
    }

    [Fact]
    public void Search_keeps_the_choice_while_it_is_hidden()
    {
        var vm = new CatalogueDirectoryDialogViewModel([Source("a", "Europe"), Source("b", "Europe")], openUrl: null, userCatalogues: null, probe: null, loadSecomRegistry: null, probeSecom: null, session: null);
        vm.SelectedEntry = vm.Entries[1];

        vm.SearchText = "nothing like it";
        vm.SelectedEntry = null; // the list clears its highlight when the row disappears

        Assert.True(vm.HasNoMatches);
        Assert.Empty(vm.Rows);
        Assert.Equal("b", vm.SelectedEntry!.Source.Id);

        vm.SearchText = string.Empty;
        Assert.Contains(vm.SelectedEntry, vm.Rows);
    }

    [Fact]
    public void Preselect_lists_an_unknown_source_under_custom_for_the_session()
    {
        using var context = new LibraryTestContext();
        var store = new UserCatalogueStore(Path.Combine(context.Root, "catalogues.json"));
        var vm = new CatalogueDirectoryDialogViewModel([Source("a", "Europe")], null, store, probe: null, loadSecomRegistry: null, probeSecom: null, session: null);
        var feed = KnownCatalogueSources.FromUrl(new Uri("http://machine.test:8100/feed.json"), KnownCatalogueFormat.S100Feed, "Shared");

        vm.Preselect(feed);

        Assert.Equal(feed.Id, vm.SelectedEntry!.Source.Id);
        Assert.Equal(KnownCatalogueCategory.User, vm.SelectedCategory);
        Assert.Equal([feed.Id], vm.Rows.Select(r => r.Source.Id));
        Assert.Empty(store.Sources);

        vm.Preselect(Source("a", "Europe"));
        Assert.Equal("a", vm.SelectedEntry!.Source.Id);
        Assert.Equal(KnownCatalogueCategory.Charts, vm.SelectedCategory);
    }

    [Fact]
    public void Built_in_list_opens_on_charts_with_noaa_enc_chosen_and_counts_each_category()
    {
        var vm = new CatalogueDirectoryDialogViewModel(KnownCatalogueSources.All, openUrl: null, userCatalogues: null, probe: null, loadSecomRegistry: null, probeSecom: null, session: null);

        Assert.Equal(KnownCatalogueCategory.Charts, vm.SelectedCategory);
        Assert.Equal("noaa-enc", vm.SelectedEntry!.Source.Id);
        Assert.Equal(
            ["Charts", "Depth, tides & currents", "Weather & ocean", "SECOM services", "My sources"],
            vm.Categories.Select(c => c.Label));
        Assert.Equal([17, 3, 11, 1, 0], vm.Categories.Select(c => c.Count));
        Assert.True(vm.Categories[0].IsOpen);
        Assert.All(vm.Rows.Where(r => !r.IsGroupHeader), r => Assert.Equal(KnownCatalogueCategory.Charts, r.Category));

        // Region is the second grouping inside a category.
        Assert.Equal(
            ["NORTH AMERICA · UNITED STATES", "EUROPE", "SOUTH AMERICA"],
            vm.Rows.Where(r => r.IsGroupHeader).Select(r => r.HeaderText));
    }

    [Fact]
    public void Opening_a_category_lists_only_its_sources_and_keeps_the_choice()
    {
        var vm = new CatalogueDirectoryDialogViewModel(KnownCatalogueSources.All, openUrl: null, userCatalogues: null, probe: null, loadSecomRegistry: null, probeSecom: null, session: null);

        vm.Categories.Single(c => c.Category == KnownCatalogueCategory.Weather).SelectCommand.Execute(null);

        Assert.Equal(KnownCatalogueCategory.Weather, vm.SelectedCategory);
        Assert.Equal(11, vm.Rows.Count(r => !r.IsGroupHeader));
        Assert.Contains(vm.Rows, r => r.Source.Id == "noaa-erddap-currents");
        Assert.Equal(["NORTH AMERICA · UNITED STATES", "WORLDWIDE"], vm.Rows.Where(r => r.IsGroupHeader).Select(r => r.HeaderText));
        Assert.Equal("noaa-enc", vm.SelectedEntry!.Source.Id);
        Assert.False(vm.Categories[0].IsOpen);
    }

    [Fact]
    public void Search_spans_categories_with_match_counts_and_dims_the_empty_ones()
    {
        var vm = new CatalogueDirectoryDialogViewModel(
        [
            Source("enc", "Europe"),
            Source("s111", "Europe", provider: "Currents office", format: KnownCatalogueFormat.S100ForecastModels),
            Source("hf", "Europe", provider: "HF radar currents", category: KnownCatalogueCategory.Weather),
            Source("ww3", "Europe", category: KnownCatalogueCategory.Weather),
        ],
        openUrl: null,
        userCatalogues: null,
        probe: null,
        loadSecomRegistry: null,
        probeSecom: null,
        session: null);

        vm.SearchText = "currents";

        Assert.True(vm.IsSearching);
        Assert.Equal(["Depth, tides & currents", "S111", "Weather & ocean", "HF"],
            vm.Rows.Select(r => r.IsGroupHeader ? r.GroupTitle : r.Name));
        Assert.Equal([0, 1, 1, 0, 0], vm.Categories.Select(c => c.Count));
        Assert.Equal([true, false, false, true, true], vm.Categories.Select(c => c.IsDimmed));
        Assert.DoesNotContain(vm.Categories, c => c.IsOpen);

        // Clearing returns to the open category.
        vm.SearchText = string.Empty;
        Assert.Equal(KnownCatalogueCategory.Charts, vm.SelectedCategory);
        Assert.Equal(["enc"], vm.Rows.Where(r => !r.IsGroupHeader).Select(r => r.Source.Id));
        Assert.Equal([1, 1, 2, 0, 0], vm.Categories.Select(c => c.Count));
        Assert.DoesNotContain(vm.Categories, c => c.IsDimmed);
    }

    [Fact]
    public void A_source_chosen_from_search_results_is_shown_in_its_category_when_the_search_clears()
    {
        var vm = new CatalogueDirectoryDialogViewModel(
            [Source("enc", "Europe"), Source("s111", "Europe", format: KnownCatalogueFormat.S100ForecastModels)],
            openUrl: null,
            userCatalogues: null,
            probe: null,
            loadSecomRegistry: null,
            probeSecom: null,
            session: null);

        vm.SearchText = "s111";
        vm.SelectedEntry = vm.Rows.Single(r => !r.IsGroupHeader);
        Assert.Equal(2, vm.Rows.Count); // the rows don't change under the pointer

        vm.SearchText = string.Empty;

        Assert.Equal(KnownCatalogueCategory.Depth, vm.SelectedCategory);
        Assert.Contains(vm.SelectedEntry, vm.Rows);
    }

    [Fact]
    public void Choosing_a_category_clears_the_search()
    {
        var vm = new CatalogueDirectoryDialogViewModel(
            [Source("enc", "Europe"), Source("s111", "Europe", format: KnownCatalogueFormat.S100ForecastModels)],
            openUrl: null,
            userCatalogues: null,
            probe: null,
            loadSecomRegistry: null,
            probeSecom: null,
            session: null);
        vm.SearchText = "nothing like it";
        Assert.True(vm.HasNoMatches);

        vm.Categories.Single(c => c.Category == KnownCatalogueCategory.Charts).SelectCommand.Execute(null);

        Assert.Equal(string.Empty, vm.SearchText);
        Assert.False(vm.HasNoMatches);
        Assert.Equal(["enc"], vm.Rows.Where(r => !r.IsGroupHeader).Select(r => r.Source.Id));
    }

    [Fact]
    public void Add_by_url_opens_my_sources_with_the_form_and_carries_a_searched_url()
    {
        var vm = new CatalogueDirectoryDialogViewModel([Source("a", "X")], null, null,
            Probe(new CatalogueProbe(null, null, null)),
            loadSecomRegistry: null,
            probeSecom: null,
            session: null);
        var focused = 0;
        vm.UrlEntryRequested += (_, _) => focused++;
        Assert.False(vm.ShowsUrlForm);

        vm.SearchText = "https://example.test/lists/catalog.xml";
        Assert.True(vm.HasNoMatches);
        Assert.Equal("No source matches “https://example.test/lists/catalog.xml”.", vm.NoMatchesText);
        vm.ShowAddByUrlCommand.Execute(null);

        Assert.Equal(KnownCatalogueCategory.User, vm.SelectedCategory);
        Assert.Equal(string.Empty, vm.SearchText);
        Assert.True(vm.ShowsUrlForm);
        Assert.Equal("https://example.test/lists/catalog.xml", vm.CatalogueUrl);
        Assert.True(vm.IsCategoryEmpty);
        Assert.Equal("Nothing added yet. Paste a catalogue or feed URL above.", vm.EmptyCategoryText);
        Assert.Equal(1, focused);

        // Search text that isn't a URL isn't carried; searching hides the form.
        vm.CatalogueUrl = string.Empty;
        vm.SearchText = "weather fax";
        vm.ShowAddByUrlCommand.Execute(null);
        Assert.Equal(string.Empty, vm.CatalogueUrl);
        vm.SearchText = "a";
        Assert.False(vm.ShowsUrlForm);
    }

    [Fact]
    public void Without_a_probe_there_is_no_add_by_url()
    {
        var vm = new CatalogueDirectoryDialogViewModel([Source("a", "X")], openUrl: null, userCatalogues: null, probe: null, loadSecomRegistry: null, probeSecom: null, session: null);

        Assert.False(vm.ShowAddByUrlCommand.CanExecute(null));
        vm.SelectedCategory = KnownCatalogueCategory.User;
        Assert.False(vm.ShowsUrlForm);
        Assert.Equal("Nothing added yet.", vm.EmptyCategoryText);
    }

    [Fact]
    public void Limitations_state_only_what_the_source_cannot_tell_in_advance()
    {
        var vm = new CatalogueDirectoryDialogViewModel(
        [
            Source("full", "X"),
            Source("bare", "X", KnownCatalogueCoverage.None, editions: false),
            Source("forecast", "X", KnownCatalogueCoverage.None, editions: false, format: KnownCatalogueFormat.S100ForecastModels) with { Pilot = true },
        ],
        openUrl: null,
        userCatalogues: null,
        probe: null,
        loadSecomRegistry: null,
        probeSecom: null,
        session: null);

        // Source() publishes no sizes; a full source would say nothing.
        Assert.Equal(["Download sizes are not known in advance."], vm.Entries.Single(e => e.Source.Id == "full").Limitations);
        Assert.Equal(
            ["No coverage on the map until it is downloaded.", "New editions are not detected; refresh to check.", "Download sizes are not known in advance."],
            vm.Entries.Single(e => e.Source.Id == "bare").Limitations);

        // A forecast feed tracks runs, not editions; a pilot says so.
        var forecast = vm.Entries.Single(e => e.Source.Id == "forecast");
        Assert.DoesNotContain("New editions are not detected; refresh to check.", forecast.Limitations);
        Assert.Contains("A pilot service: it may cover little and may stop.", forecast.Limitations);
        Assert.StartsWith("No coverage on the map until it is downloaded · Download sizes", forecast.LimitationsLine, StringComparison.Ordinal);

        var noaa = new CatalogueDirectoryDialogViewModel(KnownCatalogueSources.All, openUrl: null, userCatalogues: null, probe: null, loadSecomRegistry: null, probeSecom: null, session: null).SelectedEntry!;
        Assert.False(noaa.HasLimitations);
    }

    [Fact]
    public void Rows_name_the_country_only_when_the_header_does_not()
    {
        var vm = new CatalogueDirectoryDialogViewModel(KnownCatalogueSources.All, openUrl: null, userCatalogues: null, probe: null, loadSecomRegistry: null, probeSecom: null, session: null);

        Assert.Equal("NOAA Office of Coast Survey", vm.SelectedEntry!.RowDetail);
        Assert.Equal("NOAA Office of Coast Survey · United States", vm.SelectedEntry.ProviderAndPlace);
        Assert.EndsWith(" · Austria", vm.Entries.Single(e => e.Source.Id == "chartcatalogs-at-ienc").RowDetail, StringComparison.Ordinal);

        // Search results have no region header, so every row says where.
        vm.SearchText = "NOAA ENC";
        Assert.Equal("NOAA Office of Coast Survey · United States", vm.SelectedEntry.RowDetail);
    }

    [Fact]
    public void Copying_a_catalogue_url_goes_through_the_view()
    {
        var vm = new CatalogueDirectoryDialogViewModel([Source("a", "X")], openUrl: null, userCatalogues: null, probe: null, loadSecomRegistry: null, probeSecom: null, session: null);
        string? copied = null;
        vm.CopyRequested += (_, text) => copied = text;

        vm.SelectedEntry!.CopyUrlCommand.Execute(null);

        Assert.Equal("https://a.test/catalog.xml", copied);
        Assert.True(vm.HasSelection);
    }

    [Fact]
    public void Homepage_opens_through_the_url_opener()
    {
        Uri? opened = null;
        var vm = new CatalogueDirectoryDialogViewModel([Source("a", "X")], uri => opened = uri, userCatalogues: null, probe: null, loadSecomRegistry: null, probeSecom: null, session: null);

        vm.Entries[0].OpenHomepageCommand.Execute(null);

        Assert.Equal(new Uri("https://a.test/"), opened);
    }

    [Fact]
    public void Built_in_known_sources_are_listed()
    {
        var vm = new CatalogueDirectoryDialogViewModel(KnownCatalogueSources.All, openUrl: null, userCatalogues: null, probe: null, loadSecomRegistry: null, probeSecom: null, session: null);

        Assert.Contains(vm.Entries, e => e.Source.Id == "noaa-enc");
        Assert.Contains(vm.Entries, e => e.Source.Id == "usace-ienc-buoys");
    }

    private static Func<Uri, CancellationToken, Task<CatalogueProbe>> Probe(CatalogueProbe result, List<Uri>? requests = null) =>
        (uri, _) =>
        {
            requests?.Add(uri);
            return Task.FromResult(result);
        };

    [Fact]
    public async Task A_catalogue_url_is_recognised_saved_and_selected()
    {
        using var context = new LibraryTestContext();
        var store = new UserCatalogueStore(Path.Combine(context.Root, "catalogues.json"));
        var vm = new CatalogueDirectoryDialogViewModel([Source("a", "X")], null, store,
            Probe(new CatalogueProbe("RncProductCatalogChartCatalogs", KnownCatalogueFormat.ChartCatalogs, "Test Inland ENC Charts")),
            loadSecomRegistry: null,
            probeSecom: null,
            session: null);
        Assert.True(vm.CanAddByUrl);
        Assert.False(vm.AddUrlCommand.CanExecute(null));

        vm.CatalogueUrl = " https://example.test/lists/TEST_Catalog.xml ";
        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)vm.AddUrlCommand).ExecuteAsync(null);

        Assert.False(vm.HasUrlError);
        Assert.Equal(string.Empty, vm.CatalogueUrl);
        var added = vm.SelectedEntry!;
        Assert.True(added.IsUser);
        Assert.Equal("Test Inland ENC Charts", added.Name);
        Assert.Equal("example.test · added by URL", added.ProviderAndRegion);
        Assert.Equal("chartcatalogs", added.FormatText);
        Assert.Same(added, vm.Entries[^1]);
        Assert.Equal("Recognised as chartcatalogs and added below.", vm.UrlSuccess);
        Assert.False(vm.ShowsUrlHelp);
        Assert.Equal(KnownCatalogueCategory.User, vm.SelectedCategory);
        Assert.Same(added, Assert.Single(vm.Rows));
        Assert.Equal(1, CountOf(vm, KnownCatalogueCategory.User));

        // Persisted: a new dialog lists it, and it can be removed.
        var reopened = new CatalogueDirectoryDialogViewModel([Source("a", "X")], null,
            new UserCatalogueStore(Path.Combine(context.Root, "catalogues.json")),
            probe: null,
            loadSecomRegistry: null,
            probeSecom: null,
            session: null);
        var entry = Assert.Single(reopened.Entries, e => e.IsUser);
        Assert.False(reopened.CanAddByUrl);
        reopened.SelectedCategory = KnownCatalogueCategory.User;
        entry.RemoveCommand.Execute(null);

        Assert.DoesNotContain(reopened.Entries, e => e.IsUser);
        Assert.Equal(KnownCatalogueCategory.User, reopened.SelectedCategory);
        Assert.True(reopened.IsCategoryEmpty);
        Assert.Empty(new UserCatalogueStore(Path.Combine(context.Root, "catalogues.json")).Sources);
    }

    [Theory]
    [InlineData("ftp://example.test/c.xml", null, null, "http or https")]
    [InlineData("https://example.test/c.xml", "html", null, "root element html")]
    [InlineData("https://example.test/c.xml", null, null, "did not return XML")]
    [InlineData("https://example.test/c.xml", "S100_ExchangeCatalogue", null, "S-100")]
    [InlineData("https://example.test/c.json", null, null, "JSON that is not an S-100 feed")]
    public async Task Unsupported_urls_explain_why(string url, string? root, KnownCatalogueFormat? format, string expected)
    {
        var requests = new List<Uri>();
        var vm = new CatalogueDirectoryDialogViewModel([Source("a", "X")], null, null,
            Probe(new CatalogueProbe(root, format, null, IsJson: url.EndsWith(".json", StringComparison.Ordinal)), requests),
            loadSecomRegistry: null,
            probeSecom: null,
            session: null);

        vm.CatalogueUrl = url;
        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)vm.AddUrlCommand).ExecuteAsync(null);

        Assert.True(vm.HasUrlError);
        Assert.Contains(expected, vm.UrlError);
        Assert.DoesNotContain(vm.Entries, e => e.IsUser);
        Assert.Equal(url.StartsWith("ftp", StringComparison.Ordinal) ? 0 : 1, requests.Count);
    }

    [Fact]
    public async Task A_url_already_listed_is_selected_without_fetching()
    {
        var requests = new List<Uri>();
        var vm = new CatalogueDirectoryDialogViewModel([Source("a", "X"), Source("b", "Y")], null, null,
            Probe(new CatalogueProbe("EncProductCatalog", KnownCatalogueFormat.NoaaEnc, null), requests),
            loadSecomRegistry: null,
            probeSecom: null,
            session: null);

        vm.CatalogueUrl = "https://b.test/catalog.xml";
        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)vm.AddUrlCommand).ExecuteAsync(null);

        Assert.Empty(requests);
        Assert.Equal("b", vm.SelectedEntry!.Source.Id);
    }

    [Fact]
    public async Task A_failed_fetch_is_reported()
    {
        var vm = new CatalogueDirectoryDialogViewModel([Source("a", "X")], null, null,
            (_, _) => throw new HttpRequestException("offline"),
            loadSecomRegistry: null,
            probeSecom: null,
            session: null);

        vm.CatalogueUrl = "https://example.test/c.xml";
        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)vm.AddUrlCommand).ExecuteAsync(null);

        Assert.Equal("offline", vm.UrlError);
        Assert.False(vm.IsChecking);
    }

    [Fact]
    public void An_unreadable_store_reads_as_empty()
    {
        using var context = new LibraryTestContext();
        var path = Path.Combine(context.Root, "catalogues.json");
        File.WriteAllText(path, "{ not json");

        Assert.Empty(new UserCatalogueStore(path).Sources);
    }

    [Fact]
    public void Reopening_in_the_same_session_returns_to_the_last_source_and_category()
    {
        KnownCatalogueSource[] sources =
        [
            Source("chart", "Europe", category: KnownCatalogueCategory.Charts),
            Source("wx", "Worldwide", category: KnownCatalogueCategory.Weather),
            Source("depth", "Worldwide", category: KnownCatalogueCategory.Depth),
        ];
        var session = new CatalogueDirectorySession();

        var first = new CatalogueDirectoryDialogViewModel(sources, session: session, openUrl: null, userCatalogues: null, probe: null, loadSecomRegistry: null, probeSecom: null);
        Assert.Equal("chart", first.SelectedEntry!.Source.Id);
        first.SelectedEntry = first.Entries.Single(e => e.Source.Id == "wx");
        first.SelectedCategory = KnownCatalogueCategory.Depth;  // browsing on without choosing

        var second = new CatalogueDirectoryDialogViewModel(sources, session: session, openUrl: null, userCatalogues: null, probe: null, loadSecomRegistry: null, probeSecom: null);

        Assert.Equal("wx", second.SelectedEntry!.Source.Id);
        Assert.Equal(KnownCatalogueCategory.Depth, second.SelectedCategory);
    }

    [Fact]
    public void Without_a_session_every_open_starts_on_the_first_source()
    {
        KnownCatalogueSource[] sources =
        [
            Source("chart", "Europe", category: KnownCatalogueCategory.Charts),
            Source("wx", "Worldwide", category: KnownCatalogueCategory.Weather),
        ];
        var first = new CatalogueDirectoryDialogViewModel(sources, openUrl: null, userCatalogues: null, probe: null, loadSecomRegistry: null, probeSecom: null, session: null);
        first.SelectedEntry = first.Entries.Single(e => e.Source.Id == "wx");

        var second = new CatalogueDirectoryDialogViewModel(sources, openUrl: null, userCatalogues: null, probe: null, loadSecomRegistry: null, probeSecom: null, session: null);

        Assert.Equal("chart", second.SelectedEntry!.Source.Id);
        Assert.Equal(KnownCatalogueCategory.Charts, second.SelectedCategory);
    }

    [Fact]
    public void A_remembered_source_that_is_gone_falls_back_to_the_first()
    {
        var session = new CatalogueDirectorySession { LastSourceId = "removed", LastCategory = KnownCatalogueCategory.Charts };

        var vm = new CatalogueDirectoryDialogViewModel([Source("chart", "Europe", category: KnownCatalogueCategory.Charts)], session: session, openUrl: null, userCatalogues: null, probe: null, loadSecomRegistry: null, probeSecom: null);

        Assert.Equal("chart", vm.SelectedEntry!.Source.Id);
        Assert.Equal("chart", session.LastSourceId);
    }
}
