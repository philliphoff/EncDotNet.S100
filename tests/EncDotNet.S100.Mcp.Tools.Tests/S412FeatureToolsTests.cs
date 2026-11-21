using System.Text.Json;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Datasets.S412;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;

namespace EncDotNet.S100.Mcp.Tools.Tests;

/// <summary>
/// <c>identify_features</c> and <c>describe_feature</c> on S-412 warnings
/// (#841): a warning is found inside its area, and its description carries
/// the threshold, Beaufort force and message text, decoded.
/// </summary>
public class S412FeatureToolsTests
{
    private static FakeDatasetCatalog Catalog()
    {
        var dataset = S412Dataset.Open(Path.Combine(AppContext.BaseDirectory, "TestData", "S412", "synthetic_all_warnings.gml"));
        var catalog = new FakeDatasetCatalog();
        catalog.Add(new LoadedDataset(
            new DatasetId("warnings"),
            new SpecRef("S-412", new SpecVersion(1, 1, 0)),
            LoadedDatasetFactory.Box(50, -38, 62, -20),
            null,
            new S412DatasetData(dataset)));
        return catalog;
    }

    private static async Task<JsonElement> DescribeAsync(string featureId)
    {
        var result = await new DescribeFeatureTool(Catalog()).InvokeAsync(
            new DescribeFeatureRequest(new DatasetId("warnings"), featureId), TestContext.Current.CancellationToken);
        Assert.True(result.TryGetValue(out var value));
        return value.Attributes;
    }

    [Fact]
    public async Task Identify_FindsTheWarningInsideItsArea()
    {
        var result = await new IdentifyFeaturesTool(Catalog()).InvokeAsync(
            new IdentifyFeaturesRequest(61.0, -23.0), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var value));
        var match = Assert.Single(value.Features);
        Assert.Equal("ICE1", match.FeatureId);
        Assert.Equal("IceAccretionWarning", match.FeatureType);
        Assert.Equal("inside", match.Containment);
    }

    [Fact]
    public async Task Describe_WindWarning_DecodesThresholdForceAndMessages()
    {
        var attributes = await DescribeAsync("WIND1");

        // The encoded attribute keeps the code.
        Assert.Equal("2", attributes.GetProperty("attributes").GetProperty("windWarningThreshold").GetString());

        var warning = attributes.GetProperty("warning");
        Assert.Equal("Wind", warning.GetProperty("kind").GetString());
        Assert.Equal("W-001", warning.GetProperty("weatherWarningNumber").GetString());
        Assert.Equal(2, warning.GetProperty("threshold").GetProperty("code").GetInt32());
        Assert.Equal("gale force", warning.GetProperty("threshold").GetProperty("label").GetString());
        Assert.Equal(8, warning.GetProperty("beaufortForce").GetProperty("code").GetInt32());
        Assert.Equal("gale", warning.GetProperty("beaufortForce").GetProperty("label").GetString());
        Assert.Equal("45 knots", warning.GetProperty("maximumWindSpeed")[0].GetProperty("text").GetString());

        var messages = warning.GetProperty("messages");
        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal("Gale warning: southwest 8, increasing 9 later.", messages[0].GetProperty("text").GetString());
        Assert.Equal("fra", messages[1].GetProperty("language").GetString());

        Assert.Equal(new DateTimeOffset(2026, 11, 7, 6, 0, 0, TimeSpan.Zero), warning.GetProperty("validFrom").GetDateTimeOffset());
        Assert.Equal(new DateTimeOffset(2026, 11, 8, 18, 0, 0, TimeSpan.Zero), warning.GetProperty("inForceUntil").GetDateTimeOffset());
        Assert.Equal("METAREA I", warning.GetProperty("dataProvider").GetProperty("metarea").GetString());
    }

    [Fact]
    public async Task Describe_KeepsEveryLanguageOfNestedAttributes()
    {
        var attributes = await DescribeAsync("WIND1");

        var message = attributes.GetProperty("complexAttributes").EnumerateArray()
            .Single(c => c.GetProperty("code").GetString() == "weatherWarningMessage");
        Assert.Equal(2, message.GetProperty("groups").GetArrayLength());
    }

    [Fact]
    public async Task Describe_IceAccretion_DecodesRateAndSource()
    {
        var warning = (await DescribeAsync("ICE1")).GetProperty("warning");

        Assert.Equal("severeIcing", warning.GetProperty("iceAccretionRate").GetProperty("label").GetString());
        Assert.Equal("freezingSpray", warning.GetProperty("sourceOfIcing").GetProperty("label").GetString());
    }

    [Fact]
    public async Task Describe_ResolvesTheDataProviderReference()
    {
        var result = await new DescribeFeatureTool(Catalog()).InvokeAsync(
            new DescribeFeatureRequest(new DatasetId("warnings"), "WAVE1"), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var value));
        var reference = Assert.Single(value.References);
        Assert.Equal("dataProviderInformation", reference.Role);
        Assert.True(reference.Resolved);
        Assert.Equal("six to nine metres", value.Attributes.GetProperty("warning").GetProperty("threshold").GetProperty("label").GetString());
    }
}
