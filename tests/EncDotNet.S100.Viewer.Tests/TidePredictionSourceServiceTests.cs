using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Tides;
using EncDotNet.S100.Tides.Stations;
using EncDotNet.S100.Viewer.Services.Depth;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The tide-prediction switch (#1451, design D1): it hands the cached station
/// list to the shared source cache while it's on, and takes it back when off.
/// </summary>
public sealed class TidePredictionSourceServiceTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("s100-tides-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public async Task The_switch_hands_the_station_list_to_the_sources_and_takes_it_back()
    {
        var path = Path.Combine(_folder, "tide-stations.json.gz");
        await TideStationCatalogueFile.SaveAsync(path, Catalogue(), TestContext.Current.CancellationToken);
        var settings = new Settings();
        var cache = new RouteCheckSourceCache(new ProjNetCrsTransformFactory());
        var changes = 0;
        cache.Changed += (_, _) => changes++;
        using var service = new TidePredictionSourceService(settings, cache, TideTestLists.At(path), action => action());

        Assert.Equal(TidePredictionSourceState.Off, service.State);
        Assert.Null(cache.TidePredictions);

        settings.Set(true);
        await service.Loading;
        Assert.Equal(TidePredictionSourceState.InUse, service.State);
        Assert.Equal("H1", Assert.Single(cache.TidePredictions!.Stations).Id);
        Assert.Equal(1, changes);

        settings.Set(false);
        Assert.Equal(TidePredictionSourceState.Off, service.State);
        Assert.Null(cache.TidePredictions);
        Assert.Equal(2, changes);
    }

    [Fact]
    public async Task Without_a_station_list_it_says_so()
    {
        var settings = new Settings();
        settings.Set(true);
        var cache = new RouteCheckSourceCache(new ProjNetCrsTransformFactory());
        using var service = new TidePredictionSourceService(settings, cache, TideTestLists.At(Path.Combine(_folder, "missing.json.gz")), action => action());

        await service.Loading;

        Assert.Equal(TidePredictionSourceState.NoStationList, service.State);
        Assert.Null(cache.TidePredictions);
    }

    private static TideStationCatalogue Catalogue() => new(
        [
            new TideStationEntry
            {
                Id = "H1",
                Name = "Harbour",
                Position = new GeoPosition(0.02, 0.06),
                Kind = TideStationKind.Harmonic,
                Constituents = [new TideConstituent("M2", 1.0, 0)],
                Datums = new StationDatums(new Dictionary<string, double> { ["MSL"] = 2.0, ["MLLW"] = 0.5 }, null, null),
            },
        ],
        "Test",
        null);

    private sealed class Settings : IWaterLevelAdjustmentSettings
    {
        public WaterLevelAdjustmentMode WaterLevelAdjustment => WaterLevelAdjustmentMode.Off;

        public bool WaterLevelApplyUndeclaredDatum => false;

        public bool UseTidePredictions { get; private set; }

        public event Action? WaterLevelAdjustmentChanged;

        public void Set(bool on)
        {
            UseTidePredictions = on;
            WaterLevelAdjustmentChanged?.Invoke();
        }
    }
}
