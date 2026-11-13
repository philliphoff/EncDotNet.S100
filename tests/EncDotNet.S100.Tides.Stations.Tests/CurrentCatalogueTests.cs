using System.Globalization;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Tides.Stations.Noaa;

namespace EncDotNet.S100.Tides.Stations.Tests;

public sealed class CurrentCatalogueTests
{
    private static readonly DateTime YearStart = new(Fixtures.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Download_ReadsCurrentStations_OneHarconRequestPerHarmonicStation()
    {
        var handler = new FixtureHandler();
        var reports = new List<NoaaDownloadProgress>();
        var catalogue = await Fixtures.Client(handler, Fixtures.CurrentOptions()).DownloadCatalogueAsync(new SyncProgress(reports.Add), Token);

        Assert.Equal(5, catalogue.Stations.Count);
        Assert.Equal(Fixtures.CurrentStations.Order(), catalogue.CurrentStations.Select(s => s.Id).Order());

        // Tide list, two requests per tide reference, current list, one per harmonic current station.
        Assert.Equal(1 + (2 * 2) + 1 + 4, handler.Requests);
        Assert.Equal(new NoaaDownloadProgress(6, 6), reports.MaxBy(r => r.CompletedStations));
    }

    [Fact]
    public void HarmonicStation_CarriesItsBinsConstituentsAndDirections()
    {
        var station = Fixtures.CurrentCatalogue().FindCurrentStation("PUG1701_33");
        Assert.NotNull(station);
        Assert.Equal(TideStationKind.Harmonic, station.Kind);
        Assert.Equal("Deception Pass (Narrows)", station.Name);
        Assert.Equal(108, station.FloodDirectionDegrees);
        Assert.Equal(277, station.EbbDirectionDegrees);
        Assert.Equal(5.49, station.DepthMeters);
        Assert.Equal(101.5, station.MajorAxisDirectionDegrees);
        Assert.Equal(-0.3179, station.MajorMeanVelocity, 6);
        Assert.False(station.IsRotary);
        var m2 = Assert.Single(station.Constituents, c => c.Name == "M2");
        Assert.Equal(2.6802, m2.MajorAmplitudeMetersPerSecond, 6);
        Assert.Equal(241.2, m2.MajorPhaseDegrees);
    }

    [Fact]
    public void SubordinateStation_RefersToItsReferenceStationAndBin()
    {
        var station = Fixtures.CurrentCatalogue().FindCurrentStation("PCT0281_1");
        Assert.NotNull(station);
        Assert.Equal(TideStationKind.Subordinate, station.Kind);
        Assert.Equal("SFB1202_17", station.ReferenceStationId);
        Assert.Equal(
            new SubordinateCurrentOffsets(
                TimeSpan.FromMinutes(42), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(27), TimeSpan.FromMinutes(43), 0.9, 1.4),
            station.Offsets);
        Assert.Equal(70, station.FloodDirectionDegrees);
        Assert.Equal(256, station.EbbDirectionDegrees);
    }

    [Theory]
    [InlineData("PUG1701_33")]
    [InlineData("SFB1202_17")]
    [InlineData("PCT0281_1")]
    [InlineData("ACT2401_1")]
    public void Predictions_MatchNoaasJanuarySlacksAndMaxima(string id)
    {
        var prediction = Fixtures.CurrentCatalogue().TryCreateCurrentPrediction(id);
        Assert.NotNull(prediction);
        var expected = NoaaEvents(id).Where(e => e.Time.Month == 1).ToList();
        var actual = prediction.Predictor.FindEvents(YearStart, YearStart.AddMonths(1));

        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].IsSlack, actual[i].IsSlack);
            Assert.True(Math.Abs((expected[i].Time - actual[i].Time).TotalMinutes) <= 1, $"{expected[i].Time:u} vs {actual[i].Time:u}");
            if (!expected[i].IsSlack)
            {
                Assert.Equal(expected[i].VelocityMetersPerSecond, actual[i].VelocityMetersPerSecond, 0.003);
            }
        }
    }

    [Fact]
    public void PredictionSource_NamesTheReferenceStationAndDepth()
    {
        var catalogue = Fixtures.CurrentCatalogue();
        var subordinate = catalogue.TryCreateCurrentPrediction("PCT0281_1")!.Source;
        Assert.Equal(TideStationKind.Subordinate, subordinate.Kind);
        Assert.Equal("from Golden Gate Bridge, 0.88 nm NE of SFB1202_17, with offsets", subordinate.ReferenceDescription);
        Assert.Equal(NoaaTideStationClient.ProviderName, subordinate.Provider);
        Assert.NotNull(subordinate.CatalogueDate);

        var harmonic = catalogue.TryCreateCurrentPrediction("SFB1202_17")!.Source;
        Assert.Equal(6.4, harmonic.DepthMeters);
        Assert.Null(harmonic.ReferenceDescription);
        Assert.False(harmonic.IsRotary);
    }

    [Fact]
    public void TryCreateCurrentPrediction_IsNull_ForAnUnknownStationOrMissingReference()
    {
        var catalogue = Fixtures.CurrentCatalogue();
        Assert.Null(catalogue.TryCreateCurrentPrediction("XXX0000_1"));

        var orphan = new TideStationCatalogue(
            [],
            [catalogue.FindCurrentStation("PCT0281_1")!],
            null,
            null);
        Assert.Null(orphan.TryCreateCurrentPrediction("PCT0281_1"));
    }

    [Fact]
    public void FindNearestCurrentStations_RanksByDistance()
    {
        var catalogue = Fixtures.CurrentCatalogue();
        var goldenGate = new GeoPosition(37.82, -122.465);
        var nearest = catalogue.FindNearestCurrentStations(goldenGate, 5, 10_000);
        Assert.Equal(["PCT0281_1", "SFB1202_17"], nearest.Select(m => m.Station.Id));
        Assert.True(nearest[0].DistanceMeters < nearest[1].DistanceMeters);

        var box = new BoundingBox(47.5, -123.0, 48.5, -122.0);
        Assert.Equal("PUG1701_33", Assert.Single(catalogue.FindCurrentStationsWithin(box)).Station.Id);
    }

    [Fact]
    public async Task Download_ReadsEverySubordinateInNoaasFullList_AndLeavesOutWeakStations()
    {
        var handler = new FixtureHandler
        {
            CurrentStationList = Fixtures.CurrentStationList(),
            EmptyCurrentHarmonics = true,
        };
        var catalogue = await Fixtures.Client(handler, Fixtures.CurrentOptions()).DownloadCatalogueAsync(cancellationToken: Token);

        var listed = Fixtures.CurrentStationList()["stations"]!.AsArray();
        var subordinates = listed.Count(s => s!["type"]!.GetValue<string>() == "S");
        var weak = listed.Where(s => s!["type"]!.GetValue<string>() == "W").Select(s => $"{s!["id"]}_{s["currbin"]}").ToHashSet();

        // The recorded list: about 1,900 subordinate stations; every one has
        // its offsets and directions. Harmonic stations without a recorded
        // harcon.json have no constituents and are left out.
        Assert.InRange(subordinates, 1500, 2500);
        Assert.Equal(subordinates, catalogue.CurrentStations.Count(s => s.Kind == TideStationKind.Subordinate));
        Assert.Equal(4, catalogue.CurrentStations.Count(s => s.Kind == TideStationKind.Harmonic));
        Assert.DoesNotContain(catalogue.CurrentStations, s => weak.Contains(s.Id));
        Assert.All(catalogue.CurrentStations, s => Assert.Matches("^[A-Za-z0-9]+_[0-9]+$", s.Id));
    }

    [Fact]
    public async Task SaveAndLoad_RoundTripsCurrentStations()
    {
        var catalogue = Fixtures.CurrentCatalogue();
        var path = Path.Combine(Path.GetTempPath(), $"current-stations-{Guid.NewGuid():N}.json.gz");
        try
        {
            await TideStationCatalogueFile.SaveAsync(path, catalogue, Token);
            var loaded = await TideStationCatalogueFile.LoadAsync(path, Token);

            Assert.Equal(catalogue.CurrentStations.Count, loaded.CurrentStations.Count);
            foreach (var id in Fixtures.CurrentStations)
            {
                var before = catalogue.TryCreateCurrentPrediction(id)!.Predictor;
                var after = loaded.TryCreateCurrentPrediction(id)!.Predictor;
                var time = YearStart.AddDays(40).AddHours(5);
                Assert.Equal(before.CurrentAt(time), after.CurrentAt(time));
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task CheckForUpdates_ReadsTheCurrentList_AndFindsItsChanges()
    {
        var handler = new FixtureHandler();
        var client = Fixtures.Client(handler, Fixtures.CurrentOptions());
        var catalogue = await client.DownloadCatalogueAsync(cancellationToken: Token);
        var requests = handler.Requests;

        var unchanged = await client.CheckForUpdatesAsync(catalogue, Token);
        Assert.False(unchanged.HasNewerData);

        // The tide list, one sentinel's datums and the current list.
        Assert.Equal(3, handler.Requests - requests);

        var list = Fixtures.CurrentStationList([.. Fixtures.CurrentStations.Where(id => id != "ACT2401_1")]);
        var golden = list["stations"]!.AsArray().Single(s => s!["id"]!.GetValue<string>() == "PCT0281")!;
        golden["currentpredictionoffsets"]!["mfcAmpAdj"] = 1.0;
        handler.CurrentStationList = list;

        var check = await client.CheckForUpdatesAsync(catalogue, Token);
        Assert.True(check.HasNewerData);
        Assert.Equal(["ACT2401_1"], check.RemovedStationIds);
        Assert.Equal(["PCT0281_1"], check.ChangedStationIds);
    }

    [Fact]
    public void Constructor_RejectsARepeatedCurrentStationId()
    {
        var station = Fixtures.CurrentCatalogue().FindCurrentStation("PUG1701_33")!;
        Assert.Throws<ArgumentException>(() => new TideStationCatalogue([], [station, station], null, null));
    }

    /// <summary>NOAA's slacks (any kind) and turns for the year, from max-slack.csv.</summary>
    private static IEnumerable<CurrentEvent> NoaaEvents(string id)
    {
        foreach (var line in File.ReadLines(Fixtures.Path("Currents", id, "max-slack.csv")).Skip(1))
        {
            var fields = line.Split(',');
            var time = DateTime.SpecifyKind(
                DateTime.ParseExact(fields[0], "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                DateTimeKind.Utc);
            var velocity = double.Parse(fields[3], CultureInfo.InvariantCulture) / 100;
            var kind = fields[2].Trim() switch
            {
                "flood" => CurrentEventKind.MaximumFlood,
                "ebb" => CurrentEventKind.MaximumEbb,
                _ => CurrentEventKind.SlackBeforeFlood,
            };
            yield return new CurrentEvent(time, velocity, kind);
        }
    }

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
