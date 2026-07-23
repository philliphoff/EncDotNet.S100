using System.Text.Json;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;

namespace EncDotNet.S100.Mcp.Tools.Tests;

/// <summary>
/// Tests for the S-101 <c>describe_feature</c> describer's surfacing of
/// MultiPoint sounding depths (issue #311): the Z ordinate of each point in
/// a <c>Sounding</c> feature must be read, scaled by the dataset's Z
/// multiplication factor, and returned as a <c>depths</c> array aligned
/// one-to-one with the geometry coordinates.
/// </summary>
public class DescribeFeatureToolS101Tests
{
    [Fact]
    public async Task Sounding_surfaces_per_point_depths_aligned_with_coordinates()
    {
        var dataset = S101Synth.DatasetWithSounding(
            "enc-with-soundings",
            new (double Lat, double Lon, double Depth)[]
            {
                (50.7699, -1.1396, 11.0),
                (50.7709, -1.1367, 6.4),
                (50.7720, -1.1340, 18.3),
            });

        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S101("enc", dataset));
        var tool = new DescribeFeatureTool(catalog);

        var result = await tool.InvokeAsync(
            new DescribeFeatureRequest(new DatasetId("enc"), "808"));

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal("Sounding", value.FeatureTypeName);

        var geometry = value.Attributes.GetProperty("geometry");
        var coordinates = geometry.GetProperty("coordinates");
        Assert.Equal(3, coordinates.GetArrayLength());

        var depths = geometry.GetProperty("depths");
        Assert.Equal(JsonValueKind.Array, depths.ValueKind);
        Assert.Equal(3, depths.GetArrayLength());
        Assert.Equal(11.0, depths[0].GetDouble(), 6);
        Assert.Equal(6.4, depths[1].GetDouble(), 6);
        Assert.Equal(18.3, depths[2].GetDouble(), 6);

        // The depth unit is stated explicitly so a client never infers it
        // (issue #316).
        Assert.Equal("metres", geometry.GetProperty("depthUnit").GetString());
    }

    [Fact]
    public async Task Non_multipoint_feature_omits_depths()
    {
        var dataset = S101Synth.DatasetWithPointFeatures(
            "enc-points",
            new (uint, ushort, double, double)[] { (1u, 7, 50.0, -1.0) });

        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S101("enc", dataset));
        var tool = new DescribeFeatureTool(catalog);

        var result = await tool.InvokeAsync(
            new DescribeFeatureRequest(new DatasetId("enc"), "1"));

        Assert.True(result.TryGetValue(out var value));
        var depths = value.Attributes.GetProperty("geometry").GetProperty("depths");
        Assert.Equal(JsonValueKind.Null, depths.ValueKind);

        // With no aligned depths the unit is null, not a misleading "metres".
        var depthUnit = value.Attributes.GetProperty("geometry").GetProperty("depthUnit");
        Assert.Equal(JsonValueKind.Null, depthUnit.ValueKind);
    }

    [Fact]
    public async Task Information_association_inlines_target_record_text()
    {
        var dataset = S101Synth.DatasetWithAssociatedInformation(
            "enc-with-info",
            text: "Caution: strong tidal streams.");

        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S101("enc", dataset));
        var tool = new DescribeFeatureTool(catalog);

        var result = await tool.InvokeAsync(
            new DescribeFeatureRequest(new DatasetId("enc"), "700"));

        Assert.True(result.TryGetValue(out var value));

        var infoAssociations = value.Attributes.GetProperty("informationAssociations");
        Assert.Equal(1, infoAssociations.GetArrayLength());

        var association = infoAssociations[0];
        Assert.Equal("AdditionalInformation", association.GetProperty("acronym").GetString());

        var target = association.GetProperty("target");
        Assert.Equal(JsonValueKind.Object, target.ValueKind);
        Assert.Equal("NauticalInformation", target.GetProperty("informationTypeAcronym").GetString());

        var targetAttributes = target.GetProperty("attributes");
        Assert.Equal(1, targetAttributes.GetArrayLength());
        Assert.Equal("information", targetAttributes[0].GetProperty("acronym").GetString());
        Assert.Equal("Caution: strong tidal streams.", targetAttributes[0].GetProperty("value").GetString());
    }

    [Fact]
    public async Task Information_association_target_is_null_when_record_absent()
    {
        // Feature has no information associations at all → none to dereference;
        // the array is present and empty.
        var dataset = S101Synth.DatasetWithPointFeatures(
            "enc-points",
            new (uint, ushort, double, double)[] { (1u, 7, 50.0, -1.0) });

        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S101("enc", dataset));
        var tool = new DescribeFeatureTool(catalog);

        var result = await tool.InvokeAsync(
            new DescribeFeatureRequest(new DatasetId("enc"), "1"));

        Assert.True(result.TryGetValue(out var value));
        var infoAssociations = value.Attributes.GetProperty("informationAssociations");
        Assert.Equal(JsonValueKind.Array, infoAssociations.ValueKind);
        Assert.Equal(0, infoAssociations.GetArrayLength());
    }

    [Fact]
    public async Task Depth_valued_attribute_carries_metres_unit()
    {
        // DredgedArea.depthRangeMinimumValue is metres by definition in
        // S-101; the describer must annotate it from the Feature Catalogue's
        // uom rather than leaving it a bare string (issue #334).
        var dataset = S101Synth.DatasetWithAttributedFeature(
            "enc-dredged",
            featureRcid: 4242,
            featureTypeCode: 42,
            featureTypeName: "DredgedArea",
            attributes: new (ushort, string, string)[]
            {
                (17, "depthRangeMinimumValue", "12.5"),
            });

        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S101("enc", dataset));
        var tool = new DescribeFeatureTool(catalog);

        var result = await tool.InvokeAsync(
            new DescribeFeatureRequest(new DatasetId("enc"), "4242"));

        Assert.True(result.TryGetValue(out var value));

        var attributes = value.Attributes.GetProperty("attributes");
        Assert.Equal(1, attributes.GetArrayLength());
        var attr = attributes[0];
        Assert.Equal("depthRangeMinimumValue", attr.GetProperty("acronym").GetString());
        Assert.Equal("12.5", attr.GetProperty("value").GetString());
        Assert.Equal("m", attr.GetProperty("unit").GetString());
        Assert.Equal("metre", attr.GetProperty("unitName").GetString());
    }

    [Fact]
    public async Task Unitless_attribute_omits_unit()
    {
        // categoryOfDredgedArea is an enumeration — no uom — so the
        // describer must not invent a unit.
        var dataset = S101Synth.DatasetWithAttributedFeature(
            "enc-dredged",
            featureRcid: 4242,
            featureTypeCode: 42,
            featureTypeName: "DredgedArea",
            attributes: new (ushort, string, string)[]
            {
                (33, "categoryOfDredgedArea", "1"),
            });

        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S101("enc", dataset));
        var tool = new DescribeFeatureTool(catalog);

        var result = await tool.InvokeAsync(
            new DescribeFeatureRequest(new DatasetId("enc"), "4242"));

        Assert.True(result.TryGetValue(out var value));
        var attr = value.Attributes.GetProperty("attributes")[0];
        Assert.False(attr.TryGetProperty("unit", out _));
    }

    [Fact]
    public async Task Sounding_geometry_depths_carry_metres_unit()
    {
        var dataset = S101Synth.DatasetWithSounding(
            "enc-with-soundings",
            new (double Lat, double Lon, double Depth)[]
            {
                (50.7699, -1.1396, 11.0),
            });

        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S101("enc", dataset));
        var tool = new DescribeFeatureTool(catalog);

        var result = await tool.InvokeAsync(
            new DescribeFeatureRequest(new DatasetId("enc"), "808"));

        Assert.True(result.TryGetValue(out var value));
        var geometry = value.Attributes.GetProperty("geometry");
        Assert.Equal("metres", geometry.GetProperty("depthUnit").GetString());
    }
}
