using System.Xml.Linq;
using EncDotNet.S100.Core;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Datasets.S411;
using EncDotNet.S100.Features;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;

namespace EncDotNet.S100.Mcp.Tools.Tests;

/// <summary>
/// The pick sections the contributors add (the S-411 egg code) reach MCP
/// through <c>describe_feature</c> and <c>identify_features</c>.
/// </summary>
public class PickSectionToolTests
{
    private static FakeDatasetCatalog SeaIceCatalog()
    {
        IReadOnlyList<GeoPosition> ring =
        [
            new GeoPosition(-1, -1), new GeoPosition(-1, 1), new GeoPosition(1, 1), new GeoPosition(1, -1), new GeoPosition(-1, -1),
        ];
        var seaIce = new S411Feature
        {
            Id = "seaice.1",
            FeatureType = "seaice",
            GeometryType = S100GeometryType.Surface,
            ExteriorRing = ring,
            Attributes = new Dictionary<string, string>
            {
                ["iceact"] = "70",
                ["iceapc"] = "[30, 40]",
                ["icesod"] = "[91, 87]",
                ["iceflz"] = "[5, 4]",
            },
            ComplexAttributes = [],
        };
        var lead = new S411Feature
        {
            Id = "lead.1",
            FeatureType = "LineOfIceLead",
            GeometryType = S100GeometryType.Surface,
            ExteriorRing = ring,
            Attributes = new Dictionary<string, string> { ["icesod"] = "[87]" },
            ComplexAttributes = [],
        };

        var catalog = new FakeDatasetCatalog();
        catalog.Add(new LoadedDataset(
            new DatasetId("ice"),
            new SpecRef("S-411", new SpecVersion(1, 2, 1)),
            LoadedDatasetFactory.Box(-1, -1, 1, 1),
            null,
            new S411DatasetData(new S411Dataset { Features = [seaIce, lead], SourceDocument = new XDocument() })));
        return catalog;
    }

    [Fact]
    public async Task DescribeFeature_SeaIce_CarriesTheEggCodeSection()
    {
        var tool = new DescribeFeatureTool(SeaIceCatalog());

        var result = await tool.InvokeAsync(
            new DescribeFeatureRequest(new DatasetId("ice"), "seaice.1"),
            TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var value));
        var section = Assert.Single(value.Sections!);
        Assert.Equal(IceEggCodeSection.KindName, section.Kind);
        Assert.NotNull(section.Description);
        Assert.Contains(section.Rows, r => r.Code == "Ct" && r.RawValue == "70");
        var data = section.Data!.Value;
        Assert.Equal("70", data.GetProperty("egg").GetProperty("totalConcentration").GetProperty("text").GetString());
    }

    [Fact]
    public async Task DescribeFeature_IceLead_HasNoSections()
    {
        var tool = new DescribeFeatureTool(SeaIceCatalog());

        var result = await tool.InvokeAsync(
            new DescribeFeatureRequest(new DatasetId("ice"), "lead.1"),
            TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var value));
        Assert.Null(value.Sections);
    }

    [Fact]
    public async Task IdentifyFeatures_SeaIce_CarriesTheEggCodeSection()
    {
        var tool = new IdentifyFeaturesTool(SeaIceCatalog());

        var result = await tool.InvokeAsync(new IdentifyFeaturesRequest(0.0, 0.0), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var value));
        var seaIce = value.Features.Single(f => f.FeatureId == "seaice.1");
        Assert.Equal(IceEggCodeSection.KindName, Assert.Single(seaIce.Sections!).Kind);
        Assert.Null(value.Features.Single(f => f.FeatureId == "lead.1").Sections);
    }
}
