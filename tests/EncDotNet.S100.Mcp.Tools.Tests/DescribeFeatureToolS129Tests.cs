using System.Text.Json;
using EncDotNet.S100.Mcp.Tools.Catalog;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;

namespace EncDotNet.S100.Mcp.Tools.Tests;

public class DescribeFeatureToolS129Tests
{
    [Fact]
    public async Task PlanId_ResolvesToPlanHeader_WithExternalReferencesAndDraught()
    {
        var dataset = S129Synth.Dataset(
            S129Synth.Plan(id: "PLAN_1"),
            S129Synth.PlanArea(),
            S129Synth.ControlPoint(id: "CP_01"),
            S129Synth.ControlPoint(id: "CP_02", expectedPassingTime: "2024-04-17T22:30:00Z"),
            S129Synth.NonNavigableArea(id: "NN_1"));

        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S129("ds", dataset));
        var tool = new DescribeFeatureTool(catalog);

        var result = await tool.InvokeAsync(new DescribeFeatureRequest(new DatasetId("ds"), "PLAN_1"));
        Assert.True(result.TryGetValue(out var value));
        Assert.Equal("UnderKeelClearancePlan", value.FeatureTypeName);
        Assert.Equal("S-129", value.Spec.Name);
        Assert.Empty(value.References);

        var attrs = value.Attributes;
        Assert.Equal("PLAN_1", attrs.GetProperty("id").GetString());
        Assert.Equal(12.2, attrs.GetProperty("maximumDraughtMetres").GetDouble());
        Assert.Equal("passage planning", attrs.GetProperty("underKeelClearancePurpose").GetString());

        var refs = attrs.GetProperty("externalReferences");
        Assert.Equal(JsonValueKind.Array, refs.ValueKind);
        Assert.Equal(2, refs.GetArrayLength());
        // First reference: vessel.
        Assert.Equal("vessel", refs[0].GetProperty("kind").GetString());
        Assert.Equal("9800738", refs[0].GetProperty("identifier").GetString());
        // Second reference: S-421 route with version.
        Assert.Equal("S-421 route", refs[1].GetProperty("kind").GetString());
        Assert.Equal("Test Route", refs[1].GetProperty("identifier").GetString());
        Assert.Equal("1", refs[1].GetProperty("version").GetString());

        var counts = attrs.GetProperty("counts");
        Assert.Equal(2, counts.GetProperty("controlPoints").GetInt32());
        Assert.Equal(1, counts.GetProperty("nonNavigableAreas").GetInt32());
        Assert.Equal(0, counts.GetProperty("almostNonNavigableAreas").GetInt32());

        var range = attrs.GetProperty("fixedTimeRange");
        Assert.Equal("2024-04-17T21:41:00+00:00", range.GetProperty("start").GetString());
        Assert.Equal("2024-04-18T01:13:00+00:00", range.GetProperty("end").GetString());
    }

    [Fact]
    public async Task PlanAreaId_ResolvesToSurface_WithExteriorRing()
    {
        var dataset = S129Synth.Dataset(
            S129Synth.Plan(),
            S129Synth.PlanArea(id: "PLAN_AREA_1"));
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S129("ds", dataset));
        var tool = new DescribeFeatureTool(catalog);

        var result = await tool.InvokeAsync(new DescribeFeatureRequest(new DatasetId("ds"), "PLAN_AREA_1"));
        Assert.True(result.TryGetValue(out var value));
        Assert.Equal("UnderKeelClearancePlanArea", value.FeatureTypeName);

        var attrs = value.Attributes;
        Assert.Equal("Surface", attrs.GetProperty("geometryType").GetString());
        var ring = attrs.GetProperty("exteriorRing");
        Assert.True(ring.GetArrayLength() >= 3);
        Assert.Equal(47.0, ring[0].GetProperty("latitude").GetDouble());
        Assert.Equal(-122.0, ring[0].GetProperty("longitude").GetDouble());
    }

    [Fact]
    public async Task ControlPointId_ResolvesToCP_WithUkcMargin()
    {
        var dataset = S129Synth.Dataset(
            S129Synth.Plan(),
            S129Synth.PlanArea(),
            S129Synth.ControlPoint(id: "CP_01", latitude: 47.15, longitude: -121.85,
                expectedPassingSpeed: 6.0, distanceAboveUkcLimit: 0.113));

        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S129("ds", dataset));
        var tool = new DescribeFeatureTool(catalog);

        var result = await tool.InvokeAsync(new DescribeFeatureRequest(new DatasetId("ds"), "CP_01"));
        Assert.True(result.TryGetValue(out var value));
        Assert.Equal("UnderKeelClearanceControlPoint", value.FeatureTypeName);

        var attrs = value.Attributes;
        var pos = attrs.GetProperty("position");
        Assert.Equal(47.15, pos.GetProperty("latitude").GetDouble(), 6);
        Assert.Equal(-121.85, pos.GetProperty("longitude").GetDouble(), 6);
        Assert.Equal("2024-04-17T22:00:00+00:00", attrs.GetProperty("expectedPassingTime").GetString());
        Assert.Equal(6.0, attrs.GetProperty("expectedPassingSpeedKnots").GetDouble());
        Assert.Equal(0.113, attrs.GetProperty("distanceAboveUkcLimitMetres").GetDouble(), 6);
    }

    [Fact]
    public async Task NonNavigableAreaId_ResolvesToSurface_WithScaleMinimum()
    {
        var dataset = S129Synth.Dataset(
            S129Synth.Plan(),
            S129Synth.PlanArea(),
            S129Synth.NonNavigableArea(id: "NN_1", scaleMinimum: 50000));

        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S129("ds", dataset));
        var tool = new DescribeFeatureTool(catalog);

        var result = await tool.InvokeAsync(new DescribeFeatureRequest(new DatasetId("ds"), "NN_1"));
        Assert.True(result.TryGetValue(out var value));
        Assert.Equal("UnderKeelClearanceNonNavigableArea", value.FeatureTypeName);
        var attrs = value.Attributes;
        Assert.Equal(50000, attrs.GetProperty("scaleMinimum").GetInt32());
        Assert.Equal("Surface", attrs.GetProperty("geometryType").GetString());
        Assert.True(attrs.GetProperty("exteriorRing").GetArrayLength() >= 3);
    }

    [Fact]
    public async Task UnknownId_ReturnsFeatureNotFound()
    {
        var dataset = S129Synth.Dataset(S129Synth.Plan(), S129Synth.PlanArea());
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S129("ds", dataset));
        var tool = new DescribeFeatureTool(catalog);

        var result = await tool.InvokeAsync(new DescribeFeatureRequest(new DatasetId("ds"), "does-not-exist"));
        Assert.True(result.TryGetError(out var err));
        Assert.IsType<FeatureNotFound>(err);
    }

    [Fact]
    public async Task SpecMismatch_S129SpecWithS101Payload_ReturnsSpecNotSupported()
    {
        var catalog = new FakeDatasetCatalog();
        var s101 = LoadedDatasetFactory.S101("ds");
        catalog.Add(new LoadedDataset(
            s101.Id,
            LoadedDatasetFactory.S129Spec,
            s101.Bounds,
            null,
            s101.Data));
        var tool = new DescribeFeatureTool(catalog);

        var result = await tool.InvokeAsync(new DescribeFeatureRequest(new DatasetId("ds"), "PLAN_1"));
        Assert.True(result.TryGetError(out var err));
        var unsupported = Assert.IsType<SpecNotSupportedForTool>(err);
        Assert.Equal("S-129", unsupported.Spec.Name);
    }

    [Fact]
    public async Task GeometrylessPlanArea_StillResolves_WithEmptyRings()
    {
        var dataset = S129Synth.Dataset(
            S129Synth.Plan(),
            S129Synth.GeometrylessPlanArea(id: "PLAN_AREA_1"));

        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S129("ds", dataset));
        var tool = new DescribeFeatureTool(catalog);

        var result = await tool.InvokeAsync(new DescribeFeatureRequest(new DatasetId("ds"), "PLAN_AREA_1"));
        Assert.True(result.TryGetValue(out var value));
        Assert.Equal("UnderKeelClearancePlanArea", value.FeatureTypeName);
        var attrs = value.Attributes;
        Assert.Equal("None", attrs.GetProperty("geometryType").GetString());
        Assert.Equal(0, attrs.GetProperty("exteriorRing").GetArrayLength());
        Assert.Equal(0, attrs.GetProperty("interiorRings").GetArrayLength());
    }
}
