using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Quantities;
using EncDotNet.S100.Tides;
using EncDotNet.S100.Tides.Stations;

namespace EncDotNet.S100.Mcp.Tools.Tests;

/// <summary>
/// Tide predictions as a water-level source (#1451): the catalogue-backed
/// source, S-104 first, the reach measured through the loaded charts' water,
/// the water mask, and a route check with no S-104.
/// </summary>
public class TidePredictionSourceTests
{
    private const int Mllw = 12;
    private const int Lat = 23;

    private static readonly DateTime S104Time = new(2024, 1, 1, 1, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// A harmonic station, Harbour (H1), at (0,02, 0,06) with an M2 tide of
    /// 1 m about MSL 1,5 m above MLLW; and a subordinate station, Inlet (S1),
    /// at (0,02, 0,10), predicted from Harbour with no offsets.
    /// </summary>
    private static TideStationCatalogue Catalogue(bool subordinate = true)
    {
        var stations = new List<TideStationEntry>
        {
            new()
            {
                Id = "H1",
                Name = "Harbour",
                Position = new GeoPosition(0.02, 0.06),
                Kind = TideStationKind.Harmonic,
                Constituents = [new TideConstituent("M2", 1.0, 0)],
                Datums = new StationDatums(new Dictionary<string, double> { ["MSL"] = 2.0, ["MLLW"] = 0.5 }, "1983-2001", null),
            },
        };
        if (subordinate)
        {
            stations.Add(new TideStationEntry
            {
                Id = "S1",
                Name = "Inlet",
                Position = new GeoPosition(0.02, 0.10),
                Kind = TideStationKind.Subordinate,
                ReferenceStationId = "H1",
                Offsets = new SubordinateOffsets(TimeSpan.Zero, TimeSpan.Zero, 1.0, 1.0, SubordinateHeightAdjustment.Ratio),
            });
        }

        return new TideStationCatalogue(stations, "Test", new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero));
    }

    private static RouteCheckSources Sources(TideStationCatalogue? catalogue, params LoadedDataset[] datasets) =>
        CatalogRouteCheckSources.FromDatasets(datasets, new ProjNetCrsTransformFactory(), new RouteCheckSourceOptions { TidePredictions = catalogue });

    // An S-101 cell over the test area, with land where given.
    private static LoadedDataset Chart(string id = "101CHART", int scale = 22000, params (double Lat, double Lon)[][] land)
    {
        var areas = new List<S101AreaSynth.AreaFeature> { S101AreaSynth.DataCoverage(scale, S101AreaSynth.Box(-0.2, -0.2, 0.4, 0.4)) };
        areas.AddRange(land.Select(ring => new S101AreaSynth.AreaFeature("LandArea", ring, new Dictionary<string, string>())));
        return LoadedDatasetFactory.S101(id, S101AreaSynth.Cell(areas), LoadedDatasetFactory.Box(-0.2, -0.2, 0.4, 0.4));
    }

    [Fact]
    public void Without_S104_the_nearest_station_is_predicted_on_its_chart_datum()
    {
        var catalogue = Catalogue();
        var series = Assert.Single(Sources(catalogue, Chart()).WaterLevel!.Locate(new GeoPosition(0.02, 0.07)));
        var expected = catalogue.TryCreatePrediction("H1", VerticalDatum.MeanLowerLowWater)!.Predictor.HeightAt(S104Time);

        Assert.True(series.Source.IsPredicted);
        Assert.Equal("H1", series.Source.StationId);
        Assert.Equal("Harbour", series.Source.StationName);
        Assert.Equal("Harbour H1", series.Source.StationLabel);
        Assert.Equal(Mllw, series.Source.VerticalDatum);
        Assert.Null(series.Source.ReferenceStationId);
        Assert.InRange(series.Source.DistanceMeters!.Value, 1_100, 1_125);
        Assert.Equal(expected, series.ValueAt(S104Time)!.Value.HeightMeters, 9);
        Assert.InRange(series.ValueAt(S104Time)!.Value.HeightMeters, 0.5 - 0.2, 2.5 + 0.2);
    }

    [Fact]
    public void A_subordinate_station_names_its_reference_station()
    {
        var series = Assert.Single(Sources(Catalogue(), Chart()).WaterLevel!.Locate(new GeoPosition(0.02, 0.105)));

        Assert.Equal("S1", series.Source.StationId);
        Assert.Equal("H1", series.Source.ReferenceStationId);
        Assert.Equal("Harbour", series.Source.ReferenceStationName);
    }

    [Fact]
    public void S104_wins_inside_its_coverage_and_time_and_the_prediction_outside_them()
    {
        // The synthetic grid covers 0–0,04° from 00:00 to 02:00 on 1 Jan 2024 at 1,5 m on LAT.
        var grid = LoadedDatasetFactory.S104("104G");
        var water = Sources(Catalogue(), Chart(), grid).WaterLevel!;

        var inside = water.Locate(new GeoPosition(0.01, 0.01));
        Assert.Equal(WaterLevelSourceKind.Grid, inside[0].Source.Kind);
        Assert.True(inside[^1].Source.IsPredicted);

        var during = WaterLevelSnapshot.Resolve(inside, S104Time, depthDatum: null);
        Assert.Equal(WaterLevelSourceKind.Grid, during.Source!.Kind);
        Assert.Equal(inside[0].ValueAt(S104Time)!.Value.HeightMeters, during.Sample!.Value.HeightMeters, 6);

        var later = WaterLevelSnapshot.Resolve(inside, S104Time.AddHours(5), depthDatum: null);
        Assert.Equal(WaterLevelStatus.Applied, later.Status);
        Assert.True(later.Source!.IsPredicted);

        var outside = Assert.Single(water.Locate(new GeoPosition(0.02, 0.07)));
        Assert.True(outside.Source.IsPredicted);

        Assert.Equal(["104G", WaterLevelSourceInfo.PredictionDatasetId], water.Datasets.Select(d => d.DatasetId));
    }

    [Fact]
    public void A_prediction_on_another_datum_than_the_depth_is_reported_not_applied()
    {
        var series = Sources(Catalogue(), Chart()).WaterLevel!.Locate(new GeoPosition(0.02, 0.07));

        var resolved = WaterLevelSnapshot.Resolve(series, S104Time, depthDatum: Lat);

        Assert.Equal(WaterLevelStatus.DatumMismatch, resolved.Status);
        Assert.True(resolved.Source!.IsPredicted);
        Assert.Equal(DatumAgreement.Different, resolved.Datum);
    }

    [Fact]
    public void Charted_land_blocks_a_station_and_uncharted_ground_does_not()
    {
        var catalogue = Catalogue(subordinate: false);
        var across = new GeoPosition(0.02, 0.12); // 6,7 km east of Harbour

        // Without a chart the reach is a straight line.
        var open = Assert.Single(Sources(catalogue).WaterLevel!.Locate(across));
        Assert.InRange(open.Source.DistanceMeters!.Value, 6_600, 6_750);

        // A ridge of land from south to north between them cuts the station off.
        var ridge = S101AreaSynth.Box(-0.2, 0.08, 0.4, 0.09);
        Assert.Empty(Sources(catalogue, Chart(land: [ridge])).WaterLevel!.Locate(across));

        // A headland that ends at 0,0° N: the way round its south end is longer than the straight line.
        var headland = S101AreaSynth.Box(0.0, 0.08, 0.4, 0.09);
        var round = Assert.Single(Sources(catalogue, Chart(land: [headland])).WaterLevel!.Locate(across));
        Assert.InRange(round.Source.DistanceMeters!.Value, 7_900, 8_700);
    }

    [Fact]
    public void S104_stations_follow_the_same_reach_by_water()
    {
        // StationSeries puts STN_001 at (47,0, −122,0); a ridge of land runs north–south 0,4 km east of it.
        var stations = LoadedDatasetFactory.S104Stations("104S", S104Synth.StationSeries());
        var ridge = S101AreaSynth.Box(46.8, -121.995, 47.2, -121.99);
        var chart = LoadedDatasetFactory.S101(
            "101RIDGE",
            S101AreaSynth.Cell(
            [
                S101AreaSynth.DataCoverage(22000, S101AreaSynth.Box(46.8, -122.3, 47.2, -121.7)),
                new S101AreaSynth.AreaFeature("LandArea", ridge, new Dictionary<string, string>()),
            ]),
            LoadedDatasetFactory.Box(46.8, -122.3, 47.2, -121.7));
        var east = new GeoPosition(47.0, -121.95);

        Assert.Single(Sources(null, stations).WaterLevel!.Locate(east));
        Assert.Empty(Sources(null, stations, chart).WaterLevel!.Locate(east));
    }

    [Fact]
    public void The_water_mask_takes_the_finest_cell_covering_a_point()
    {
        // The coarse cell charts land over 0–0,1°; the fine cell over 0–0,05° charts water there.
        var coarse = Chart("101COARSE", scale: 90000, land: [S101AreaSynth.Box(0, 0, 0.1, 0.1)]);
        var fine = LoadedDatasetFactory.S101(
            "101FINE",
            S101AreaSynth.Cell([S101AreaSynth.DataCoverage(12000, S101AreaSynth.Box(0, 0, 0.05, 0.05))]),
            LoadedDatasetFactory.Box(0, 0, 0.05, 0.05));
        var mask = CatalogRouteCheckSources.WaterMaskFromDatasets([coarse, fine]);

        Assert.Equal(WaterMaskClass.Water, mask.Classify(new GeoPosition(0.02, 0.02)));
        Assert.Equal(WaterMaskClass.Land, mask.Classify(new GeoPosition(0.08, 0.08)));
        Assert.Equal(WaterMaskClass.Water, mask.Classify(new GeoPosition(0.2, 0.2)));
        Assert.Equal(WaterMaskClass.Uncharted, mask.Classify(new GeoPosition(1, 1)));

        // A grid gives the same answers as one point at a time (off the
        // polygons' edges, where rounding may fall either way).
        var grid = new WaterMaskGrid(-0.045, -0.045, 0.01, 0.01, 21, 21);
        var classes = new WaterMaskClass[grid.Count];
        mask.Classify(grid, classes);
        for (var row = 0; row < grid.Rows; row++)
        {
            for (var col = 0; col < grid.Columns; col++)
            {
                Assert.Equal(mask.Classify(grid.PositionAt(row, col)), classes[(row * grid.Columns) + col]);
            }
        }
    }

    [Fact]
    public void The_footprint_lists_the_stations_that_reach_the_loaded_charts()
    {
        var water = Sources(Catalogue(), Chart()).WaterLevel!;

        Assert.Equal(2, water.Footprint.Count);
        Assert.Contains(water.Footprint, a => a.Contains(0.02, 0.06));
        Assert.Empty(Sources(Catalogue()).WaterLevel!.Footprint);
    }

    [Fact]
    public void A_route_check_with_no_S104_uses_the_prediction_and_names_the_station()
    {
        var catalogue = Catalogue(subordinate: false);
        var depth = LoadedDatasetFactory.S102(
            "102A",
            LoadedDatasetFactory.Box(0, 0, 0.1, 0.1),
            S102Synth.Source(new Datasets.S102.S102Dataset
            {
                HorizontalCRS = 4326,
                VerticalDatum = Mllw,
                Coverages = S102Synth.Dataset(spacingLat: 0.01, spacingLon: 0.01, numRows: 10, numCols: 10, depth: 20f).Coverages,
            }));
        var sources = Sources(catalogue, Chart(), depth);
        var departure = new DateTimeOffset(S104Time);
        var plan = new PassagePlan(
            [
                new PassageLeg
                {
                    Points = [new GeoPosition(0.02, 0.03), new GeoPosition(0.02, 0.08)],
                    Speed = Speed.FromKnots(6),
                    Draft = Quantities.Depth.FromMetres(10),
                    RequiredUkc = Quantities.Depth.FromMetres(2),
                },
            ],
            departure);

        var report = new RouteChecker(sources).Check(plan, new RouteCheckSettings { At = departure }, TestContext.Current.CancellationToken);

        var clearance = report.Clearance!;
        Assert.All(clearance.Samples, s =>
        {
            Assert.Equal(WaterLevelStatus.Applied, s.WaterLevel.Status);
            Assert.True(s.WaterLevel.Source!.IsPredicted);
            Assert.True(s.Confidence.HasFlag(ConfidenceReasons.WaterLevelPredicted));
        });
        var predicted = Assert.Single(report.Findings, f => f.Code == RouteFindingCodes.WaterLevelPredicted);
        Assert.Contains("Harbour H1", predicted.Meta, StringComparison.Ordinal);
        Assert.Equal(RouteFindingGroup.Info, predicted.Group);
        Assert.Contains(clearance.Sources.WaterLevelSources, w => w.IsPredicted && w.StationId == "H1");
    }
}
