using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Features;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.Specifications;
using EncDotNet.S100.Viewer.Services.Depth;
using Synth = EncDotNet.S100.Mcp.Tools.Tests.Fakes.S101AreaSynth;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The pick report reads its base depth only from the finest S-101 cells
/// covering the point, as the passage check does (issue #938).
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
        var catalogues = new PortrayalCatalogueManager();
        catalogues.SetSource("S-101", Specification.CreatePortrayalCatalogueSource("S-101"));
        return new S101DatasetProcessor(path, catalogues, new MoonSharpLuaEngine(), new FeatureCatalogueManager());
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
}
