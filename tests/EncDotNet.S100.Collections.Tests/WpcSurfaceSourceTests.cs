using System.Net;
using System.Text;
using EncDotNet.S100.Collections.Downloads;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Collections.Weather;
using Microsoft.Extensions.Time.Testing;

namespace EncDotNet.S100.Collections.Tests;

/// <summary>
/// Issue #1301 (V27, V28, V36): the "Surface analysis — WPC" Library source —
/// its known-sources entry, the bulletins' times, one item per chosen bulletin
/// dated by its analysis, WPC's domain, and staleness after 6 hours.
/// </summary>
public sealed class WpcSurfaceSourceTests : IDisposable
{
    private const string AnalysisUri = "https://tgftp.nws.noaa.gov/data/raw/as/asus02.kwbc.cod.sus.txt";
    private const string ForecastUri = "https://tgftp.nws.noaa.gov/data/raw/fs/fsus02.kwbc.cod.srp.txt";

    // The fixtures (tests/datasets/CODSUS): the HR analysis valid 2026-11-04 00Z,
    // and the forecast bulletin issued 2026-11-05 04:02Z, valid 12Z and 00Z.
    private static readonly DateTimeOffset AnalysisTime = new(2026, 11, 4, 0, 0, 0, TimeSpan.Zero);

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static KnownCatalogueSource Known => KnownCatalogueSources.Find("noaa-wpc-surface")
        ?? throw new InvalidOperationException("No WPC source.");

    private static WeatherFeedSource Feed(params string[] bulletins) =>
        new(Guid.NewGuid(), null, Known.CatalogUri, Known.Weather!, new WeatherSelection(Datasets: bulletins))
        {
            Licence = Known.Licence,
            Attribution = Known.Attribution,
        };

    private static BulletinServer Server() => new()
    {
        [AnalysisUri] = (File.ReadAllText(TestPaths.Dataset("CODSUS", "codsus-hr-2026110400.txt")), AnalysisTime.AddMinutes(75)),
        [ForecastUri] = (File.ReadAllText(TestPaths.Dataset("CODSUS", "codsrp-2026110504.txt")), new DateTimeOffset(2026, 11, 5, 4, 5, 0, TimeSpan.Zero)),
    };

    [Fact]
    public void The_known_source_offers_the_analysis_by_default_and_the_forecast_fronts_refreshing_every_3_hours()
    {
        var known = Known;
        var service = known.Weather!;

        Assert.Equal("Surface analysis — WPC", known.Name);
        Assert.True(known.NotForNavigation);
        Assert.Equal("Public domain", known.Licence);
        Assert.Equal(WeatherServiceKind.WpcBulletins, service.Kind);
        Assert.True(service.IsUsable);
        Assert.Equal(TimeSpan.FromHours(6), service.StaleAfter);
        Assert.Equal(3, service.RefreshHours);
        Assert.Equal(["wpc-analysis", "wpc-forecast"], service.Datasets.Select(d => d.Id));
        Assert.Equal(["wpc-analysis"], new WeatherSelection().DatasetsOf(service).Select(d => d.Id));
        Assert.Equal(new Uri(AnalysisUri), service.BulletinUri(service.Datasets[0]));
        Assert.Contains("North America", known.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void A_bulletin_entry_round_trips_through_a_known_sources_document()
    {
        using var stream = new MemoryStream();
        KnownCatalogueSources.Write(stream, [Known]);
        stream.Position = 0;

        var service = Assert.Single(KnownCatalogueSources.Read(stream)).Weather!;

        Assert.Equal(WeatherServiceKind.WpcBulletins, service.Kind);
        Assert.Equal(
            Known.Weather!.Datasets.Select(d => (d.Id, d.Name, d.File, d.Default)),
            service.Datasets.Select(d => (d.Id, d.Name, d.File, d.Default)));
        Assert.All(service.Datasets, d => Assert.Empty(d.Variables));
        Assert.Equal(6, service.StaleAfterHours);
        Assert.Equal(3, service.RefreshHours);
    }

    [Fact]
    public void An_analysis_is_dated_by_its_heading_and_a_forecast_by_its_sections()
    {
        var analysis = WpcBulletinTimes.TryRead(File.ReadAllText(TestPaths.Dataset("CODSUS", "codsus-hr-2026110400.txt")), AnalysisTime.AddHours(1))!;
        var forecast = WpcBulletinTimes.TryRead(File.ReadAllText(TestPaths.Dataset("CODSUS", "codsrp-2026110504.txt")), new DateTimeOffset(2026, 11, 5, 4, 5, 0, TimeSpan.Zero))!;

        Assert.Equal(AnalysisTime, analysis.Issued);
        Assert.Equal(AnalysisTime, analysis.Run);
        Assert.Null(analysis.FirstLeadHours);
        Assert.Equal(new DateTimeOffset(2026, 11, 5, 4, 2, 0, TimeSpan.Zero), forecast.Issued);
        Assert.Equal(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero), forecast.FirstValid);
        Assert.Equal(new DateTimeOffset(2026, 11, 6, 0, 0, 0, TimeSpan.Zero), forecast.LastValid);
        Assert.Equal(new DateTimeOffset(2026, 11, 5, 0, 0, 0, TimeSpan.Zero), forecast.Run);
        Assert.Null(WpcBulletinTimes.TryRead("not a bulletin", AnalysisTime));
    }

    [Fact]
    public void A_heading_early_in_a_month_read_late_in_the_last_is_dated_in_its_own_month()
    {
        // Issued 31 October 21Z, read just after midnight on 1 November.
        var times = WpcBulletinTimes.TryRead("ASUS02 KWBC 312100\nCODSUS\n", new DateTimeOffset(2026, 11, 1, 0, 30, 0, TimeSpan.Zero))!;

        Assert.Equal(new DateTimeOffset(2026, 10, 31, 21, 0, 0, TimeSpan.Zero), times.Issued);
    }

    [Fact]
    public async Task Each_chosen_bulletin_is_one_item_over_WPCs_domain_stale_after_6_hours()
    {
        var server = Server();
        var index = await new WeatherFeedIndexer(new HttpClient(server), timeProvider: new FakeTimeProvider(AnalysisTime.AddHours(2)))
            .IndexAsync(Feed("wpc-analysis", "wpc-forecast"), null, TestContext.Current.CancellationToken);

        Assert.Empty(index.Diagnostics);
        Assert.Equal(["wpc-analysis", "wpc-forecast"], index.Items.Select(i => i.Key));
        var analysis = index.Items[0];
        Assert.Equal("CODSUS", analysis.ProductSpec);
        Assert.Equal("Surface analysis · WPC", analysis.Title);
        Assert.Equal(WeatherFeedIndexer.WpcDomain, analysis.Bounds);
        Assert.Equal(AnalysisTime, S100ForecastFeedIndexer.RunOf(analysis));
        Assert.Equal("analysis", analysis.Properties[WeatherFeedIndexer.BulletinKindProperty]);
        Assert.Equal("North America and adjacent waters", WeatherRuns.CoverageOf(analysis));
        Assert.Equal("NOAA/NWS Weather Prediction Center · Public domain", WeatherRuns.CreditOf(analysis));
        Assert.True(WeatherRuns.IsWeatherRun(analysis));
        Assert.Equal(TimeSpan.FromHours(6), WeatherRuns.StaleAfterOf(analysis));
        Assert.False(WeatherRuns.IsStale(analysis, null, AnalysisTime.AddHours(5)));
        Assert.True(WeatherRuns.IsStale(analysis, null, AnalysisTime.AddHours(7)));
        Assert.Equal(new DateTimeOffset(2026, 11, 5, 0, 0, 0, TimeSpan.Zero), S100ForecastFeedIndexer.RunOf(index.Items[1]));
        Assert.Equal("forecast", index.Items[1].Properties[WeatherFeedIndexer.BulletinKindProperty]);
        // Forecast fronts come about every 12 hours, so they are stale after 13.
        Assert.Equal(TimeSpan.FromHours(13), WeatherRuns.StaleAfterOf(index.Items[1]));
        Assert.Equal(new DateTimeOffset(2026, 11, 5, 0, 0, 0, TimeSpan.Zero), index.PublishedAt);

        var cell = await new EncCellDownloader(new HttpClient(server), Path.Combine(_temp.Path, "wpc"))
            .DownloadAsync(analysis, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("wpc-analysis.txt", cell.Location.RelativePath);
        Assert.StartsWith("ASUS02 KWBC 040000", File.ReadAllText(Path.Combine(cell.Location.RootPath, cell.Location.RelativePath)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_bulletin_that_cant_be_read_is_an_error_and_the_others_still_list()
    {
        var server = Server();
        server.Remove(ForecastUri);

        var index = await new WeatherFeedIndexer(new HttpClient(server), timeProvider: new FakeTimeProvider(AnalysisTime.AddHours(2)))
            .IndexAsync(Feed("wpc-analysis", "wpc-forecast"), null, TestContext.Current.CancellationToken);

        Assert.Equal("wpc-analysis", Assert.Single(index.Items).Key);
        var error = Assert.Single(index.Diagnostics);
        Assert.Equal(IndexDiagnosticSeverity.Error, error.Severity);
        Assert.StartsWith("Forecast fronts · WPC:", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_scope_offers_the_bulletins_over_WPCs_domain_and_is_a_FEED()
    {
        var server = Server();
        var indexer = new WeatherFeedIndexer(new HttpClient(server), timeProvider: new FakeTimeProvider(AnalysisTime.AddHours(2)));
        var scope = new WeatherScope(Known, (feed, ct) => indexer.DescribeAsync(feed, ct), () => new GeoBounds(30, -80, 40, -70));

        Assert.Null(await scope.LoadAsync(TestContext.Current.CancellationToken));

        var group = Assert.Single(scope.Groups);
        Assert.Equal(["wpc-analysis"], group.Options.Where(o => o.IsSelected).Select(o => o.Value));
        Assert.Null(scope.Area);
        Assert.Equal(AnalysisTime, scope.LatestRun);
        Assert.Equal("Surface analysis · WPC · North America and adjacent waters", scope.SelectionSummary);
        Assert.Equal("FEED", LibraryNodeText.KindOf(scope.Build(Guid.NewGuid(), null)));
    }

    /// <summary>Serves bulletins by address, with their Last-Modified time; 404 for anything else.</summary>
    private sealed class BulletinServer : HttpMessageHandler
    {
        private readonly Dictionary<string, (string Text, DateTimeOffset Modified)> _files = new(StringComparer.Ordinal);

        public (string Text, DateTimeOffset Modified) this[string uri]
        {
            set => _files[uri] = value;
        }

        public void Remove(string uri) => _files.Remove(uri);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!_files.TryGetValue(request.RequestUri!.AbsoluteUri, out var file))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            var content = new ByteArrayContent(Encoding.ASCII.GetBytes(file.Text));
            content.Headers.LastModified = file.Modified;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
}
