using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.S57;
using EncDotNet.S100.Features;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Specifications;
using EncDotNet.S100.TestSupport;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// A chart cell's edition and compilation scale for the S-98 position legend
/// (#1407): an S-57 cell reports its DSID edition, update and issue date and
/// its DSPM compilation scale, as its header does.
/// </summary>
public sealed class ChartCellEditionTests
{
    [Fact]
    public void An_s57_cell_reports_its_dsid_edition_and_dspm_compilation_scale()
    {
        var path = FixturePath();
        Assert.SkipUnless(File.Exists(path), $"Fixture not found: {path}");
        var header = S57DatasetHeader.Read(path)!;

        var catalogues = new PortrayalCatalogueManager();
        catalogues.SetSource("S-101", Specification.CreatePortrayalCatalogueSource("S-101"));
        IChartDepthCell cell = new S57DatasetProcessor(
            path,
            catalogues,
            TestLuaEngine.Create(),
            new FeatureCatalogueManager(spec => Specification.TryOpenFeatureCatalogue(spec)));

        Assert.NotNull(header.EditionNumber);
        Assert.Equal(new ChartCellEdition(header.EditionNumber!.Value, header.UpdateNumber ?? 0, header.IssueDate), cell.Edition);
        Assert.NotNull(header.CompilationScale);
        Assert.Equal(header.CompilationScale, cell.CompilationScale);
    }

    private static string FixturePath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "datasets", "S57", "US5MA1BO", "US5MA1BO.000");
            if (File.Exists(candidate))
                return candidate;
        }

        return Path.Combine("tests", "datasets", "S57", "US5MA1BO", "US5MA1BO.000");
    }
}
