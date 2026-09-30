using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Viewer.Library;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

public class CatalogueDirectoryDialogViewModelTests
{
    private static KnownCatalogueSource Source(
        string id, string region, KnownCatalogueCoverage coverage = KnownCatalogueCoverage.Polygons, bool editions = true,
        string provider = "Provider", string? country = null, KnownCatalogueFormat format = KnownCatalogueFormat.NoaaEnc) =>
        new(id, id.ToUpperInvariant(), provider, country is null ? [region] : [region, country], format,
            new Uri($"https://{id}.test/catalog.xml"), new Uri($"https://{id}.test/"), coverage, editions, Sizes: false);

    [Fact]
    public void Lists_entries_by_region_then_name_and_preselects_the_first()
    {
        var vm = new CatalogueDirectoryDialogViewModel([Source("b", "Europe"), Source("a", "North America"), Source("c", "Europe")]);

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
        ]);

        Assert.Equal(
            ["NORTH AMERICA · UNITED STATES", "NOAA", "USACE", "EUROPE", "AT", "BG", "SOUTH AMERICA", "BR"],
            vm.Rows.Select(r => r.IsGroupHeader ? r.HeaderText : r.Name));
        Assert.Equal("5", vm.CountText);
    }

    [Fact]
    public void Group_headers_are_not_selectable_and_not_counted()
    {
        var vm = new CatalogueDirectoryDialogViewModel([Source("a", "Europe"), Source("b", "Europe")]);
        var header = vm.Rows[0];
        Assert.True(header.IsGroupHeader);

        vm.SelectedEntry = vm.Rows[2];
        vm.SelectedEntry = header;
        vm.SelectedEntry = null;

        Assert.Equal("b", vm.SelectedEntry!.Source.Id);
        Assert.True(vm.SelectedEntry.IsSelected);
        Assert.False(vm.Rows[1].IsSelected);
        Assert.Equal("2", vm.CountText);
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
        ]);

        vm.SearchText = text;

        Assert.Equal(expected, vm.Rows.Where(r => !r.IsGroupHeader).Select(r => r.Source.Id));
        Assert.Equal($"{expected.Length} of 3", vm.CountText);
        Assert.False(vm.HasNoMatches);
    }

    [Fact]
    public void Search_keeps_the_choice_while_it_is_hidden()
    {
        var vm = new CatalogueDirectoryDialogViewModel([Source("a", "Europe"), Source("b", "Europe")]);
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
        var vm = new CatalogueDirectoryDialogViewModel([Source("a", "Europe")], null, store);
        var feed = KnownCatalogueSources.FromUrl(new Uri("http://machine.test:8100/feed.json"), KnownCatalogueFormat.S100Feed, "Shared");

        vm.Preselect(feed);

        Assert.Equal(feed.Id, vm.SelectedEntry!.Source.Id);
        Assert.Equal("CUSTOM", vm.Rows[^2].HeaderText);
        Assert.Empty(store.Sources);

        vm.Preselect(Source("a", "Europe"));
        Assert.Equal("a", vm.SelectedEntry!.Source.Id);
    }

    [Fact]
    public void Chips_describe_what_the_catalogue_provides()
    {
        var vm = new CatalogueDirectoryDialogViewModel([Source("a", "X", KnownCatalogueCoverage.None, editions: false)]);
        var entry = vm.Entries[0];

        Assert.True(entry.IsCoverageLimited);
        Assert.True(entry.IsEditionsLimited);
        Assert.True(entry.IsSizesLimited);
        Assert.Equal("No coverage until downloaded", entry.CoverageChip);
    }

    [Fact]
    public void Homepage_opens_through_the_url_opener()
    {
        Uri? opened = null;
        var vm = new CatalogueDirectoryDialogViewModel([Source("a", "X")], uri => opened = uri);

        vm.Entries[0].OpenHomepageCommand.Execute(null);

        Assert.Equal(new Uri("https://a.test/"), opened);
    }

    [Fact]
    public void Built_in_known_sources_are_listed()
    {
        var vm = new CatalogueDirectoryDialogViewModel(KnownCatalogueSources.All);

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
            Probe(new CatalogueProbe("RncProductCatalogChartCatalogs", KnownCatalogueFormat.ChartCatalogs, "Test Inland ENC Charts")));
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
        Assert.Equal("Recognised as chartcatalogs and added under Custom.", vm.UrlSuccess);
        Assert.False(vm.ShowsUrlHelp);
        Assert.Equal("CUSTOM", vm.Rows[^2].HeaderText);

        // Persisted: a new dialog lists it, and it can be removed.
        var reopened = new CatalogueDirectoryDialogViewModel([Source("a", "X")], null,
            new UserCatalogueStore(Path.Combine(context.Root, "catalogues.json")));
        var entry = Assert.Single(reopened.Entries, e => e.IsUser);
        Assert.False(reopened.CanAddByUrl);
        entry.RemoveCommand.Execute(null);

        Assert.DoesNotContain(reopened.Entries, e => e.IsUser);
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
            Probe(new CatalogueProbe(root, format, null, IsJson: url.EndsWith(".json", StringComparison.Ordinal)), requests));

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
            Probe(new CatalogueProbe("EncProductCatalog", KnownCatalogueFormat.NoaaEnc, null), requests));

        vm.CatalogueUrl = "https://b.test/catalog.xml";
        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)vm.AddUrlCommand).ExecuteAsync(null);

        Assert.Empty(requests);
        Assert.Equal("b", vm.SelectedEntry!.Source.Id);
    }

    [Fact]
    public async Task A_failed_fetch_is_reported()
    {
        var vm = new CatalogueDirectoryDialogViewModel([Source("a", "X")], null, null,
            (_, _) => throw new HttpRequestException("offline"));

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
}
