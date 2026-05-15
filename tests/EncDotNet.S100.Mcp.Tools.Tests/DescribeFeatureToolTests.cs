using System.Text.Json;
using EncDotNet.S100.Mcp.Tools.Catalog;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;

namespace EncDotNet.S100.Mcp.Tools.Tests;

public class DescribeFeatureToolTests
{
    [Fact]
    public async Task Returns_DatasetNotFound_for_unknown_dataset_id()
    {
        var catalog = new FakeDatasetCatalog();
        var tool = new DescribeFeatureTool(catalog);

        var result = await tool.InvokeAsync(new DescribeFeatureRequest(new DatasetId("missing"), "f1"));

        Assert.True(result.TryGetError(out var error));
        var notFound = Assert.IsType<DatasetNotFound>(error);
        Assert.Equal(new DatasetId("missing"), notFound.Id);
    }

    [Fact]
    public async Task Returns_SpecNotSupported_for_unsupported_spec()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S122("s122-ds"));
        var tool = new DescribeFeatureTool(catalog);

        var result = await tool.InvokeAsync(new DescribeFeatureRequest(new DatasetId("s122-ds"), "x"));

        Assert.True(result.TryGetError(out var error));
        var unsupported = Assert.IsType<SpecNotSupportedForTool>(error);
        Assert.Equal("S-122", unsupported.Spec.Name);
        Assert.Equal(DescribeFeatureTool.Name, unsupported.Tool);
    }

    [Fact]
    public async Task Returns_FeatureNotFound_for_unknown_feature_id()
    {
        var catalog = new FakeDatasetCatalog();
        var model = S124Synth.Dataset(S124Synth.Feature("known"));
        catalog.Add(LoadedDatasetFactory.S124("ds", model));
        var tool = new DescribeFeatureTool(catalog);

        var result = await tool.InvokeAsync(new DescribeFeatureRequest(new DatasetId("ds"), "absent"));

        Assert.True(result.TryGetError(out var error));
        var notFound = Assert.IsType<FeatureNotFound>(error);
        Assert.Equal("absent", notFound.FeatureId);
    }

    [Fact]
    public async Task Returns_basic_feature_with_attributes_and_type_name()
    {
        var catalog = new FakeDatasetCatalog();
        var feature = S124Synth.Feature(
            id: "warn-1",
            featureType: "NavwarnPart",
            attributes: new Dictionary<string, string> { ["status"] = "in force" });
        catalog.Add(LoadedDatasetFactory.S124("ds", S124Synth.Dataset(feature)));
        var tool = new DescribeFeatureTool(catalog);

        var result = await tool.InvokeAsync(new DescribeFeatureRequest(new DatasetId("ds"), "warn-1"));

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal("NavwarnPart", value.FeatureTypeName);
        Assert.Equal("S-124", value.Spec.Name);

        var attrs = value.Attributes.GetProperty("attributes");
        Assert.Equal("in force", attrs.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Serializes_complex_attributes()
    {
        var catalog = new FakeDatasetCatalog();
        var feature = S124Synth.Feature(
            id: "f",
            complex: [S124Synth.Complex("fixedDateRange", new Dictionary<string, string>
            {
                ["dateStart"] = "2025-01-01",
                ["dateEnd"] = "2025-01-31",
            })]);
        catalog.Add(LoadedDatasetFactory.S124("ds", S124Synth.Dataset(feature)));
        var tool = new DescribeFeatureTool(catalog);

        var result = await tool.InvokeAsync(new DescribeFeatureRequest(new DatasetId("ds"), "f"));

        Assert.True(result.TryGetValue(out var value));
        var complex = value.Attributes.GetProperty("complexAttributes");
        Assert.Equal(JsonValueKind.Array, complex.ValueKind);
        var first = complex[0];
        Assert.Equal("fixedDateRange", first.GetProperty("code").GetString());
        Assert.Equal("2025-01-01", first.GetProperty("subAttributes").GetProperty("dateStart").GetString());
    }

    [Fact]
    public async Task Projects_reference_with_resolved_target_in_same_dataset()
    {
        var catalog = new FakeDatasetCatalog();
        var target = S124Synth.Feature("target");
        var source = S124Synth.Feature(
            id: "source",
            references: [S124Synth.Ref("theWarningPart", "#target")]);
        catalog.Add(LoadedDatasetFactory.S124("ds", S124Synth.Dataset(target, source)));
        var tool = new DescribeFeatureTool(catalog);

        var result = await tool.InvokeAsync(new DescribeFeatureRequest(new DatasetId("ds"), "source"));

        Assert.True(result.TryGetValue(out var value));
        var reference = Assert.Single(value.References);
        Assert.Equal("theWarningPart", reference.Role);
        Assert.True(reference.Resolved);
        Assert.Equal(new DatasetId("ds"), reference.TargetDatasetId);
        Assert.Equal("target", reference.TargetFeatureId);
    }

    [Fact]
    public async Task Projects_reference_unresolved_when_target_missing()
    {
        var catalog = new FakeDatasetCatalog();
        var source = S124Synth.Feature(
            id: "source",
            references: [S124Synth.Ref("theWarningPart", "#nope")]);
        catalog.Add(LoadedDatasetFactory.S124("ds", S124Synth.Dataset(source)));
        var tool = new DescribeFeatureTool(catalog);

        var result = await tool.InvokeAsync(new DescribeFeatureRequest(new DatasetId("ds"), "source"));

        Assert.True(result.TryGetValue(out var value));
        var reference = Assert.Single(value.References);
        Assert.False(reference.Resolved);
        Assert.Null(reference.TargetDatasetId);
        Assert.Equal("nope", reference.TargetFeatureId);
    }

    [Fact]
    public async Task Resolves_reference_against_information_type_in_same_dataset()
    {
        var catalog = new FakeDatasetCatalog();
        var info = S124Synth.Info("preamble-1");
        var source = S124Synth.Feature(
            id: "source",
            references: [S124Synth.Ref("preamble", "#preamble-1")]);
        catalog.Add(LoadedDatasetFactory.S124("ds",
            S124Synth.Dataset([source], [info])));
        var tool = new DescribeFeatureTool(catalog);

        var result = await tool.InvokeAsync(new DescribeFeatureRequest(new DatasetId("ds"), "source"));

        Assert.True(result.TryGetValue(out var value));
        var reference = Assert.Single(value.References);
        Assert.True(reference.Resolved);
        Assert.Equal("preamble-1", reference.TargetFeatureId);
    }

    [Fact]
    public async Task Resolves_reference_across_loaded_datasets()
    {
        var catalog = new FakeDatasetCatalog();
        var target = S124Synth.Feature("cross-target");
        var source = S124Synth.Feature(
            id: "source",
            references: [S124Synth.Ref("xref", "#cross-target")]);
        catalog.Add(LoadedDatasetFactory.S124("ds-a", S124Synth.Dataset(target)));
        catalog.Add(LoadedDatasetFactory.S124("ds-b", S124Synth.Dataset(source)));
        var tool = new DescribeFeatureTool(catalog);

        var result = await tool.InvokeAsync(new DescribeFeatureRequest(new DatasetId("ds-b"), "source"));

        Assert.True(result.TryGetValue(out var value));
        var reference = Assert.Single(value.References);
        Assert.True(reference.Resolved);
        Assert.Equal(new DatasetId("ds-a"), reference.TargetDatasetId);
    }

    [Fact]
    public async Task Empty_references_collection_returns_no_references()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S124("ds", S124Synth.Dataset(S124Synth.Feature("f"))));
        var tool = new DescribeFeatureTool(catalog);

        var result = await tool.InvokeAsync(new DescribeFeatureRequest(new DatasetId("ds"), "f"));

        Assert.True(result.TryGetValue(out var value));
        Assert.Empty(value.References);
    }
}
