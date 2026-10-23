using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Features;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.Specifications;
using EncDotNet.S100.TestSupport;
using EncDotNet.S100.Viewer.Services.Depth;
using Synth = EncDotNet.S100.Mcp.Tools.Tests.Fakes.S101AreaSynth;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The pick report reads its base depth only from the finest chart cells
/// covering the point, as the passage check does (issue #938). Translated S-57
/// cells rank by their compilation scale alongside S-101 cells (issue #1040).
/// </summary>
public sealed class LocationDepthProbeCellRankingTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("s101-ranking").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private S101DatasetProcessor Cell(string name, int minimumDisplayScale, (double Lat, double Lon)[] coverage)
    {
        var dataset = Synth.Cell([Synth.DataCoverage(minimumDisplayScale, coverage), Synth.DepthArea(5, coverage)]);
        var path = Path.Combine(_directory, name + ".000");
        S101DocumentWriter.WriteToFile(path, dataset.Document);
        return new S101DatasetProcessor(path, Catalogues(), new MoonSharpLuaEngine(), new FeatureCatalogueManager());
    }

    // The committed NOAA fixture (US5MA1BO) with its CSCL rewritten.
    private S57DatasetProcessor S57Cell(string name, uint compilationScale) =>
        new(
            SyntheticS57Cell.WriteWithCompilationScale(_directory, name + ".000", compilationScale),
            Catalogues(),
            new MoonSharpLuaEngine(),
            new FeatureCatalogueManager(spec => Specification.TryOpenFeatureCatalogue(spec)));

    private static PortrayalCatalogueManager Catalogues()
    {
        var catalogues = new PortrayalCatalogueManager();
        catalogues.SetSource("S-101", Specification.CreatePortrayalCatalogueSource("S-101"));
        return catalogues;
    }

    // A charted sounding of the fixture, so a point its coverage contains.
    private static (double Lat, double Lon) SoundingIn(S57DatasetProcessor cell)
    {
        var extent = cell.Metadata.Extent!;
        var centre = ((extent.SouthLatitude + extent.NorthLatitude) / 2, (extent.WestLongitude + extent.EastLongitude) / 2);
        var sounding = cell.SampleNearestSounding(centre.Item1, centre.Item2);
        Assert.NotNull(sounding);
        return (sounding.Value.Position.Latitude, sounding.Value.Position.Longitude);
    }

    [Fact]
    public void Only_the_finest_covering_cells_supply_the_depth()
    {
        var coastal = Cell("coastal", 90_000, Synth.Box(-0.1, -0.1, 0.2, 0.2));
        var harbourA = Cell("harbourA", 22_000, Synth.Box(0, 0, 0.05, 0.05));
        var harbourB = Cell("harbourB", 22_000, Synth.Box(0.02, 0.02, 0.06, 0.06));
        var cells = new[] { coastal, harbourA, harbourB };

        Assert.Equal([harbourA], LocationDepthProbe.FinestCoveringCells(cells, 0.01, 0.01)!);
        Assert.True(LocationDepthProbe.FinestCoveringCells(cells, 0.03, 0.03)!.SetEquals([harbourA, harbourB]));
        Assert.Equal([coastal], LocationDepthProbe.FinestCoveringCells(cells, 0.1, 0.1)!);
        Assert.Null(LocationDepthProbe.FinestCoveringCells(cells, 1, 1));
    }

    [Fact]
    public void Translated_s57_cells_rank_by_compilation_scale()
    {
        var coastal = S57Cell("US4COAST", 45_000);
        var harbour = S57Cell("US5HARB", 12_000);
        var peer = S57Cell("US5PEER", 12_000);
        var (lat, lon) = SoundingIn(harbour);

        Assert.True(coastal.CoversPoint(lat, lon));
        Assert.Equal(12_000, harbour.CellRankingScale);
        Assert.Equal([harbour], LocationDepthProbe.FinestCoveringCells([coastal, harbour], lat, lon)!);
        Assert.True(LocationDepthProbe.FinestCoveringCells([coastal, harbour, peer], lat, lon)!.SetEquals([harbour, peer]));
    }

    [Fact]
    public void Translated_s57_and_s101_cells_rank_together()
    {
        var coastal = S57Cell("US4COAST", 45_000);
        var (lat, lon) = SoundingIn(coastal);
        var harbour = Cell("harbour", 22_000, Synth.Box(lat - 0.01, lon - 0.01, lat + 0.01, lon + 0.01));
        var smallScale = Cell("overview", 90_000, Synth.Box(lat - 0.01, lon - 0.01, lat + 0.01, lon + 0.01));

        Assert.Equal([harbour], LocationDepthProbe.FinestCoveringCells([coastal, harbour], lat, lon)!);
        Assert.Equal([coastal], LocationDepthProbe.FinestCoveringCells([coastal, smallScale], lat, lon)!);
    }

    [Fact]
    public void Translated_s57_cells_supply_their_soundings()
    {
        var cell = S57Cell("US5HARB", 12_000);
        var (lat, lon) = SoundingIn(cell);

        var sounding = cell.SampleNearestSounding(lat, lon);
        Assert.NotNull(sounding);
        Assert.Equal(0, sounding.Value.DistanceMeters, 3);
    }
}
