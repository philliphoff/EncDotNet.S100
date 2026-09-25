using System.IO.Compression;
using System.Net;
using EncDotNet.S100.Collections.KnownSources;

namespace EncDotNet.S100.Collections.Tests;

public class CatalogueFormatDetectorTests
{
    [Theory]
    [InlineData("noaa-enc-prodcat.xml", KnownCatalogueFormat.NoaaEnc, "ENC Product Catalog")]
    [InlineData("usace-ienc-u37.xml", KnownCatalogueFormat.UsaceIenc, "IENC U37 Product Catalog")]
    [InlineData("usace-ienc-buoy.xml", KnownCatalogueFormat.UsaceIenc, null)]
    [InlineData("chartcatalogs-list.xml", KnownCatalogueFormat.ChartCatalogs, "Test Inland ENC Charts")]
    public void Recognises_supported_catalogues_by_root_element(string fixture, KnownCatalogueFormat format, string? title)
    {
        using var stream = File.OpenRead(TestPaths.Fixture(fixture));

        var probe = CatalogueFormatDetector.Probe(stream);

        Assert.Equal(format, probe.Format);
        if (title is not null)
            Assert.Equal(title, probe.Title);
    }

    [Fact]
    public void An_S100_exchange_catalogue_is_recognised_but_not_supported()
    {
        using var stream = File.OpenRead(TestPaths.Fixture("noaa-s104-catalog.xml"));

        var probe = CatalogueFormatDetector.Probe(stream);

        Assert.Null(probe.Format);
        Assert.True(probe.IsS100ExchangeCatalogue);
    }

    [Theory]
    [InlineData("<html><body>Not found</body></html>", "html")]
    [InlineData("not xml at all", null)]
    [InlineData("", null)]
    public void Other_content_is_not_a_catalogue(string content, string? root)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));

        var probe = CatalogueFormatDetector.Probe(stream);

        Assert.Null(probe.Format);
        Assert.Equal(root, probe.RootElement);
    }

    [Fact]
    public void Gzipped_and_truncated_documents_are_probed()
    {
        var xml = File.ReadAllBytes(TestPaths.Fixture("noaa-enc-prodcat.xml"));
        using var gz = new MemoryStream();
        using (var zip = new GZipStream(gz, CompressionMode.Compress, leaveOpen: true))
            zip.Write(xml);
        gz.Position = 0;

        Assert.Equal(KnownCatalogueFormat.NoaaEnc, CatalogueFormatDetector.Probe(gz).Format);
        Assert.Equal(KnownCatalogueFormat.NoaaEnc, CatalogueFormatDetector.Probe(new MemoryStream(xml[..900])).Format);
    }

    [Fact]
    public async Task ProbeAsync_fetches_the_url()
    {
        var client = new HttpClient(new Server(File.ReadAllBytes(TestPaths.Fixture("chartcatalogs-list.xml"))));

        var probe = await CatalogueFormatDetector.ProbeAsync(client, new Uri("https://example.test/list.xml"));

        Assert.Equal(KnownCatalogueFormat.ChartCatalogs, probe.Format);
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            CatalogueFormatDetector.ProbeAsync(new HttpClient(new Server(null)), new Uri("https://example.test/gone.xml")));
    }

    [Fact]
    public void User_catalogues_round_trip_through_a_known_sources_document()
    {
        var uri = new Uri("https://example.test/lists/TEST_Catalog.xml");
        var source = KnownCatalogueSources.FromUrl(uri, KnownCatalogueFormat.ChartCatalogs, "Test Inland ENC Charts");

        Assert.StartsWith("user-", source.Id, StringComparison.Ordinal);
        Assert.Equal(source.Id, KnownCatalogueSources.FromUrl(uri, KnownCatalogueFormat.ChartCatalogs).Id);
        Assert.Equal(["Custom"], source.Region);
        Assert.Equal("example.test", source.Provider);
        Assert.Equal(KnownCatalogueCoverage.None, source.Coverage);
        Assert.Equal("example.test", KnownCatalogueSources.FromUrl(uri, KnownCatalogueFormat.NoaaEnc, " ").Name);

        using var stream = new MemoryStream();
        KnownCatalogueSources.Write(stream, [source]);
        stream.Position = 0;
        var read = Assert.Single(KnownCatalogueSources.Read(stream));

        Assert.Equal((source.Id, source.Name, source.Format, source.CatalogUri, source.Coverage),
            (read.Id, read.Name, read.Format, read.CatalogUri, read.Coverage));
        Assert.Equal(source.Region, read.Region);
    }

    private sealed class Server(byte[]? body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(body is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
    }
}
