using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.ExchangeSets;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Unit tests for <see cref="S101ExchangeSetUpdatePlan"/>, which groups S-101
/// base cells with their in-set sequential updates (S-100 Part 10a) so each cell
/// is loaded once at its up-to-date state.
/// </summary>
public class S101ExchangeSetUpdatePlanTests
{
    [Fact]
    public void Build_BaseWithUpdates_GroupsAndOrders()
    {
        var datasets = new[]
        {
            S101("101NL00NZ110", 0),
            S101("101NL00NZ110", 2),
            S101("101NL00NZ110", 1),
        };

        var plan = S101ExchangeSetUpdatePlan.Build(datasets);

        var item = Assert.Single(plan);
        Assert.Equal(S101LoadItemKind.BaseWithUpdates, item.Kind);
        Assert.Equal("101NL00NZ110.000", item.Base.FileName);
        Assert.Equal(new[] { 1, 2 }, item.Updates.Select(u => u.UpdateNumber!.Value).ToArray());
    }

    [Fact]
    public void Build_BaseWithNoUpdates_IsSingle()
    {
        var plan = S101ExchangeSetUpdatePlan.Build(new[] { S101("101GB00ABCDEF", 0) });

        var item = Assert.Single(plan);
        Assert.Equal(S101LoadItemKind.Single, item.Kind);
    }

    [Fact]
    public void Build_UpdateWithNoBase_IsOrphan()
    {
        var plan = S101ExchangeSetUpdatePlan.Build(new[] { S101("101IT00600154", 1) });

        var item = Assert.Single(plan);
        Assert.Equal(S101LoadItemKind.OrphanUpdate, item.Kind);
        Assert.Equal("101IT00600154.001", item.Base.FileName);
    }

    [Fact]
    public void Build_NonS101_PassesThroughIndividually()
    {
        var datasets = new[]
        {
            NonS101("102NO32904820.h5", "S-102"),
            NonS101("104DK00.h5", "S-104"),
        };

        var plan = S101ExchangeSetUpdatePlan.Build(datasets);

        Assert.Equal(2, plan.Count);
        Assert.All(plan, p => Assert.Equal(S101LoadItemKind.Single, p.Kind));
    }

    [Fact]
    public void Build_PreservesCatalogueOrder_AndEmitsCellOnce()
    {
        var datasets = new[]
        {
            NonS101("102NO32904820.h5", "S-102"),
            S101("101NL00NZ110", 0),
            S101("101NL00NZ110", 1),
            NonS101("104DK00.h5", "S-104"),
        };

        var plan = S101ExchangeSetUpdatePlan.Build(datasets);

        Assert.Equal(3, plan.Count);
        Assert.Equal("102NO32904820.h5", plan[0].Base.FileName);
        Assert.Equal(S101LoadItemKind.BaseWithUpdates, plan[1].Kind);
        Assert.Equal("104DK00.h5", plan[2].Base.FileName);
    }

    private static DatasetDiscoveryMetadata S101(string cellName, int updateNumber) =>
        new()
        {
            FileName = $"{cellName}.{updateNumber:D3}",
            UpdateNumber = updateNumber,
            ProductSpecification = new ProductSpecification { ProductIdentifier = "S-101" },
        };

    private static DatasetDiscoveryMetadata NonS101(string fileName, string productId) =>
        new()
        {
            FileName = fileName,
            ProductSpecification = new ProductSpecification { ProductIdentifier = productId },
        };
}
