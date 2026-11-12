using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.TestSupport;
using EncDotNet.S57;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Issue #1373 (AML S5): SoundCharts' own AML portrayal behind the
/// <see cref="IAmlPortrayal"/> seam. A cell with styled classes is drawn on the
/// Additional military layers plane, picks and drawing name the same features,
/// a cell without styled classes stays "recognised, not drawn", and a drawn
/// cell is still never a depth or route-check source.
/// </summary>
public sealed class AmlOverlayPortrayalTests
{
    [Fact]
    public void The_registry_draws_AML_with_SoundCharts_own_portrayal()
    {
        Assert.Same(AmlOverlayPortrayal.Default, S100Products.AmlPortrayal);
        Assert.True(S100Products.AmlPortrayal.Draws);
    }

    [Fact]
    public async Task A_RAL_cell_is_drawn_on_the_additional_military_layers_plane() =>
        await WithCell(AmlCellBuilder.Ral("UKR0TEST.000"), async path =>
        {
            var processor = new AmlDatasetProcessor(path, S100Products.AmlPortrayal);

            Assert.True(processor.Drawn);
            Assert.Equal("drawn", processor.Status);
            Assert.True(Assert.IsType<AmlDatasetData>(processor.CreateLoadedData()).Drawn);

            var result = await processor.BuildVectorPortrayalAsync(null, TestContext.Current.CancellationToken);
            Assert.Equal(["aml.operational", "aml.air"], result.SubLayers.Select(s => s.LayerKey));
            Assert.All(result.SubLayers, s => Assert.Equal(S98DisplayPlane.AdditionalMilitaryLayers, s.Plane));
            Assert.Equal("AML", result.Spec.Name);
            Assert.NotEmpty(result.CoverageAreas);
            Assert.StartsWith(processor.Spec.Name, result.Product, StringComparison.Ordinal);

            // The drawing and the processor's picks name features alike (decimal RCID).
            var drawn = result.SubLayers.SelectMany(s => s.Instructions).Select(i => i.FeatureReference).ToHashSet();
            var picked = processor.EnumerateFeatures().Select(f => f.FeatureRef).ToHashSet();
            Assert.Subset(picked, drawn);
            Assert.All(drawn, id => Assert.NotNull(result.GeometryProvider.GetGeometry(id)));
        });

    [Fact]
    public async Task A_cell_whose_classes_have_no_style_stays_recognised_not_drawn() =>
        await WithCell(AmlCellBuilder.Ral("UKR0RADR.000").Point("RADSTA", (-1.1, 50.6), ("OBJNAM", "Radar")), async path =>
        {
            var processor = new AmlDatasetProcessor(path, S100Products.AmlPortrayal);

            Assert.False(processor.Drawn);
            Assert.Equal("recognised-not-drawn", processor.Status);
            Assert.False(Assert.IsType<AmlDatasetData>(processor.CreateLoadedData()).Drawn);
            var result = await processor.BuildVectorPortrayalAsync(null, TestContext.Current.CancellationToken);
            Assert.Empty(result.SubLayers);
            Assert.Single(processor.Features);
        });

    [Fact]
    public Task A_drawn_cell_is_never_a_depth_cell_or_a_route_check_source() =>
        WithCell(AmlCellBuilder.Ral("UKR0TEST.000"), path =>
        {
            var processor = new AmlDatasetProcessor(path, S100Products.AmlPortrayal);
            Assert.True(processor.Drawn);
            Assert.False(typeof(IChartDepthCell).IsAssignableFrom(processor.GetType()));

            var dataset = LoadedDatasetProjector.Project(new DatasetId("aml"), processor)!;
            Assert.IsType<AmlDatasetData>(dataset.Data);
            var sources = CatalogRouteCheckSources.FromDatasets([dataset], new ProjNetCrsTransformFactory());
            var extent = new BoundingBox(50.3, -1.6, 50.8, -0.9);
            Assert.Empty(sources.Charts!.CellsNear(extent));
            Assert.Null(sources.Depth!.Sample(new GeoPosition(50.59, -1.07)));
            return Task.CompletedTask;
        });

    // Writes the builder's cell (all RAL classes when it has no features yet) to a temporary file.
    private static async Task WithCell(AmlCellBuilder builder, Func<string, Task> body)
    {
        var dir = Directory.CreateTempSubdirectory("aml-s5-").FullName;
        try
        {
            var document = builder.BuildDocument();
            if (document.FeatureRecords.Count == 0)
                document = AmlFixtures.RalBuilder().BuildDocument();
            var path = Path.Combine(dir, document.DataSetIdentification!.DataSetName);
            S57DocumentWriter.WriteToFile(path, document);
            await body(path);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
