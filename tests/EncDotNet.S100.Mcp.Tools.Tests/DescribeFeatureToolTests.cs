using System.Collections.Immutable;
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
    public async Task Returns_FeatureNotFound_for_unknown_feature_in_backfilled_spec()
    {
        // S-122 is now supported via the generic GmlFeatureDescriber
        // (PR-backfill), so an unknown feature returns FeatureNotFound
        // rather than SpecNotSupportedForTool.
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S122("s122-ds"));
        var tool = new DescribeFeatureTool(catalog);

        var result = await tool.InvokeAsync(new DescribeFeatureRequest(new DatasetId("s122-ds"), "x"));

        Assert.True(result.TryGetError(out var error));
        var notFound = Assert.IsType<FeatureNotFound>(error);
        Assert.Equal(new DatasetId("s122-ds"), notFound.Id);
        Assert.Equal("x", notFound.FeatureId);
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
    public async Task S101_describes_feature_by_bare_RCID()
    {
        var feature = S101Synth.Feature(rcid: 12345, featureTypeCode: 30,
            attributes: new[] { ((ushort)42, "5.0") });
        var featureTypes = new Dictionary<ushort, string> { [30] = "DEPARE" }.ToImmutableDictionary();
        var attrTypes = new Dictionary<ushort, string> { [42] = "DRVAL1" }.ToImmutableDictionary();
        var ds = S101Synth.Dataset("enc-1", ImmutableArray.Create(feature), featureTypes, attrTypes);

        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S101("enc-1", ds));
        var tool = new DescribeFeatureTool(catalog);

        var result = await tool.InvokeAsync(new DescribeFeatureRequest(new DatasetId("enc-1"), "12345"));

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal("DEPARE", value.FeatureTypeName);
        var attrs = value.Attributes.GetProperty("attributes");
        Assert.Equal(1, attrs.GetArrayLength());
        Assert.Equal("DRVAL1", attrs[0].GetProperty("acronym").GetString());
        Assert.Equal("5.0", attrs[0].GetProperty("value").GetString());
        Assert.Equal("Point", value.Attributes.GetProperty("geometryPrimitive").GetString());
    }

    [Fact]
    public async Task S101_describes_feature_by_composite_FRID()
    {
        var feature = S101Synth.Feature(rcid: 42, featureTypeCode: 73);
        var ds = S101Synth.Dataset("enc-2", ImmutableArray.Create(feature));

        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S101("enc-2", ds));
        var tool = new DescribeFeatureTool(catalog);

        var result = await tool.InvokeAsync(new DescribeFeatureRequest(new DatasetId("enc-2"), "100:42:1"));

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal("73", value.FeatureTypeName);
    }

    [Fact]
    public async Task S101_returns_FeatureNotFound_for_unknown_RCID()
    {
        var feature = S101Synth.Feature(rcid: 1, featureTypeCode: 30);
        var ds = S101Synth.Dataset("enc-3", ImmutableArray.Create(feature));

        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S101("enc-3", ds));
        var tool = new DescribeFeatureTool(catalog);

        var result = await tool.InvokeAsync(new DescribeFeatureRequest(new DatasetId("enc-3"), "99999"));

        Assert.True(result.TryGetError(out var error));
        Assert.IsType<FeatureNotFound>(error);
    }

    [Fact]
    public async Task S101_returns_FeatureNotFound_for_non_S101_RCNM_composite()
    {
        var feature = S101Synth.Feature(rcid: 1, featureTypeCode: 30);
        var ds = S101Synth.Dataset("enc-4", ImmutableArray.Create(feature));

        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S101("enc-4", ds));
        var tool = new DescribeFeatureTool(catalog);

        // RCNM 110 = spatial point record, not a feature record (100).
        var result = await tool.InvokeAsync(new DescribeFeatureRequest(new DatasetId("enc-4"), "110:1"));

        Assert.True(result.TryGetError(out var error));
        Assert.IsType<FeatureNotFound>(error);
    }

    [Fact]
    public async Task S101_falls_back_to_numeric_codes_when_no_catalogue_loaded()
    {
        var feature = S101Synth.Feature(rcid: 7, featureTypeCode: 30,
            attributes: new[] { ((ushort)42, "5.0") });
        var ds = S101Synth.Dataset("enc-5", ImmutableArray.Create(feature));

        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S101("enc-5", ds));
        var tool = new DescribeFeatureTool(catalog);

        var result = await tool.InvokeAsync(new DescribeFeatureRequest(new DatasetId("enc-5"), "7"));

        Assert.True(result.TryGetValue(out var value));
        // No feature catalogue → fall back to numeric feature type code string.
        Assert.Equal("30", value.FeatureTypeName);
        var attrs = value.Attributes.GetProperty("attributes");
        Assert.Equal(1, attrs.GetArrayLength());
        // No attribute catalogue → acronym is null but code is still present.
        Assert.Equal(JsonValueKind.Null, attrs[0].GetProperty("acronym").ValueKind);
        Assert.Equal(42, attrs[0].GetProperty("code").GetInt32());
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
