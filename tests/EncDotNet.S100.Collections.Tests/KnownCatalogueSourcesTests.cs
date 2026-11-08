using System.Text;
using EncDotNet.S100.Collections.KnownSources;

namespace EncDotNet.S100.Collections.Tests;

public class KnownCatalogueSourcesTests
{
    [Fact]
    public void Built_in_list_is_well_formed()
    {
        var all = KnownCatalogueSources.All;

        Assert.NotEmpty(all);
        Assert.Equal(all.Count, all.Select(s => s.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(all, s =>
        {
            Assert.Equal("https", s.CatalogUri.Scheme);
            Assert.False(string.IsNullOrWhiteSpace(s.Provider));
            Assert.NotEmpty(s.Region);
        });
    }

    [Fact]
    public void Built_in_list_points_at_the_feeds_the_viewer_reads()
    {
        Assert.Equal(NoaaEncFeedSource.DefaultCatalogUri, KnownCatalogueSources.Find("noaa-enc")!.CatalogUri);
        Assert.Equal(UsaceIencFeedSource.RiversCatalogUri, KnownCatalogueSources.Find("usace-ienc-rivers")!.CatalogUri);
        Assert.Equal(UsaceIencFeedSource.BuoysCatalogUri, KnownCatalogueSources.Find("USACE-IENC-BUOYS")!.CatalogUri);
        Assert.Equal(KnownCatalogueCoverage.Polygons, KnownCatalogueSources.Find("noaa-enc")!.Coverage);
        Assert.Null(KnownCatalogueSources.Find("nope"));
    }

    [Fact]
    public void NOAA_S102_is_a_remote_S100_catalogue_not_for_navigation()
    {
        var s102 = KnownCatalogueSources.Find("noaa-s102")!;

        Assert.Equal(KnownCatalogueFormat.S100ExchangeCatalogue, s102.Format);
        Assert.Equal("S-102", s102.Product);
        Assert.True(s102.NotForNavigation);
        Assert.Equal("noaa-s102-pds.s3.amazonaws.com", s102.CatalogUri.Host);
    }

    [Fact]
    public void Product_and_not_for_navigation_round_trip_and_default_when_absent()
    {
        var sources = new[]
        {
            new KnownCatalogueSource("a", "A", "P", ["R"], KnownCatalogueFormat.S100ExchangeCatalogue,
                new Uri("https://example.test/CATALOG.XML"), null, KnownCatalogueCoverage.Polygons, true, false,
                Product: "S-104", NotForNavigation: true),
            new KnownCatalogueSource("b", "B", "P", ["R"], KnownCatalogueFormat.NoaaEnc,
                new Uri("https://example.test/b.xml"), null, KnownCatalogueCoverage.Polygons, true, true),
        };
        using var stream = new MemoryStream();
        KnownCatalogueSources.Write(stream, sources);
        Assert.DoesNotContain("\"product\": null", Encoding.UTF8.GetString(stream.ToArray()), StringComparison.Ordinal);
        stream.Position = 0;

        var read = KnownCatalogueSources.Read(stream);
        Assert.Equal(["S-104", null], read.Select(s => s.Product));
        Assert.Equal([true, false], read.Select(s => s.NotForNavigation));
    }

    [Fact]
    public void Community_lists_are_known_with_no_coverage_editions_or_sizes()
    {
        var community = KnownCatalogueSources.All.Where(s => s.Format == KnownCatalogueFormat.ChartCatalogs).ToArray();

        Assert.NotEmpty(community);
        Assert.All(community, s =>
        {
            Assert.Equal("raw.githubusercontent.com", s.CatalogUri.Host);
            Assert.StartsWith("/chartcatalogs/catalogs/", s.CatalogUri.AbsolutePath, StringComparison.Ordinal);
            Assert.Equal(KnownCatalogueCoverage.None, s.Coverage);
            Assert.False(s.Editions);
            Assert.False(s.Sizes);
        });
    }

    [Fact]
    public void Every_built_in_entry_names_its_category()
    {
        using var stream = typeof(KnownCatalogueSources).Assembly.GetManifestResourceStream(
            "EncDotNet.S100.Collections.KnownSources.known-sources.json")!;
        using var document = System.Text.Json.JsonDocument.Parse(stream);

        Assert.All(document.RootElement.GetProperty("sources").EnumerateArray(), e =>
            Assert.True(e.TryGetProperty("category", out _), e.GetProperty("id").GetString()));
        Assert.Equal(KnownCatalogueCategory.Charts, KnownCatalogueSources.Find("noaa-enc")!.Category);
        Assert.Equal(KnownCatalogueCategory.Depth, KnownCatalogueSources.Find("noaa-s111")!.Category);
        Assert.Equal(KnownCatalogueCategory.Weather, KnownCatalogueSources.Find("noaa-erddap-currents")!.Category);
        Assert.Equal(KnownCatalogueCategory.Weather, KnownCatalogueSources.Find("ioos-erddap-hfradar")!.Category);
        Assert.Equal(KnownCatalogueCategory.Secom, KnownCatalogueSources.Find("ccg-s124-secom")!.Category);
        Assert.DoesNotContain(KnownCatalogueSources.All, s => s.Category == KnownCatalogueCategory.User);
    }

    [Fact]
    public void Category_is_read_and_written_and_follows_the_format_when_absent()
    {
        const string json = """
            {
              "version": 1,
              "sources": [
                { "id": "a", "name": "A", "provider": "P", "format": "weather", "category": "depth", "catalogUri": "https://a.test/c.xml",
                  "weather": { "service": "nwsAlerts", "model": "m" } },
                { "id": "b", "name": "B", "provider": "P", "format": "s100ExchangeCatalogue", "catalogUri": "https://b.test/c.xml" },
                { "id": "c", "name": "C", "provider": "P", "format": "noaaEnc", "category": "somethingNew", "catalogUri": "https://c.test/c.xml" }
              ]
            }
            """;

        var sources = KnownCatalogueSources.Read(new MemoryStream(Encoding.UTF8.GetBytes(json)));
        Assert.Equal([KnownCatalogueCategory.Depth, KnownCatalogueCategory.Depth, KnownCatalogueCategory.Charts], sources.Select(s => s.Category));

        var user = KnownCatalogueSources.FromUrl(new Uri("https://example.test/feed.json"), KnownCatalogueFormat.S100Feed);
        Assert.Equal(KnownCatalogueCategory.User, user.Category);
        using var stream = new MemoryStream();
        KnownCatalogueSources.Write(stream, [sources[0], user]);
        stream.Position = 0;
        Assert.Equal([KnownCatalogueCategory.Depth, KnownCatalogueCategory.User], KnownCatalogueSources.Read(stream).Select(s => s.Category));
    }

    [Fact]
    public void Entries_in_unknown_formats_or_without_a_url_are_skipped()
    {
        const string json = """
            {
              "version": 2,
              "sources": [
                { "id": "a", "name": "A", "provider": "P", "region": ["X"], "format": "noaaEnc", "catalogUri": "https://a.test/c.xml", "coverage": "polygons", "editions": true, "sizes": true },
                { "id": "b", "name": "B", "provider": "P", "region": ["X"], "format": "somethingNew", "catalogUri": "https://b.test/c.xml" },
                { "id": "c", "name": "C", "provider": "P", "region": ["X"], "format": "usaceIenc" },
                { "id": "d", "name": "D", "provider": "P", "format": "usaceIenc", "catalogUri": "https://d.test/c.xml", "coverage": "sparkly" }
              ]
            }
            """;

        var sources = KnownCatalogueSources.Read(new MemoryStream(Encoding.UTF8.GetBytes(json)));

        Assert.Equal(["a", "d"], sources.Select(s => s.Id));
        Assert.Equal(KnownCatalogueCoverage.None, sources[1].Coverage);
        Assert.Empty(sources[1].Region);
    }
}
