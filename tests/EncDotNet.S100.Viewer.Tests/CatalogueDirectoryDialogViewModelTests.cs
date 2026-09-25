using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Viewer.Library;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

public class CatalogueDirectoryDialogViewModelTests
{
    private static KnownCatalogueSource Source(
        string id, string region, KnownCatalogueCoverage coverage = KnownCatalogueCoverage.Polygons, bool editions = true) =>
        new(id, id.ToUpperInvariant(), "Provider", [region], KnownCatalogueFormat.NoaaEnc,
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
    public void Next_hands_on_the_selected_catalogue()
    {
        var vm = new CatalogueDirectoryDialogViewModel([Source("a", "X"), Source("b", "Y")]);
        KnownCatalogueSource? chosen = null;
        vm.Chosen += (_, s) => chosen = s;

        vm.SelectedEntry = vm.Entries[1];
        vm.NextCommand.Execute(null);

        Assert.Equal("b", chosen!.Id);
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
        Assert.Equal("example.test · Custom", added.ProviderAndRegion);
        Assert.Same(added, vm.Entries[^1]);

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
    public async Task Unsupported_urls_explain_why(string url, string? root, KnownCatalogueFormat? format, string expected)
    {
        var requests = new List<Uri>();
        var vm = new CatalogueDirectoryDialogViewModel([Source("a", "X")], null, null,
            Probe(new CatalogueProbe(root, format, null), requests));

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
