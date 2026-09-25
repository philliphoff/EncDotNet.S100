using EncDotNet.S100.Collections.KnownSources;
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
}
