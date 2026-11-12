using System.Text;
using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Tides.Stations.Tests;

public sealed class FileFormatTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string ExamplePath =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "Tides", "Stations", "example-stations.json");

    [Theory]
    [InlineData("catalogue.json")]
    [InlineData("catalogue.json.gz")]
    public async Task Save_ThenLoad_RoundTripsTheCatalogue(string fileName)
    {
        var catalogue = Fixtures.PugetSoundCatalogue();
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(directory.FullName, "cache", fileName);
            await TideStationCatalogueFile.SaveAsync(path, catalogue, Token);
            Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!));
            var loaded = await TideStationCatalogueFile.LoadAsync(path, Token);

            Assert.Equal(catalogue.Provider, loaded.Provider);
            Assert.Equal(catalogue.Retrieved, loaded.Retrieved);
            Assert.Equal(catalogue.DatumEpoch, loaded.DatumEpoch);
            Assert.Equal(catalogue.Stations.Select(s => s.Id), loaded.Stations.Select(s => s.Id));
            foreach (var station in catalogue.Stations)
            {
                var copy = loaded.Find(station.Id)!;
                Assert.Equal(station.Name, copy.Name);
                Assert.Equal(station.Position, copy.Position);
                Assert.Equal(station.Kind, copy.Kind);
                Assert.Equal(station.Region, copy.Region);
                Assert.Equal(station.UtcOffset, copy.UtcOffset);
                Assert.Equal(station.Constituents, copy.Constituents);
                Assert.Equal(station.ReferenceStationId, copy.ReferenceStationId);
                Assert.Equal(station.Offsets, copy.Offsets);
                Assert.Equal(station.Datums?.Epoch, copy.Datums?.Epoch);
                Assert.Equal(station.Datums?.Accepted, copy.Datums?.Accepted);
                Assert.Equal(station.Datums?.Heights.OrderBy(h => h.Key), copy.Datums?.Heights.OrderBy(h => h.Key));
            }

            var time = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
            Assert.Equal(
                catalogue.TryCreatePrediction(Fixtures.TacomaNarrows, VerticalDatum.MeanLowerLowWater)!.Predictor.HeightAt(time),
                loaded.TryCreatePrediction(Fixtures.TacomaNarrows, VerticalDatum.MeanLowerLowWater)!.Predictor.HeightAt(time));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Example_BringYourOwn_ConvertsLocalPhasesAndPredictsBothStations()
    {
        var catalogue = await TideStationCatalogueFile.LoadAsync(ExamplePath, Token);
        Assert.Equal("Example harbour authority", catalogue.Provider);
        Assert.Null(catalogue.Retrieved);

        // Seattle's phase_local, referred to PST, gives its phase_GMT.
        var m2 = catalogue.Find("example-harbour")!.Constituents.Single(c => c.Name == "M2");
        Assert.Equal(10.8, m2.PhaseDegrees, 1);

        var cove = catalogue.TryCreatePrediction("example-cove", VerticalDatum.MeanLowerLowWater);
        Assert.NotNull(cove);
        Assert.Equal("from Example Harbour example-harbour, with offsets", cove.Source.ReferenceDescription);
        Assert.NotNull(catalogue.TryCreatePrediction("example-harbour", VerticalDatum.MeanHigherHighWater));
        Assert.Null(catalogue.TryCreatePrediction("example-harbour", VerticalDatum.LowestAstronomicalTide));
    }

    [Theory]
    [InlineData("""{ "format": "other", "version": 1, "stations": [] }""", "format")]
    [InlineData("""{ "format": "tide-stations", "version": 2, "stations": [] }""", "version")]
    [InlineData("""{ "format": "tide-stations", "version": 1, "stations": [ { "id": "a", "latitude": 1, "longitude": 2, "kind": "x" } ] }""", "kind")]
    [InlineData("""{ "format": "tide-stations", "version": 1, "stations": [ { "id": "a", "latitude": 1, "kind": "harmonic" } ] }""", "latitude")]
    [InlineData("""{ "format": "tide-stations", "version": 1, "stations": [ { "id": "a", "latitude": 1, "longitude": 2, "kind": "harmonic", "constituents": [ { "name": "ZZ9", "amplitude": 1, "phase": 0 } ] } ] }""", "ZZ9")]
    [InlineData("""{ "format": "tide-stations", "version": 1, "stations": [ { "id": "a", "latitude": 1, "longitude": 2, "kind": "harmonic", "phaseReference": "local" } ] }""", "utcOffsetHours")]
    [InlineData("""{ "format": "tide-stations", "version": 1, "stations": [ { "id": "a", "latitude": 1, "longitude": 2, "kind": "subordinate" } ] }""", "reference")]
    [InlineData("""{ "format": "tide-stations", "version": 1, "stations": [ { "id": "a", "latitude": 1, "longitude": 2, "kind": "harmonic", "datums": { "accepted": "Apr 17 2003" } } ] }""", "accepted")]
    [InlineData("""{ "format": "tide-stations", "version": 1, "stations": [ { "id": "a" """, "valid")]
    public async Task Read_RejectsAnInvalidFile_SayingWhy(string json, string reason)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var error = await Assert.ThrowsAsync<FormatException>(() => TideStationCatalogueFile.ReadAsync(stream, Token));
        Assert.Contains(reason, error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
