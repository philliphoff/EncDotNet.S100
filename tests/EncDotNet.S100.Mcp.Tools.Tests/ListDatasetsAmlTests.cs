using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.S57;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;
using EncDotNet.S57.Aml;

namespace EncDotNet.S100.Mcp.Tools.Tests;

/// <summary>
/// list_datasets says which datasets are NATO Additional Military Layers and
/// gives the highest loaded marking, so an agent can say it before describing
/// the map (#1236, decision 14).
/// </summary>
public class ListDatasetsAmlTests
{
    private static LoadedDataset Aml(string id, string code, string marking) => new(
        new DatasetId(id),
        new SpecRef(SpecName.Aml, new SpecVersion(1, 0, 0)),
        LoadedDatasetFactory.Box(0, 0, 1, 1),
        null,
        new AmlDatasetData(
            new AmlDatasetInfo
            {
                DataSetName = id,
                Product = AmlProduct.CLB,
                ProductSpecification = 54,
                ProductCode = code,
                ProductName = "Contour Line Bathymetry",
                ProtectiveMarking = marking,
            },
            new DatasetMetadata { Spec = new SpecRef(SpecName.Aml, new SpecVersion(1, 0, 0)) }));

    [Fact]
    public async Task AmlDatasets_CarryTheirProductAndMarking_AndTheHighestMarkingIsReported()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S124("warnings", bounds: LoadedDatasetFactory.Box(0, 0, 10, 10)));
        catalog.Add(Aml("UKC6UT00.000", "CLB", "UNCLASSIFIED"));
        catalog.Add(Aml("UKR6RT00.000", "RAL", "RESTRICTED"));

        var result = await new ListDatasetsTool(catalog).InvokeAsync(new ListDatasetsRequest(), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal("RESTRICTED", value.HighestMarking);
        Assert.Null(value.Datasets.Single(d => d.Id.Value == "warnings").Aml);
        var aml = value.Datasets.Single(d => d.Id.Value == "UKR6RT00.000").Aml!;
        Assert.Equal("RAL", aml.Product);
        Assert.Equal("RESTRICTED", aml.ProtectiveMarking);
        Assert.False(aml.Drawn);
        Assert.Equal("recognised-not-drawn", aml.Status);
    }

    [Fact]
    public async Task WithoutAml_ThereIsNoHighestMarking()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S124("warnings", bounds: LoadedDatasetFactory.Box(0, 0, 10, 10)));

        var result = await new ListDatasetsTool(catalog).InvokeAsync(new ListDatasetsRequest(), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var value));
        Assert.Null(value.HighestMarking);
    }
}
