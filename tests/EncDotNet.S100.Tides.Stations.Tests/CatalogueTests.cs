using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Tides.Stations.Tests;

public sealed class CatalogueTests
{
    private static readonly Lazy<TideStationCatalogue> PugetSound = new(Fixtures.PugetSoundCatalogue);
    private static readonly DateTime Start = new(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Download_ReadsReferencesAndSubordinates()
    {
        var catalogue = PugetSound.Value;
        Assert.Equal("NOAA CO-OPS", catalogue.Provider);
        Assert.Equal("1983-2001", catalogue.DatumEpoch);
        Assert.Equal(5, catalogue.Stations.Count);

        var seattle = catalogue.Find(Fixtures.Seattle)!;
        Assert.Equal(TideStationKind.Harmonic, seattle.Kind);
        Assert.Equal("SEATTLE (Madison St.), Elliott Bay", seattle.Name);
        Assert.Equal("WA", seattle.Region);
        Assert.Equal(TimeSpan.FromHours(-8), seattle.UtcOffset);
        Assert.Contains(seattle.Constituents, c => c is { Name: "M2", AmplitudeMeters: 1.063, PhaseDegrees: 10.8 });
        Assert.DoesNotContain(seattle.Constituents, c => c.AmplitudeMeters == 0);
        Assert.Equal(new DateOnly(2003, 4, 17), seattle.Datums!.Accepted);
        Assert.Equal(2.419, seattle.Datums.Heights["MLLW"]);
        Assert.Equal(1.1089039, seattle.Datums.Heights["LAT"]);

        var tacoma = catalogue.Find(Fixtures.TacomaNarrows)!;
        Assert.Equal(TideStationKind.Subordinate, tacoma.Kind);
        Assert.Equal(Fixtures.Seattle, tacoma.ReferenceStationId);
        Assert.Equal(
            new SubordinateOffsets(TimeSpan.FromMinutes(28), TimeSpan.FromMinutes(23), 1.11, 1.02, SubordinateHeightAdjustment.Ratio),
            tacoma.Offsets);
        Assert.Empty(tacoma.Constituents);

        var marrowstone = catalogue.Find(Fixtures.MarrowstonePoint)!;
        Assert.Equal(SubordinateHeightAdjustment.Offset, marrowstone.Offsets!.HeightAdjustment);
        Assert.Equal(0.12, marrowstone.Offsets.HighWaterHeight);
    }

    [Fact]
    public void Harmonic_OnMllw_IsMslMinusMllwAboveTheConstituents()
    {
        var catalogue = PugetSound.Value;
        var onMllw = catalogue.TryCreatePrediction(Fixtures.Seattle, VerticalDatum.MeanLowerLowWater)!;
        var onMsl = catalogue.TryCreatePrediction(Fixtures.Seattle, VerticalDatum.MeanSeaLevel)!;
        var onLat = catalogue.TryCreatePrediction(Fixtures.Seattle, VerticalDatum.LowestAstronomicalTide)!;

        Assert.Equal("MLLW", onMllw.Predictor.DatumName);
        Assert.Equal(4.443 - 2.419, onMllw.Predictor.HeightAt(Start) - onMsl.Predictor.HeightAt(Start), 9);
        Assert.Equal(4.443 - 1.1089039, onLat.Predictor.HeightAt(Start) - onMsl.Predictor.HeightAt(Start), 9);
        Assert.Equal(VerticalDatum.LowestAstronomicalTide, onLat.Source.VerticalDatum);
    }

    [Fact]
    public void Subordinate_IsPredictedOnlyOnItsOffsetsDatum_WithItsReferenceAsProvenance()
    {
        var catalogue = PugetSound.Value;
        Assert.Null(catalogue.TryCreatePrediction(Fixtures.TacomaNarrows, VerticalDatum.MeanSeaLevel));
        Assert.Equal([VerticalDatum.MeanLowerLowWater], catalogue.GetAvailableDatums(Fixtures.TacomaNarrows));

        var prediction = catalogue.TryCreatePrediction(Fixtures.TacomaNarrows, VerticalDatum.MeanLowerLowWater)!;
        var source = prediction.Source;
        Assert.IsType<SubordinateTidePredictor>(prediction.Predictor);
        Assert.Equal(TideStationKind.Subordinate, source.Kind);
        Assert.Equal("Tacoma Narrows Bridge", source.StationName);
        Assert.Equal(Fixtures.Seattle, source.ReferenceStationId);
        Assert.Equal("from SEATTLE (Madison St.), Elliott Bay 9447130, with offsets", source.ReferenceDescription);
        Assert.Equal("MLLW", source.DatumName);
        Assert.Equal(catalogue.Retrieved, source.CatalogueDate);

        var harmonic = catalogue.TryCreatePrediction(Fixtures.Seattle, VerticalDatum.MeanLowerLowWater)!.Source;
        Assert.Null(harmonic.ReferenceDescription);
        Assert.Equal(
            [
                VerticalDatum.MeanHigherHighWater, VerticalDatum.MeanHighWater, VerticalDatum.MeanSeaLevel,
                VerticalDatum.MeanLowWater, VerticalDatum.MeanLowerLowWater, VerticalDatum.LowestAstronomicalTide,
                VerticalDatum.HighestAstronomicalTide,
            ],
            catalogue.GetAvailableDatums(Fixtures.Seattle));
    }

    [Fact]
    public void Prediction_IsNull_ForAnUnknownStationOrDatum()
    {
        var catalogue = PugetSound.Value;
        Assert.Null(catalogue.TryCreatePrediction("0000000", VerticalDatum.MeanLowerLowWater));
        Assert.Null(catalogue.TryCreatePrediction(Fixtures.Seattle, VerticalDatum.MeanLowWaterSprings));
        Assert.Empty(catalogue.GetAvailableDatums("0000000"));
    }

    [Fact]
    public void FindNearest_RanksByGeodesicDistance()
    {
        var catalogue = PugetSound.Value;
        var narrows = new GeoPosition(47.27, -122.55);
        var matches = catalogue.FindNearest(narrows, 3);
        Assert.Equal([Fixtures.TacomaNarrows, Fixtures.Seattle, Fixtures.PortLudlow], matches.Select(m => m.Station.Id));
        Assert.InRange(matches[0].DistanceMeters, 0, 300);
        Assert.True(matches[1].DistanceMeters < matches[2].DistanceMeters);

        Assert.Equal([Fixtures.TacomaNarrows], catalogue.FindNearest(narrows, 10, 10_000).Select(m => m.Station.Id));
        Assert.Empty(catalogue.FindNearest(narrows, 0));
    }

    [Fact]
    public void FindWithin_KeepsTheBox_AndRanksFromItsCentre()
    {
        var catalogue = PugetSound.Value;
        // Admiralty Inlet: Port Townsend, Marrowstone Point and Port Ludlow, not Seattle or Tacoma.
        var box = new BoundingBox(47.85, -122.85, 48.15, -122.6);
        var matches = catalogue.FindWithin(box, new GeoPosition(48.11, -122.76));
        Assert.Equal([Fixtures.PortTownsend, Fixtures.MarrowstonePoint, Fixtures.PortLudlow], matches.Select(m => m.Station.Id));
    }

    [Fact]
    public void FindWithin_HandlesABoxAcrossTheAntimeridian()
    {
        // The full NOAA list has stations in the western Aleutians, east of 180°.
        var stations = Fixtures.StationList()["stations"]!.AsArray()
            .Select(s => new TideStationEntry
            {
                Id = s!["id"]!.GetValue<string>(),
                Name = s["name"]!.GetValue<string>(),
                Position = new GeoPosition(s["lat"]!.GetValue<double>(), s["lng"]!.GetValue<double>()),
                Kind = TideStationKind.Subordinate,
                ReferenceStationId = "x",
                Offsets = new SubordinateOffsets(TimeSpan.Zero, TimeSpan.Zero, 1, 1, SubordinateHeightAdjustment.Ratio),
            })
            .ToList();
        var catalogue = new TideStationCatalogue(stations, null, null);
        var aleutians = catalogue.FindWithin(new BoundingBox(50, 170, 56, -170));
        Assert.NotEmpty(aleutians);
        Assert.Contains(aleutians, m => m.Station.Position.Longitude > 170);
        Assert.Contains(aleutians, m => m.Station.Position.Longitude < -170);
        Assert.All(aleutians, m => Assert.True(m.Station.Position.Longitude is >= 170 or <= -170));
        Assert.Equal(aleutians.OrderBy(m => m.DistanceMeters).Select(m => m.Station.Id), aleutians.Select(m => m.Station.Id));
    }

    [Fact]
    public void IsOlderThan_TellsTheOfflineFallback()
    {
        var retrieved = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var catalogue = new TideStationCatalogue([], null, retrieved);
        Assert.False(catalogue.IsOlderThan(TideStationCatalogue.OfflineStaleAfter, retrieved.AddDays(300)));
        Assert.True(catalogue.IsOlderThan(TideStationCatalogue.OfflineStaleAfter, retrieved.AddDays(400)));
        Assert.True(new TideStationCatalogue([], null, null).IsOlderThan(TimeSpan.MaxValue, retrieved));
    }

    [Fact]
    public void Constructor_RejectsRepeatsAndIncompleteSubordinates()
    {
        var seattle = PugetSound.Value.Find(Fixtures.Seattle)!;
        Assert.Throws<ArgumentException>(() => new TideStationCatalogue([seattle, seattle], null, null));
        Assert.Throws<ArgumentException>(() => new TideStationCatalogue(
            [new TideStationEntry { Id = "1", Name = "x", Position = default, Kind = TideStationKind.Subordinate }], null, null));
    }
}
