using System.Net;
using EncDotNet.S100.Tides.Stations.Noaa;

namespace EncDotNet.S100.Tides.Stations.Tests;

public sealed class NoaaClientTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GetStations_ReadsTheWholeList_FromAGzippedResponse()
    {
        var handler = new FixtureHandler { StationList = Fixtures.StationList() };
        var stations = await Fixtures.Client(handler).GetStationsAsync(Token);

        // The recorded list: about 1,260 reference and 2,240 subordinate stations.
        Assert.InRange(stations.Count, 3000, 4000);
        Assert.InRange(stations.Count(s => s.Kind == TideStationKind.Harmonic), 1000, 1500);
        Assert.All(
            stations.Where(s => s.Kind == TideStationKind.Subordinate),
            s => Assert.True(s.ReferenceStationId is not null && s.Offsets is not null, s.Id));
        Assert.Single(handler.Uris);
    }

    [Fact]
    public async Task GetConstituents_UsesGreenwichPhases_AndDropsZeroAmplitudes()
    {
        var constituents = await Fixtures.Client(new FixtureHandler()).GetConstituentsAsync(Fixtures.Seattle, Token);
        var m2 = Assert.Single(constituents, c => c.Name == "M2");
        Assert.Equal(1.063, m2.AmplitudeMeters);
        Assert.Equal(10.8, m2.PhaseDegrees);
        Assert.All(constituents, c => Assert.NotEqual(0, c.AmplitudeMeters));
    }

    [Fact]
    public async Task GetDatums_ReadsTheChartDatumsEpochAndRevisionDate()
    {
        var datums = await Fixtures.Client(new FixtureHandler()).GetDatumsAsync(Fixtures.PortTownsend, Token);
        Assert.NotNull(datums);
        Assert.Equal("1983-2001", datums.Epoch);
        Assert.NotNull(datums.Accepted);
        foreach (var name in TideDatumNames.All)
        {
            Assert.True(datums.Heights.ContainsKey(name), name);
        }

        // Only the datums a prediction can be given on are kept.
        Assert.False(datums.Heights.ContainsKey("STND"));
        Assert.False(datums.Heights.ContainsKey("GT"));
    }

    [Fact]
    public async Task GetDatums_IsNull_WhenNoaaHasNone()
    {
        var handler = new FixtureHandler { EditDatums = (_, json) => json["datums"] = null };
        Assert.Null(await Fixtures.Client(handler).GetDatumsAsync(Fixtures.Seattle, Token));
    }

    [Fact]
    public async Task Requests_AreRetried_AfterARefusal()
    {
        var handler = new FixtureHandler();
        handler.Failures.Enqueue(HttpStatusCode.Forbidden);
        handler.Failures.Enqueue(HttpStatusCode.ServiceUnavailable);
        var constituents = await Fixtures.Client(handler).GetConstituentsAsync(Fixtures.Seattle, Token);
        Assert.NotEmpty(constituents);
        Assert.Equal(3, handler.Requests);
    }

    [Fact]
    public async Task Requests_Fail_WhenRetriesRunOut_OrOnNotFound()
    {
        var handler = new FixtureHandler();
        handler.Failures.Enqueue(HttpStatusCode.Forbidden);
        handler.Failures.Enqueue(HttpStatusCode.Forbidden);
        var client = Fixtures.Client(new FixtureHandler());
        var options = new NoaaTideStationClientOptions { MaxRetries = 1, RetryDelay = TimeSpan.Zero, RequestInterval = TimeSpan.Zero };
        var limited = new NoaaTideStationClient(new HttpClient(handler), options);
        await Assert.ThrowsAsync<HttpRequestException>(() => limited.GetConstituentsAsync(Fixtures.Seattle, Token));
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetConstituentsAsync("0000000", Token));
    }

    [Fact]
    public async Task Download_ReportsProgress_AndAsksTwicePerReferenceStation()
    {
        var handler = new FixtureHandler();
        var reports = new List<NoaaDownloadProgress>();
        var catalogue = await Fixtures.Client(handler).DownloadCatalogueAsync(new SyncProgress(reports.Add), Token);
        Assert.Equal(5, catalogue.Stations.Count);
        Assert.Equal(1 + (2 * 2), handler.Requests);
        Assert.Equal(new NoaaDownloadProgress(0, 2), reports[0]);
        Assert.Equal(new NoaaDownloadProgress(2, 2), reports.MaxBy(r => r.CompletedStations));
    }

    [Fact]
    public async Task CheckForUpdates_FindsNothing_WhenNoaaIsUnchanged()
    {
        var handler = new FixtureHandler();
        var client = Fixtures.Client(handler);
        var catalogue = await client.DownloadCatalogueAsync(cancellationToken: Token);
        var requests = handler.Requests;

        var check = await client.CheckForUpdatesAsync(catalogue, Token);
        Assert.False(check.HasNewerData);
        Assert.Null(check.NewDatumEpoch);

        // The list and one sentinel's datums, not the full download.
        Assert.Equal(2, handler.Requests - requests);
    }

    [Fact]
    public async Task CheckForUpdates_FindsANewEpoch_RevisedDatums_AndListChanges()
    {
        var handler = new FixtureHandler();
        var client = Fixtures.Client(handler);
        var catalogue = await client.DownloadCatalogueAsync(cancellationToken: Token);

        // NOAA moves to the 2002–2020 epoch, drops Port Ludlow, adds a
        // station and revises Tacoma Narrows' offsets.
        handler.EditDatums = (_, json) =>
        {
            json["epoch"] = "2002-2020";
            json["accepted"] = "Jan 15 2027";
        };
        var list = Fixtures.StationList([.. Fixtures.PugetSound.Where(id => id != Fixtures.PortLudlow), "9445639"]);
        var tacoma = list["stations"]!.AsArray().Single(s => s!["id"]!.GetValue<string>() == Fixtures.TacomaNarrows)!;
        tacoma["tidepredoffsets"]!["timeOffsetHighTide"] = 30;
        handler.StationList = list;

        var check = await client.CheckForUpdatesAsync(catalogue, Token);
        Assert.True(check.HasNewerData);
        Assert.Equal("2002-2020", check.NewDatumEpoch);
        Assert.Equal([Fixtures.Seattle], check.RevisedDatumStationIds);
        Assert.Equal(["9445639"], check.AddedStationIds);
        Assert.Equal([Fixtures.PortLudlow], check.RemovedStationIds);
        Assert.Equal([Fixtures.TacomaNarrows], check.ChangedStationIds);
    }

    [Fact]
    public void Options_AreValidated()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new NoaaTideStationClient(new HttpClient(), new() { MaxConcurrency = 0 }));
        Assert.Throws<ArgumentException>(() => new NoaaTideStationClient(new HttpClient(), new() { BaseAddress = new Uri("https://example.org/api") }));
    }

    /// <summary>Reports on the calling thread, unlike <see cref="Progress{T}"/>.</summary>
    private sealed class SyncProgress(Action<NoaaDownloadProgress> report) : IProgress<NoaaDownloadProgress>
    {
        public void Report(NoaaDownloadProgress value)
        {
            lock (this)
            {
                report(value);
            }
        }
    }
}
