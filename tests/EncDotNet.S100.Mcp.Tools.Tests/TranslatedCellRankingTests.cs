using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Mcp.Tools.Tests;

/// <summary>
/// Overlapping translated S-57 cells are ranked by their compilation scale
/// (DSPM CSCL), which the payload carries because the S-101 translation drops
/// it and their <c>DataCoverage</c> has no display scale. As in Charleston
/// Harbor with NOAA ENCs: the band-4 cell charts the harbour as one shallow
/// <c>DEPARE</c>, leaving the detail to band 5.
/// </summary>
public class TranslatedCellRankingTests
{
    private static readonly (double Lat, double Lon)[] CoastalBox = S101AreaSynth.Box(-0.1, -0.1, 0.2, 0.2);
    private static readonly (double Lat, double Lon)[] HarbourBox = S101AreaSynth.Box(-0.05, -0.05, 0.15, 0.15);

    private static LoadedDataset Translated(string id, int? compilationScale, (double Lat, double Lon)[] coverage, double drval1) =>
        LoadedDatasetFactory.S101(
            id,
            S101AreaSynth.Cell([S101AreaSynth.DataCoverage(coverage), S101AreaSynth.DepthArea(drval1, coverage)]),
            LoadedDatasetFactory.Box(-0.1, -0.1, 0.2, 0.2),
            compilationScale);

    private static RouteCheckResult Check(params LoadedDataset[] datasets)
    {
        var leg = new PassageLeg
        {
            Points = [new GeoPosition(0, 0), new GeoPosition(0, 0.1)],
            PortLimit = Length.FromMetres(50),
            StarboardLimit = Length.FromMetres(50),
        };
        var settings = new RouteCheckSettings
        {
            At = DateTimeOffset.UnixEpoch,
            SafetyContour = Depth.FromMetres(10),
            SafetyDepth = Depth.FromMetres(10),
        };
        return RouteCheckDataSources.FromDatasets(datasets).CreateEngine().Check(new PassagePlan([leg]), settings, TestContext.Current.CancellationToken);
    }

    [Fact]
    public void A_coarser_translated_cell_s_depth_area_does_not_count_inside_a_finer_one()
    {
        var result = Check(
            Translated("US4", 45_000, CoastalBox, 0),
            Translated("US5", 12_000, HarbourBox, 15));

        Assert.DoesNotContain(result.Findings, f => f.Code == RouteCheckCodes.CrossesSafetyContour);
        Assert.DoesNotContain(result.Findings, f => f.Code == RouteCheckCodes.SafetyContourInCorridor);
    }

    [Fact]
    public void Without_a_compilation_scale_the_cells_are_peers_and_the_shoal_one_counts()
    {
        var result = Check(
            Translated("US4", null, CoastalBox, 0),
            Translated("US5", null, HarbourBox, 15));

        Assert.Contains(result.Findings, f => f.Code == RouteCheckCodes.CrossesSafetyContour);
    }

    [Fact]
    public void Charted_depth_comes_from_the_finer_translated_cell()
    {
        var sources = PassageDataSources.FromDatasets(
            [Translated("US4", 45_000, CoastalBox, 0), Translated("US5", 12_000, HarbourBox, 15)],
            new ProjNetCrsTransformFactory());

        var depth = sources.Depth.Sample(0, 0.05)!;
        Assert.Equal(15, depth.DepthMeters);
        Assert.Equal("US5", depth.DatasetId);

        // Outside the finer cell's coverage the coarser one still answers.
        var outside = sources.Depth.Sample(0.18, 0.18)!;
        Assert.Equal(0, outside.DepthMeters);
        Assert.Equal("US4", outside.DatasetId);
    }
}
