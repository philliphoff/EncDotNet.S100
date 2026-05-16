using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using EncDotNet.S100.Core;
using EncDotNet.S100.Mcp.Tools.Catalog;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Mcp.Tools.Tests;

/// <summary>
/// Golden-key assertions on the JSON shape that MCP tool result records
/// produce on the wire. These tests are deliberately about
/// <em>property names</em>, not values — they exist so the agent-facing
/// contract (camelCase keys with units in the suffix) cannot drift
/// silently.
/// </summary>
public class SerializationContractTests
{
    private static JsonSerializerOptions CreateWireOptions()
    {
        // Mirrors the options shape in
        // S100McpServerToolFactory.JsonOptions. The factory uses
        // JsonSerializerDefaults.Web (which sets camelCase naming) and
        // configures polymorphism on SampledValue with a "$kind"
        // discriminator. We replicate the full set of SampledValue
        // variants here so each one's JSON shape can be asserted.
        return new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver
            {
                Modifiers =
                {
                    static typeInfo =>
                    {
                        if (typeInfo.Type == typeof(SampledValue))
                        {
                            typeInfo.PolymorphismOptions = new JsonPolymorphismOptions
                            {
                                TypeDiscriminatorPropertyName = "$kind",
                                DerivedTypes =
                                {
                                    new JsonDerivedType(typeof(DepthSample), "depth"),
                                    new JsonDerivedType(typeof(WaterLevelSample), "waterLevel"),
                                    new JsonDerivedType(typeof(WaterLevelStationSample), "waterLevelStation"),
                                    new JsonDerivedType(typeof(SurfaceCurrentSample), "surfaceCurrent"),
                                    new JsonDerivedType(typeof(SurfaceCurrentStationSample), "surfaceCurrentStation"),
                                },
                            };
                        }
                    },
                },
            },
        };
    }

    [Fact]
    public void ListDatasetsResult_uses_camelCase_and_unambiguous_bbox_keys()
    {
        var result = new ListDatasetsResult(
            ImmutableArray.Create(
                new DatasetSummary(
                    new DatasetId("ds-1"),
                    new SpecRef("S-104", new SpecVersion(1, 0, 0)),
                    new BoundingBox(southLatitude: 47.0, westLongitude: -123.0, northLatitude: 48.0, eastLongitude: -122.0),
                    new TimeRange(
                        new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
                        new DateTimeOffset(2025, 1, 2, 0, 0, 0, TimeSpan.Zero)))),
            Page: 0,
            PageSize: 50,
            TotalCount: 1,
            HasMore: false);

        var json = JsonSerializer.Serialize(result, CreateWireOptions());

        // Outer page envelope.
        Assert.Contains("\"datasets\":", json);
        Assert.Contains("\"page\":0", json);
        Assert.Contains("\"pageSize\":50", json);
        Assert.Contains("\"totalCount\":1", json);
        Assert.Contains("\"hasMore\":false", json);

        // Per-dataset summary.
        Assert.Contains("\"id\":", json);
        Assert.Contains("\"spec\":", json);
        Assert.Contains("\"bounds\":", json);
        Assert.Contains("\"timeRange\":", json);

        // Bounding-box keys must be the unambiguous four-edge form, in
        // camelCase, never a bare lat/lon pair. This is the single
        // assertion that the agent-facing dogfooding bug surfaced.
        Assert.Contains("\"southLatitude\":47", json);
        Assert.Contains("\"westLongitude\":-123", json);
        Assert.Contains("\"northLatitude\":48", json);
        Assert.Contains("\"eastLongitude\":-122", json);

        // SpecRef projects onto camelCase too.
        Assert.Contains("\"name\":\"S-104\"", json);
        Assert.Contains("\"edition\":", json);
        Assert.Contains("\"major\":1", json);

        // TimeRange.
        Assert.Contains("\"start\":", json);
        Assert.Contains("\"end\":", json);
    }

    [Fact]
    public void DepthSample_serialises_with_kind_discriminator_and_unit_suffix()
    {
        var result = new SampleCoverageResult(
            new DatasetId("ds-1"),
            Latitude: 47.6,
            Longitude: -122.3,
            Value: new DepthSample(DepthMeters: 12.5, UncertaintyMeters: 0.25));

        var json = JsonSerializer.Serialize(result, CreateWireOptions());

        Assert.Contains("\"datasetId\":", json);
        Assert.Contains("\"latitude\":47.6", json);
        Assert.Contains("\"longitude\":-122.3", json);
        Assert.Contains("\"value\":", json);
        Assert.Contains("\"$kind\":\"depth\"", json);
        Assert.Contains("\"depthMeters\":12.5", json);
        Assert.Contains("\"uncertaintyMeters\":0.25", json);
    }

    [Fact]
    public void WaterLevelSample_emits_camelCase_with_grid_and_time_fields()
    {
        var result = new SampleCoverageResult(
            new DatasetId("ds-2"),
            Latitude: 47.6,
            Longitude: -122.3,
            Value: new WaterLevelSample(
                WaterLevelHeight: 1.42,
                Trend: "increasing",
                SampleTime: new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc),
                RequestedTime: null,
                Row: 10,
                Column: 20,
                CellCentreLatitude: 47.61,
                CellCentreLongitude: -122.31));

        var json = JsonSerializer.Serialize(result, CreateWireOptions());

        Assert.Contains("\"$kind\":\"waterLevel\"", json);
        Assert.Contains("\"waterLevelHeight\":1.42", json);
        Assert.Contains("\"trend\":\"increasing\"", json);
        Assert.Contains("\"sampleTime\":", json);
        Assert.Contains("\"row\":10", json);
        Assert.Contains("\"column\":20", json);
        Assert.Contains("\"cellCentreLatitude\":47.61", json);
        Assert.Contains("\"cellCentreLongitude\":-122.31", json);
        // RequestedTime null is omitted by the WhenWritingNull policy.
        Assert.DoesNotContain("\"requestedTime\":null", json);
    }

    [Fact]
    public void SurfaceCurrentSample_carries_dual_unit_speed_and_bearing_keys()
    {
        var result = new SampleCoverageResult(
            new DatasetId("ds-3"),
            Latitude: 47.6,
            Longitude: -122.3,
            Value: new SurfaceCurrentSample(
                SpeedMetresPerSecond: 1.5,
                SpeedKnots: 2.9157667386609,
                DirectionDegreesTrue: 270.0,
                SampleTime: new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc),
                RequestedTime: null,
                Row: 5,
                Column: 7,
                CellCentreLatitude: 47.6,
                CellCentreLongitude: -122.3));

        var json = JsonSerializer.Serialize(result, CreateWireOptions());

        Assert.Contains("\"$kind\":\"surfaceCurrent\"", json);
        Assert.Contains("\"speedMetresPerSecond\":1.5", json);
        Assert.Contains("\"speedKnots\":", json);
        Assert.Contains("\"directionDegreesTrue\":270", json);
        Assert.Contains("\"cellCentreLatitude\":", json);
        Assert.Contains("\"cellCentreLongitude\":", json);
    }

    [Fact]
    public void StationSamples_emit_station_distance_and_position_keys()
    {
        var wls = new SampleCoverageResult(
            new DatasetId("ds-4"),
            Latitude: 47.6,
            Longitude: -122.3,
            Value: new WaterLevelStationSample(
                StationId: "STA-1",
                StationDistanceMetres: 1234.5,
                WaterLevelHeight: 1.0,
                Trend: "steady",
                SampleTime: new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc),
                RequestedTime: null,
                StationLatitude: 47.5,
                StationLongitude: -122.2));

        var json = JsonSerializer.Serialize(wls, CreateWireOptions());
        Assert.Contains("\"$kind\":\"waterLevelStation\"", json);
        Assert.Contains("\"stationId\":\"STA-1\"", json);
        Assert.Contains("\"stationDistanceMetres\":1234.5", json);
        Assert.Contains("\"stationLatitude\":47.5", json);
        Assert.Contains("\"stationLongitude\":-122.2", json);

        var scs = new SampleCoverageResult(
            new DatasetId("ds-5"),
            Latitude: 47.6,
            Longitude: -122.3,
            Value: new SurfaceCurrentStationSample(
                StationId: "STA-2",
                StationDistanceMetres: 5000.0,
                SpeedMetresPerSecond: 2.0,
                SpeedKnots: 3.88,
                DirectionDegreesTrue: 180.0,
                SampleTime: new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc),
                RequestedTime: null,
                StationLatitude: 47.55,
                StationLongitude: -122.25));

        var json2 = JsonSerializer.Serialize(scs, CreateWireOptions());
        Assert.Contains("\"$kind\":\"surfaceCurrentStation\"", json2);
        Assert.Contains("\"speedMetresPerSecond\":2", json2);
        Assert.Contains("\"directionDegreesTrue\":180", json2);
        Assert.Contains("\"stationDistanceMetres\":5000", json2);
    }
}
