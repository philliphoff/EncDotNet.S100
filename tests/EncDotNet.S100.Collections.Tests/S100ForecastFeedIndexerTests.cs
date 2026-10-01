using System.Globalization;
using System.Net;
using System.Text;
using EncDotNet.S100.Collections.Downloads;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.KnownSources;

namespace EncDotNet.S100.Collections.Tests;

public sealed class S100ForecastFeedIndexerTests : IDisposable
{
    private static readonly Uri ModelsUri = new("https://noaa-s111-pds.s3.amazonaws.com/ed1.0.1/model_forecast_guidance/");

    private static readonly ForecastModel Cbofs = new("cbofs", "Chesapeake Bay", 6, 48);

    private static readonly ForecastModel Dbofs = new("dbofs", "Delaware Bay", 6, 48);

    private static readonly DateTimeOffset Run = new(2026, 9, 30, 18, 0, 0, TimeSpan.Zero);

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static S100ForecastFeedSource Source(ForecastShape shape = ForecastShape.Tiles, params ForecastModel[] models) =>
        new(Guid.NewGuid(), null, ModelsUri, models.Length == 0 ? [Cbofs] : models, shape);

    [Fact]
    public async Task A_model_indexes_its_latest_runs_tiles_stamped_with_the_run_and_its_window()
    {
        var server = new ForecastServer();
        var indexer = new S100ForecastFeedIndexer(new HttpClient(server), _temp.Path);

        var index = await indexer.IndexAsync(Source(), null, CancellationToken.None);

        Assert.Empty(index.Diagnostics);
        Assert.Equal(Run, index.PublishedAt);
        Assert.Equal(3, index.Items.Count);
        var tile = index.Items.Single(i => i.Name == "111US00_CBOFS_US4VA1DD");
        Assert.Equal("cbofs/111US00_CBOFS_US4VA1DD", tile.Key);
        Assert.Equal("S-111", tile.ProductSpec);
        Assert.Equal("Chesapeake Bay", tile.Title);
        Assert.Null(tile.Edition);
        Assert.NotNull(tile.Bounds);
        Assert.Equal("cbofs", tile.Properties[S100ForecastFeedIndexer.ModelProperty]);
        Assert.Equal(Run, S100ForecastFeedIndexer.RunOf(tile));
        Assert.Equal(Run.AddHours(48), S100ForecastFeedIndexer.ValidToOf(tile));
        Assert.DoesNotContain(LocalManifestIndexer.GroupProperty, tile.Properties.Keys);

        var remote = Assert.IsType<RemoteItemLocation>(tile.Location);
        Assert.Equal(new Uri(ModelsUri, "cbofs/2026/09/30/18/dcf2/tiles/111US00_CBOFS_20260930T18Z_US4VA1DD.h5"), remote.Uri);
        Assert.Equal(Run, remote.LastModified);
        Assert.Equal(ForecastServer.SizeOf(remote.Uri), remote.SizeBytes);
        Assert.StartsWith("forecasts/noaa-s111-pds.s3.amazonaws.com-", remote.DownloadFolder, StringComparison.Ordinal);
        Assert.Equal(["cbofs/2026/09/30/18/dcf2/"], server.ListedPrefixes.Select(p => p["ed1.0.1/model_forecast_guidance/".Length..]));
    }

    [Fact]
    public async Task One_file_per_model_is_the_runs_regional_file_covering_every_tile()
    {
        var indexer = new S100ForecastFeedIndexer(new HttpClient(new ForecastServer()), _temp.Path);

        var index = await indexer.IndexAsync(Source(ForecastShape.Regional), null, CancellationToken.None);

        var model = Assert.Single(index.Items);
        Assert.Equal("cbofs", model.Key);
        Assert.Equal("111US00_CBOFS", model.Name);
        Assert.Equal(3, model.Coverage!.Polygons.Count);
        var remote = Assert.IsType<RemoteItemLocation>(model.Location);
        Assert.Equal(new Uri(ModelsUri, "cbofs/2026/09/30/18/dcf2/regional/111US00_CBOFS_20260930T18Z.h5"), remote.Uri);
        Assert.Equal("111US00_CBOFS_20260930T18Z.h5", remote.Layout!.RelativePath);
        Assert.Equal(ForecastServer.SizeOf(remote.Uri), remote.SizeBytes);
    }

    [Fact]
    public async Task A_model_that_cannot_be_read_is_reported_and_the_others_still_index()
    {
        var indexer = new S100ForecastFeedIndexer(new HttpClient(new ForecastServer()), _temp.Path);

        var index = await indexer.IndexAsync(Source(ForecastShape.Tiles, Cbofs, Dbofs), null, CancellationToken.None);

        Assert.Equal(3, index.Items.Count);
        Assert.Contains("dbofs/CATALOG.XML", Assert.Single(index.Diagnostics).Path, StringComparison.Ordinal);
        Assert.Null(index.Fingerprint);
    }

    [Fact]
    public async Task Models_are_summarised_for_choosing()
    {
        var indexer = new S100ForecastFeedIndexer(new HttpClient(new ForecastServer()), _temp.Path);

        var summaries = await indexer.GetModelsAsync(ModelsUri, [Cbofs, Dbofs]);

        var cbofs = summaries.Single(s => s.Model == Cbofs);
        Assert.Equal(Run, cbofs.Run);
        Assert.Equal(3, cbofs.TileCount);
        Assert.True(cbofs.TileBytes > 0);
        Assert.True(cbofs.RegionalBytes > 0);
        Assert.NotNull(cbofs.Bounds);
        Assert.NotNull(summaries.Single(s => s.Model == Dbofs).Error);
    }

    [Fact]
    public async Task A_downloaded_run_is_outdated_once_a_later_run_is_listed()
    {
        var indexer = new S100ForecastFeedIndexer(new HttpClient(new ForecastServer()), _temp.Path);
        var tile = (await indexer.IndexAsync(Source(), null, CancellationToken.None)).Items[0];
        var downloader = new EncCellDownloader(new HttpClient(new ForecastServer()), Path.Combine(_temp.Path, "dl"));

        var cell = await downloader.DownloadAsync(tile);

        Assert.Equal(Run, cell.PublishedAt);
        Assert.False(cell.IsOlderThan(tile));
        var next = tile with { Location = ((RemoteItemLocation)tile.Location) with { LastModified = Run.AddHours(6) } };
        Assert.True(cell.IsOlderThan(next));
    }

    [Fact]
    public void NOAA_S111_is_a_known_forecast_feed_with_its_models()
    {
        var s111 = KnownCatalogueSources.Find("noaa-s111")!;

        Assert.Equal(KnownCatalogueFormat.S100ForecastModels, s111.Format);
        Assert.Equal(14, s111.Models.Count);
        Assert.Equal(new ForecastModel("cbofs", "Chesapeake Bay", 6, 48), s111.Models.Single(m => m.Id == "cbofs"));
        Assert.All(s111.Models, m => Assert.True(m.HorizonHours >= 48));
    }

    [Fact]
    public void A_forecast_feed_without_models_is_skipped()
    {
        var json = """
            { "version": 1, "sources": [
              { "id": "f", "name": "F", "format": "s100ForecastModels", "catalogUri": "https://example.test/models/" } ] }
            """;

        Assert.Empty(KnownCatalogueSources.Read(new MemoryStream(Encoding.UTF8.GetBytes(json))));
    }

    /// <summary>
    /// Serves the cbofs fixture catalogue (dbofs fails), a listing of its run
    /// folder (tiles and the regional file), and the files themselves.
    /// </summary>
    private sealed class ForecastServer : HttpMessageHandler
    {
        private static readonly string[] Keys =
        [
            "ed1.0.1/model_forecast_guidance/cbofs/2026/09/30/18/dcf2/regional/111US00_CBOFS_20260930T18Z.h5",
            "ed1.0.1/model_forecast_guidance/cbofs/2026/09/30/18/dcf2/tiles/111US00_CBOFS_20260930T18Z_US4MD1AE.h5",
            "ed1.0.1/model_forecast_guidance/cbofs/2026/09/30/18/dcf2/tiles/111US00_CBOFS_20260930T18Z_US4VA1DD.h5",
            "ed1.0.1/model_forecast_guidance/cbofs/2026/09/30/18/dcf2/tiles/111US00_CBOFS_20260930T18Z_US4VA1DE.h5",
        ];

        public List<string> ListedPrefixes { get; } = [];

        public static long SizeOf(Uri uri) => 1000 + uri.AbsolutePath.Length;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            if (uri.AbsolutePath == "/" && uri.Query.Contains("list-type=2", StringComparison.Ordinal))
            {
                var prefix = System.Web.HttpUtility.ParseQueryString(uri.Query)["prefix"] ?? string.Empty;
                lock (ListedPrefixes)
                    ListedPrefixes.Add(prefix);
                var xml = new StringBuilder("<ListBucketResult xmlns=\"http://s3.amazonaws.com/doc/2006-03-01/\"><IsTruncated>false</IsTruncated>");
                foreach (var key in Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)))
                {
                    xml.Append(CultureInfo.InvariantCulture,
                        $"<Contents><Key>{key}</Key><LastModified>2026-09-30T19:47:00.000Z</LastModified><Size>{SizeOf(new Uri(uri, "/" + key))}</Size></Contents>");
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(xml.Append("</ListBucketResult>").ToString()) });
            }

            if (uri.AbsolutePath.EndsWith("/cbofs/CATALOG.XML", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(File.ReadAllBytes(TestPaths.Fixture("noaa-s111-cbofs-catalog.xml"))),
                });
            }

            if (uri.AbsolutePath.EndsWith("/CATALOG.XML", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent("\x89HDF"u8.ToArray()) });
        }
    }
}
