using EncDotNet.S100.Core;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Datasets.S102;
using EncDotNet.S100.Datasets.S104;
using EncDotNet.S100.ExchangeSets;
using EncDotNet.S100.Hdf5.PureHdf;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Rendering.Scene;
using EncDotNet.S100.Testing.Scenarios.Quirks;
using EncDotNet.S100.Testing.Scenarios.Writers;
using NetTopologySuite.Geometries;

namespace EncDotNet.S100.Testing.Scenarios.Tests;

/// <summary>
/// Pins how the library handles each producer quirk today (#943). These
/// tests describe current behaviour, not necessarily the behaviour we want:
/// when one fails because the handling improved, update it in the same
/// change, deliberately.
/// </summary>
public sealed class QuirkScenarioTests : IDisposable
{
    private static readonly DateTime Morning = new(2026, 3, 1, 6, 0, 0, DateTimeKind.Utc);

    private readonly string _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "s100-quirks-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    public static TheoryData<string> Names() => new(QuirkScenarios.All().Select(d => d.Name));

    // Every quirk scenario's validation findings, by dataset and rule: which
    // quirks the rule packs notice, and which they don't.
    [Theory]
    [MemberData(nameof(Names))]
    public async Task Validation_findings_are_pinned(string name)
    {
        var scenario = Generate(QuirkScenarios.All().Single(d => d.Name == name));
        await using var exchangeSet = await S100ExchangeSet.OpenAsync(scenario.Root, TestContext.Current.CancellationToken);

        var findings = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var entry in exchangeSet.Datasets)
        {
            using var dataset = await entry.OpenAsync(TestContext.Current.CancellationToken);
            foreach (var finding in dataset.Validate()!.Findings)
            {
                findings.Add($"{System.IO.Path.GetFileNameWithoutExtension(entry.Metadata.RelativePath)} {finding.RuleId} {finding.Severity}");
            }
        }

        Assert.Equal(ExpectedFindings.GetValueOrDefault(name, []), findings);
    }

    private static readonly Dictionary<string, string[]> ExpectedFindings = new()
    {
        ["quirk-swapped-axis-order"] =
        [
            "102ZZ00SPIT S102-PROJ-AXIS Warning",
            "102ZZ00SPIT S102-R-4.1 Error",
            "102ZZ00SPIT S102-R-4.2 Error",
            "104ZZ00SPIT_GRD S104-PROJ-AXIS Warning",
            "104ZZ00SPIT_GRD S104-R-4.2 Error",
            "104ZZ00SPIT_STN S104-PROJ-AXIS Warning",
        ],
        ["quirk-s104-gap-and-expired-run"] = ["104ZZ00SPIT_GRD S104-R-2.2 Warning"],
        ["quirk-unusual-crs"] = ["102ZZ00SPIT S102-R-3.1 Warning"],
        ["quirk-longitude-0-360"] =
        [
            "102ZZ00SPIT S102-R-4.1 Error",
            "102ZZ00SPIT S102-R-4.2 Error",
            "104ZZ00SPIT_GRD S104-R-4.2 Error",
        ],

        // Not noticed by any rule: a missing or different S-102 datum,
        // inverted or missing display scales, no-data holes, a spiked ring,
        // and two same-band cells that disagree.
    };

    [Theory]
    [MemberData(nameof(Names))]
    public void Quirk_scenarios_are_byte_identical_on_a_second_run(string name)
    {
        var definition = QuirkScenarios.All().Single(d => d.Name == name);
        var first = ScenarioGenerator.Generate(definition, System.IO.Path.Combine(_root, name, "a"));
        var second = ScenarioGenerator.Generate(definition, System.IO.Path.Combine(_root, name, "b"));

        Assert.All(first.Datasets.Zip(second.Datasets), pair => Assert.Equal(File.ReadAllBytes(pair.First.FullPath), File.ReadAllBytes(pair.Second.FullPath)));
        Assert.Equal(File.ReadAllBytes(first.CataloguePath), File.ReadAllBytes(second.CataloguePath));
    }

    [Fact]
    public void Swapped_axis_order_is_reported_for_grids_and_corrected_for_stations()
    {
        var scenario = Generate(QuirkScenarios.SwappedAxisOrder());

        var surface = S102(scenario);
        Assert.Equal(AxisOrderOutcome.Swapped, surface.AxisOrder!.Outcome);
        Assert.Equal(AxisOrderEvidence.PhysicalBounds, surface.AxisOrder.Evidence);
        Assert.False(surface.AxisOrder.Normalized);
        Assert.Equal(-135, surface.Coverages[0].OriginLatitude, 6);

        var grid = Assert.IsType<S104DatasetData.GriddedCoverage>(S104(scenario, QuirkScenarios.WaterLevelGrid)).Dataset;
        Assert.Equal(AxisOrderOutcome.Swapped, grid.AxisOrder!.Outcome);
        Assert.False(grid.AxisOrder.Normalized);

        var stations = Assert.IsType<S104DatasetData.StationSeries>(S104(scenario, QuirkScenarios.Stations)).Dataset;
        Assert.Equal(AxisOrderOutcome.Swapped, stations.AxisOrder!.Outcome);
        Assert.True(stations.AxisOrder.Normalized);
        var west = scenario.Definition.World.Frame.ToGeo(scenario.Definition.World.Tide.Stations[0].X, scenario.Definition.World.Tide.Stations[0].Y);
        Assert.Equal(west.Latitude, stations.Stations[0].Latitude, 4);
        Assert.Equal(west.Longitude, stations.Stations[0].Longitude, 4);
    }

    [Fact]
    public void Swapped_S102_and_S104_grids_are_read_through_the_swap()
    {
        var scenario = Generate(QuirkScenarios.SwappedAxisOrder());

        AssertGridsReadAsWithoutQuirks(scenario, "channel-west");
        AssertGridsReadAsWithoutQuirks(scenario, "channel-east");
        var place = scenario.Definition.Place("channel-east");
        Assert.Contains(Sources(scenario).WaterLevel.Locate(place.Latitude, place.Longitude), s => s.Source.Kind == WaterLevelSourceKind.Grid);
    }

    [Fact]
    public void A_missing_S102_datum_applies_the_water_level_with_low_confidence()
    {
        var scenario = Generate(QuirkScenarios.S102DatumMissing());

        var sample = CheckAt(scenario, "channel-west", Morning);

        Assert.Equal(ChartedDepthKind.Bathymetry, sample.Depth!.Kind);
        Assert.Null(sample.Depth.VerticalDatum);
        Assert.Equal(WaterLevelStatus.Applied, sample.WaterLevel.Status);
        Assert.Equal(DatumAgreement.Unknown, sample.WaterLevel.Datum);
        Assert.Equal(ConfidenceReasons.DatumUnconfirmed, sample.Confidence);
    }

    [Fact]
    public void Different_S102_and_S104_datums_are_a_mismatch_and_the_water_level_is_not_applied()
    {
        var scenario = Generate(QuirkScenarios.DatumMismatch());

        var sample = CheckAt(scenario, "channel-west", Morning);

        Assert.Equal(QuirkScenarios.OtherVerticalDatum, sample.Depth!.VerticalDatum);
        // The height is reported but not added to the depth.
        Assert.Equal(WaterLevelStatus.DatumMismatch, sample.WaterLevel.Status);
        Assert.Equal(DatumAgreement.Different, sample.WaterLevel.Datum);
    }

    [Fact]
    public void Inverted_display_scales_are_swapped_and_missing_ones_are_absent()
    {
        var scenario = Generate(QuirkScenarios.DisplayScales());

        var harbour = S101Dataset.ReadMetadata(FilePath(scenario, QuirkScenarios.HarbourCell));
        var coastal = S101Dataset.ReadMetadata(FilePath(scenario, QuirkScenarios.CoastalCell));

        Assert.Equal(new DisplayScaleRange(22000, 3000), harbour.DisplayScale);
        Assert.Null(coastal.DisplayScale);

        // The harbour cell still wins where both cover the spit.
        Assert.Equal(QuirkScenarios.HarbourCell, SampleDepth(scenario, "spit-shoulder")!.DatasetId);
    }

    [Fact]
    public void An_S102_no_data_hole_falls_back_to_S101_and_the_rest_of_the_grid_is_used()
    {
        var scenario = Generate(QuirkScenarios.NoDataHoles());

        var inHole = SampleDepth(scenario, "channel-west");
        Assert.Equal(ChartedDepthKind.DredgedArea, inHole!.Kind);
        Assert.Equal(QuirkScenarios.HarbourCell, inHole.DatasetId);
        Assert.True(inHole.IsLowConfidence);

        var outside = SampleDepth(scenario, "channel-east");
        Assert.Equal(ChartedDepthKind.Bathymetry, outside!.Kind);
        Assert.Equal(QuirkScenarios.Surface, outside.DatasetId);
    }

    [Fact]
    public void An_S104_gap_is_interpolated_across_on_a_grid_and_is_no_data_at_a_station()
    {
        var scenario = Generate(QuirkScenarios.WaterLevelGapAndExpiredRun());
        var definition = scenario.Definition;
        var place = definition.Place("channel-east");
        var (x, y) = definition.Places["channel-east"];
        var series = Sources(scenario).WaterLevel.Locate(place.Latitude, place.Longitude);
        var inGap = new DateTime(2026, 3, 1, 7, 30, 0, DateTimeKind.Utc);

        // The grid joins 05:00 and 10:00 with a straight line, however far apart.
        var grid = series.Single(s => s.Source.Kind == WaterLevelSourceKind.Grid);
        var before = definition.World.Tide.HeightAt(x, y, QuirkScenarios.Gap.From.AddHours(-1));
        var after = definition.World.Tide.HeightAt(x, y, QuirkScenarios.Gap.To.AddHours(1));
        var reading = grid.ValueAt(inGap)!.Value;
        Assert.Equal(before + (after - before) * 0.5, reading.HeightMeters, 3);
        Assert.Equal(QuirkScenarios.Gap.From.AddHours(-1), reading.StepTime);

        // A station writes the fill value in the gap.
        var station = series.First(s => s.Source.Kind == WaterLevelSourceKind.Station);
        Assert.Null(station.ValueAt(inGap));

        var sample = CheckAt(scenario, "channel-east", inGap, stationsOnly: true);
        Assert.Equal(WaterLevelStatus.NoData, sample.WaterLevel.Status);
    }

    [Fact]
    public void A_passage_after_the_run_ends_is_past_the_water_level()
    {
        var scenario = Generate(QuirkScenarios.WaterLevelGapAndExpiredRun());
        var place = scenario.Definition.Place("channel-east");

        Assert.All(Sources(scenario).WaterLevel.Locate(place.Latitude, place.Longitude), s => Assert.Equal(QuirkScenarios.RunEnd, s.End));

        var sample = CheckAt(scenario, "channel-east", QuirkScenarios.RunEnd.AddHours(1));
        Assert.Equal(WaterLevelStatus.AfterEnd, sample.WaterLevel.Status);
    }

    [Fact]
    public void An_S102_grid_in_another_geographic_crs_is_axis_checked_and_sampled()
    {
        var scenario = Generate(QuirkScenarios.UnusualCrs());

        var surface = S102(scenario);
        Assert.Equal(4258, surface.HorizontalCRS);
        Assert.Equal(AxisOrderOutcome.AsEncoded, surface.AxisOrder!.Outcome);

        // ETRS89 is read as WGS-84: the datums differ by well under a cell.
        AssertGridsReadAsWithoutQuirks(scenario, "channel-west");
    }

    [Fact]
    public void Longitudes_in_0_to_360_are_kept_and_grids_and_stations_are_found_from_minus_180_to_180()
    {
        var scenario = Generate(QuirkScenarios.Longitudes0To360());
        var catalog = Catalog(scenario);

        Assert.Equal(225, catalog.Datasets.Single(d => d.Id.Value == QuirkScenarios.Surface).Bounds.WestLongitude, 6);
        Assert.Equal(AxisOrderOutcome.AsEncoded, S102(scenario).AxisOrder!.Outcome);

        // Sampled at −135°, the S-102 surface and S-104 grid are read as in
        // −180…180, and the S-104 stations are still found.
        AssertGridsReadAsWithoutQuirks(scenario, "channel-east");
        var place = scenario.Definition.Place("channel-east");
        var series = Sources(scenario).WaterLevel.Locate(place.Latitude, place.Longitude);
        Assert.Contains(series, s => s.Source.Kind == WaterLevelSourceKind.Station);

        // The finest-grid lookup finds the surface from −180…180 too.
        Assert.NotNull(Sources(scenario).Depth.FinestGridSpacingMeters(place.Latitude - 0.001, place.Longitude - 0.001, place.Latitude + 0.001, place.Longitude + 0.001));
    }

    [Fact]
    public void A_spiked_pattern_area_is_written_and_pattern_clipping_copes_with_it()
    {
        var scenario = Generate(QuirkScenarios.MixedDimensionArea());
        var document = S101Dataset.Open(FilePath(scenario, QuirkScenarios.HarbourCell)).Document;
        var sandwaveCode = document.FeatureTypeCatalogue.Single(t => t.Value == "Sandwave").Key;
        Assert.Single(document.Features, f => f.FeatureTypeCode == sandwaveCode);

        // The spiked ring is invalid (it touches itself); overlapping it with
        // another pattern area must not throw (#891).
        var factory = new GeometryFactory();
        var frame = scenario.Definition.World.Frame;
        var spiked = factory.CreatePolygon(DatasetQuirks.SpikedRing(frame, new SpikedArea("Sandwave", QuirkScenarios.SpikedSandwave, 150))
            .Select(p => new Coordinate(p.Longitude, p.Latitude)).ToArray());
        Assert.False(spiked.IsValid);
        var other = factory.CreatePolygon(S101CellWriter.Rectangle(frame, new World.LocalRect(1800, 3500, 2600, 4300))
            .Select(p => new Coordinate(p.Longitude, p.Latitude)).ToArray());

        var clipped = PatternPriorityClipper.Clip(
            [new PatternPriorityClipper.PatternGroup("SNDWAV01", 1, [spiked]), new PatternPriorityClipper.PatternGroup("OTHER01", 2, [other])],
            []);

        Assert.Equal(2, clipped.Count);
        Assert.All(clipped, c => Assert.Equal(Dimension.Surface, c.Geometry.Dimension));
    }

    [Fact]
    public void Overlapping_same_band_cells_take_the_shoaler_value()
    {
        var scenario = Generate(QuirkScenarios.OverlappingSameBandCells());
        var definition = scenario.Definition;
        var contours = SyntheticScenarios.SyntheticSoundCells[0].DepthContours;

        // A point in the overlap, off the S-102 surface, where 1 m of bias
        // moves the depth into a shallower band.
        var (x, y, depth) = PointWhereBiasChangesBand(definition, contours);
        var position = definition.World.Frame.ToGeo(x, y);
        var sampled = Sources(scenario).Depth.Sample(position.Latitude, position.Longitude);

        Assert.Equal(ChartedDepthKind.DepthArea, sampled!.Kind);
        Assert.Equal(QuirkScenarios.OverlappingCell, sampled.DatasetId);
        Assert.Equal(contours.Last(c => c <= depth - 1), sampled.DepthMeters);
    }

    private static (double X, double Y, double Depth) PointWhereBiasChangesBand(ScenarioDefinition definition, IReadOnlyList<double> contours)
    {
        for (var y = 3700.0; y < 5900; y += 100)
        {
            for (var x = 100.0; x < 2900; x += 100)
            {
                var depth = definition.World.DepthAt(x, y);
                var band = contours.LastOrDefault(c => c <= depth, double.NaN);
                if (depth > 1.5 && contours.Any(c => depth - c is > 0.3 and < 0.7) && contours.Last(c => c <= depth - 1) < band)
                {
                    return (x, y, depth);
                }
            }
        }

        throw new InvalidOperationException("No point in the overlap changes band.");
    }

    // Passage depth and water level read the S-102 surface and S-104 grid at
    // the place exactly as in the same scenario without quirks: same value,
    // same cell.
    private void AssertGridsReadAsWithoutQuirks(GeneratedScenario scenario, string place)
    {
        var plain = Generate(SyntheticScenarios.SpitAcrossScales());
        var position = scenario.Definition.Place(place);
        var expected = Sources(plain);
        var actual = Sources(scenario);

        var depth = actual.Depth.Sample(position.Latitude, position.Longitude);
        Assert.Equal(ChartedDepthKind.Bathymetry, depth!.Kind);
        Assert.Equal(QuirkScenarios.Surface, depth.DatasetId);
        Assert.Equal(expected.Depth.Sample(position.Latitude, position.Longitude), depth);

        static IEnumerable<(WaterLevelSourceInfo Source, WaterLevelReading? Reading)> Grids(PassageDataSources sources, GeoPosition position) =>
            sources.WaterLevel.Locate(position.Latitude, position.Longitude)
                .Where(s => s.Source.Kind == WaterLevelSourceKind.Grid)
                .Select(s => (s.Source, s.ValueAt(Morning)));

        Assert.Equal(Grids(expected, position), Grids(actual, position));
    }

    private GeneratedScenario Generate(ScenarioDefinition definition) =>
        ScenarioGenerator.Generate(definition, System.IO.Path.Combine(_root, definition.Name));

    private static string FilePath(GeneratedScenario scenario, string datasetName) =>
        scenario.Datasets.Single(d => System.IO.Path.GetFileNameWithoutExtension(d.RelativePath) == datasetName).FullPath;

    private static S102Dataset S102(GeneratedScenario scenario)
    {
        using var file = PureHdfFile.Open(FilePath(scenario, QuirkScenarios.Surface));
        return S102DatasetReader.Read(file);
    }

    private static S104DatasetData S104(GeneratedScenario scenario, string name)
    {
        using var file = PureHdfFile.Open(FilePath(scenario, name));
        return S104DatasetReader.ReadAny(file);
    }

    // Like ScenarioLoading.Catalog, but quirky data may load with warnings.
    private static FileDatasetCatalog Catalog(GeneratedScenario scenario)
    {
        var catalogue = ExchangeCatalogueReader.Read(scenario.CataloguePath);
        return FileDatasetCatalog.Build(
            catalogue.DatasetDiscoveryMetadata
                .Select(d => new FileDatasetInput(
                    new DatasetId(System.IO.Path.GetFileNameWithoutExtension(d.RelativePath)),
                    d.ProductSpecification!.ProductIdentifier!,
                    System.IO.Path.Combine(scenario.Root, d.RelativePath)))
                .ToList(),
            new ProjNetCrsTransformFactory());
    }

    private static PassageDataSources Sources(GeneratedScenario scenario, Func<LoadedDataset, bool>? include = null) =>
        PassageDataSources.FromDatasets(Catalog(scenario).Datasets.Where(include ?? (_ => true)), new ProjNetCrsTransformFactory());

    private static ChartedDepth? SampleDepth(GeneratedScenario scenario, string place)
    {
        var position = scenario.Definition.Place(place);
        return Sources(scenario).Depth.Sample(position.Latitude, position.Longitude);
    }

    private static UkcSample CheckAt(GeneratedScenario scenario, string place, DateTime at, bool stationsOnly = false)
    {
        var definition = scenario.Definition;
        var (x, y) = definition.Places[place];
        var sources = Sources(scenario, stationsOnly ? d => d.Id.Value != QuirkScenarios.WaterLevelGrid : null);
        var leg = new PassageLeg(
            [definition.World.Frame.ToGeo(x + 0.1, y + 0.1), definition.World.Frame.ToGeo(x + 1.1, y + 0.1)],
            DraftMeters: 6.0,
            SpeedKnots: 10,
            SquatMeters: 0.3,
            RequiredUkcMeters: 1.0);
        var result = new PassageCheckEngine(sources.Depth, sources.WaterLevel).Check(new PassagePlan([leg], new DateTimeOffset(at)), TestContext.Current.CancellationToken);
        return Assert.Single(result.Samples);
    }
}
