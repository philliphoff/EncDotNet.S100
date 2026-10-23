using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Datasets.S104;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Mcp.Tools.Tests;

/// <summary>
/// The catalog adapters behind the passage check: S-102 with S-101 fallback,
/// and S-104 grids and stations, on synthetic datasets.
/// </summary>
public class PassageDataSourcesTests
{
    private const int Lat = 23;
    private const int Mllw = 12;

    private static PassageDataSources Sources(params LoadedDataset[] datasets) =>
        PassageDataSources.FromDatasets(datasets, new ProjNetCrsTransformFactory());

    private static LoadedDataset S102(string id, float depth = 12.5f, double spacing = 0.01, int n = 4, Func<int, int, float>? depthAt = null, int? datum = Lat)
    {
        var dataset = S102Synth.Dataset(spacingLat: spacing, spacingLon: spacing, numRows: n, numCols: n, depth: depth, depthAt: depthAt);
        dataset = new Datasets.S102.S102Dataset { HorizontalCRS = 4326, VerticalDatum = datum, Coverages = dataset.Coverages };
        return LoadedDatasetFactory.S102(id, LoadedDatasetFactory.Box(0, 0, spacing * n, spacing * n), S102Synth.Source(dataset));
    }

    private static LoadedDataset S101(params S101AreaSynth.AreaFeature[] areas) =>
        LoadedDatasetFactory.S101("101AREAS", S101AreaSynth.Cell(areas), LoadedDatasetFactory.Box(-0.1, -0.1, 0.2, 0.2));

    [Fact]
    public void S102_supplies_depth_uncertainty_datum_and_cell()
    {
        var sources = Sources(S102("102A", depthAt: (r, c) => 10 + r + c / 10f));

        var depth = Assert.IsType<ChartedDepth>(sources.Depth.Sample(0.015, 0.025));

        Assert.Equal(ChartedDepthKind.Bathymetry, depth.Kind);
        Assert.Equal(11.2, depth.DepthMeters, 4);
        Assert.Equal(0.25, depth.UncertaintyMeters!.Value, 4);
        Assert.Equal(Lat, depth.VerticalDatum);
        Assert.Equal("102A", depth.DatasetId);
        Assert.Equal("1,2", depth.Reference);
        Assert.False(depth.IsLowConfidence);
        Assert.Null(sources.Depth.Sample(0.5, 0.5));
    }

    [Fact]
    public void The_finest_overlapping_grid_sets_the_spacing_and_wins()
    {
        var sources = Sources(S102("coarse", depth: 20, spacing: 0.01), S102("fine", depth: 15, spacing: 0.001, n: 20));

        var spacing = sources.Depth.FinestGridSpacingMeters(0, 0, 0.01, 0.01);

        // 0,001° of longitude at the equator is about 111 m.
        Assert.InRange(spacing!.Value, 110, 112);
        Assert.Equal("fine", sources.Depth.Sample(0.005, 0.005)!.DatasetId);
        Assert.Equal("coarse", sources.Depth.Sample(0.035, 0.035)!.DatasetId);
        Assert.Null(sources.Depth.FinestGridSpacingMeters(1, 1, 2, 2));
    }

    [Fact]
    public void S102_nodata_falls_through_to_the_s101_depth_area()
    {
        var sources = Sources(
            S102("102A", depthAt: (r, c) => r == 0 && c == 0 ? Datasets.S102.S102CoverageSource.FillValue : 20),
            S101(S101AreaSynth.DepthArea(5, S101AreaSynth.Box(-0.05, -0.05, 0.05, 0.05))));

        var masked = sources.Depth.Sample(0.005, 0.005)!;
        Assert.Equal(ChartedDepthKind.DepthArea, masked.Kind);
        Assert.Equal(5, masked.DepthMeters);
        Assert.True(masked.IsLowConfidence);

        Assert.Equal(ChartedDepthKind.Bathymetry, sources.Depth.Sample(0.015, 0.015)!.Kind);
    }

    [Fact]
    public void S101_fallback_prefers_dredged_areas_takes_the_shoalest_and_honours_holes()
    {
        var sources = Sources(S101(
            S101AreaSynth.DepthArea(10, S101AreaSynth.Box(0, 0, 0.1, 0.1), hole: S101AreaSynth.Box(0.04, 0.04, 0.06, 0.06)),
            S101AreaSynth.DepthArea(7, S101AreaSynth.Box(0, 0, 0.03, 0.03)),
            S101AreaSynth.DredgedArea(12, S101AreaSynth.Box(0.07, 0.07, 0.09, 0.09)),
            S101AreaSynth.SoundingDatum(Mllw, S101AreaSynth.Box(-0.1, -0.1, 0.2, 0.2))));

        var overlap = sources.Depth.Sample(0.01, 0.01)!;
        Assert.Equal(7, overlap.DepthMeters);
        Assert.Equal(ChartedDepthKind.DepthArea, overlap.Kind);
        Assert.Equal(Mllw, overlap.VerticalDatum);

        var dredged = sources.Depth.Sample(0.08, 0.08)!;
        Assert.Equal(ChartedDepthKind.DredgedArea, dredged.Kind);
        Assert.Equal(12, dredged.DepthMeters);

        Assert.Null(sources.Depth.Sample(0.05, 0.05));
    }

    // Two overlapping cells as at Calshot Spit (#938): a harbour-band cell
    // charts DRVAL1 0 m over 0…0,05°, a coastal-band cell −10 m everywhere.
    private static LoadedDataset Banded(string id, int minimumDisplayScale, (double Lat, double Lon)[] coverage, params S101AreaSynth.AreaFeature[] areas) =>
        LoadedDatasetFactory.S101(
            id,
            S101AreaSynth.Cell([S101AreaSynth.DataCoverage(minimumDisplayScale, coverage), .. areas]),
            LoadedDatasetFactory.Box(-0.1, -0.1, 0.2, 0.2));

    private static LoadedDataset Coastal(int datum = Mllw) =>
        Banded("coastal", 90_000, S101AreaSynth.Box(-0.1, -0.1, 0.2, 0.2),
            S101AreaSynth.DepthArea(-10, S101AreaSynth.Box(-0.1, -0.1, 0.2, 0.2)),
            S101AreaSynth.SoundingDatum(datum, S101AreaSynth.Box(-0.1, -0.1, 0.2, 0.2)));

    [Fact]
    public void Overlapping_s101_cells_take_the_finest_cell_over_a_shoaler_coarser_one()
    {
        var sources = Sources(
            Coastal(),
            Banded("harbour", 22_000, S101AreaSynth.Box(0, 0, 0.05, 0.05),
                S101AreaSynth.DepthArea(0, S101AreaSynth.Box(0, 0, 0.05, 0.05)),
                S101AreaSynth.SoundingDatum(Lat, S101AreaSynth.Box(0, 0, 0.05, 0.05))));

        var inHarbour = sources.Depth.Sample(0.01, 0.01)!;
        Assert.Equal(0, inHarbour.DepthMeters);
        Assert.Equal("harbour", inHarbour.DatasetId);
        Assert.Equal(Lat, inHarbour.VerticalDatum);

        // Inside the harbour cell's bounds but outside its data coverage.
        var outside = sources.Depth.Sample(0.08, 0.08)!;
        Assert.Equal(-10, outside.DepthMeters);
        Assert.Equal("coastal", outside.DatasetId);
        Assert.Equal(Mllw, outside.VerticalDatum);
    }

    [Fact]
    public void Overlapping_s101_cells_of_the_same_band_take_the_shoalest()
    {
        var sources = Sources(
            Banded("deeper", 22_000, S101AreaSynth.Box(-0.1, -0.1, 0.2, 0.2), S101AreaSynth.DepthArea(5, S101AreaSynth.Box(-0.1, -0.1, 0.2, 0.2))),
            Banded("shoaler", 22_000, S101AreaSynth.Box(0, 0, 0.05, 0.05), S101AreaSynth.DepthArea(3, S101AreaSynth.Box(0, 0, 0.05, 0.05))));

        var depth = sources.Depth.Sample(0.01, 0.01)!;
        Assert.Equal(3, depth.DepthMeters);
        Assert.Equal("shoaler", depth.DatasetId);
    }

    [Fact]
    public void The_finest_cell_s_sounding_beats_a_coarser_cell_s_depth_area()
    {
        var harbour = LoadedDatasetFactory.S101(
            "harbour",
            S101AreaSynth.Cell([S101AreaSynth.DataCoverage(22_000, S101AreaSynth.Box(0, 0, 0.05, 0.05))], [(0.01, 0.01, 4.2)]),
            LoadedDatasetFactory.Box(-0.1, -0.1, 0.2, 0.2));
        var sources = Sources(Coastal(), harbour);

        var depth = sources.Depth.Sample(0.0101, 0.01)!;
        Assert.Equal(ChartedDepthKind.Sounding, depth.Kind);
        Assert.Equal(4.2, depth.DepthMeters, 6);
        Assert.Equal("harbour", depth.DatasetId);
    }

    [Fact]
    public void The_passage_result_names_the_finer_cell_as_its_charted_depth_source()
    {
        var sources = Sources(
            Coastal(),
            Banded("harbour", 22_000, S101AreaSynth.Box(0, 0, 0.05, 0.05),
                S101AreaSynth.DepthArea(8, S101AreaSynth.Box(0, 0, 0.05, 0.05)),
                S101AreaSynth.SoundingDatum(Mllw, S101AreaSynth.Box(0, 0, 0.05, 0.05))));
        var leg = new PassageLeg
        {
            Points = [new GeoPosition(0.01, 0.01), new GeoPosition(0.01, 0.04)],
            Draft = Depth.FromMetres(5),
            Speed = Speed.FromKnots(6),
            RequiredUkc = Depth.FromMetres(1),
        };
        var departure = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var plan = new PassagePlan([leg], departure);

        var result = new PassageCheckEngine(sources.Depth, sources.WaterLevel).Check(plan, new RouteCheckSettings { At = departure }, TestContext.Current.CancellationToken);

        Assert.Equal(["harbour"], result.Sources.FallbackDatasets);
        Assert.All(result.Samples, s => Assert.Equal(8, s.Depth!.DepthMeters));
    }

    [Fact]
    public void A_nearby_sounding_is_the_last_resort()
    {
        var cell = S101AreaSynth.Cell([], [(0.0, 0.0, 6.4), (0.0, 0.01, 3.0)]);
        var sources = Sources(LoadedDatasetFactory.S101("101S", cell, LoadedDatasetFactory.Box(-0.1, -0.1, 0.1, 0.1)));

        // About 111 m from the first sounding.
        var near = sources.Depth.Sample(0.001, 0.0)!;
        Assert.Equal(ChartedDepthKind.Sounding, near.Kind);
        Assert.Equal(6.4, near.DepthMeters, 6);
        Assert.InRange(near.SoundingDistanceMeters!.Value, 105, 115);

        // More than 250 m from both.
        Assert.Null(sources.Depth.Sample(0.005, 0.005));
    }

    [Fact]
    public void S104_grids_interpolate_in_time_and_report_the_cell_and_step()
    {
        // S104Synth heights are 1,5 / 1,6 / 1,7 m at 00:00 / 01:00 / 02:00.
        var sources = Sources(LoadedDatasetFactory.S104("104G"));

        var series = Assert.Single(sources.WaterLevel.Locate(0.011, 0.019));
        Assert.Equal(WaterLevelSourceKind.Grid, series.Source.Kind);
        Assert.Equal("1,2", series.Source.GridCell);
        Assert.Equal(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), series.Start);
        Assert.Equal(new DateTime(2024, 1, 1, 2, 0, 0, DateTimeKind.Utc), series.End);

        var reading = series.ValueAt(new DateTime(2024, 1, 1, 0, 40, 0, DateTimeKind.Utc))!.Value;
        Assert.Equal(1.5 + 0.1 * 40 / 60, reading.HeightMeters, 4);
        Assert.Equal(new DateTime(2024, 1, 1, 1, 0, 0, DateTimeKind.Utc), reading.StepTime);

        Assert.Empty(sources.WaterLevel.Locate(1, 1));
    }

    [Fact]
    public void S104_fill_values_are_no_data()
    {
        var dataset = S104Synth.Dataset(height: S104CoverageSource.FillValue);
        // S104Synth adds 0,1 m per step; force every step to the fill value.
        foreach (var coverage in dataset.Coverages)
        {
            for (var i = 0; i < coverage.Values.Length; i++)
            {
                coverage.Values[i] = new WaterLevelValue(S104CoverageSource.FillValue, 0);
            }
        }

        var series = Assert.Single(Sources(LoadedDatasetFactory.S104("104G", source: S104Synth.Source(dataset))).WaterLevel.Locate(0.01, 0.01));

        Assert.Null(series.ValueAt(new DateTime(2024, 1, 1, 1, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void Stations_apply_within_range_after_grids_nearest_first()
    {
        // StationSeries puts STN_001 at (47,0, −122,0) and STN_002 at (47,1, −122,1).
        var stations = LoadedDatasetFactory.S104Stations("104S", S104Synth.StationSeries());
        var sources = Sources(stations);

        var near = Assert.Single(sources.WaterLevel.Locate(47.01, -122.0));
        Assert.Equal(WaterLevelSourceKind.Station, near.Source.Kind);
        Assert.Equal("STN_001", near.Source.StationId);
        Assert.InRange(near.Source.DistanceMeters!.Value, 1100, 1125);

        // Interpolates between hourly samples: 1,0 m at 00:00, 1,1 m at 01:00.
        Assert.Equal(1.05, near.ValueAt(new DateTime(2024, 1, 1, 0, 30, 0, DateTimeKind.Utc))!.Value.HeightMeters, 4);

        Assert.Empty(sources.WaterLevel.Locate(48, -122.0));
    }
}
