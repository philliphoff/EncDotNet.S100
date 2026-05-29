using System.Text.Json;
using EncDotNet.S100.DynamicSources.Ais;
using EncDotNet.S100.DynamicSources.Ais.Drivers.AisStreamIo;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.DynamicSources.Ais.Drivers.AisStreamIo.Tests;

public class AisStreamIoJsonTests
{
    [Fact]
    public void Subscribe_frame_uses_explicit_bbox_in_lat_lon_order()
    {
        var request = new AisSubscriptionRequest
        {
            Area = new BoundingBox(southLatitude: 30, westLongitude: -120, northLatitude: 40, eastLongitude: -110),
        };

        var frame = AisStreamIoJson.BuildSubscribeFrame("KEY", request);

        using var doc = JsonDocument.Parse(frame);
        var bboxes = doc.RootElement.GetProperty("BoundingBoxes");
        var pair = bboxes[0];
        Assert.Equal(30, pair[0][0].GetDouble());
        Assert.Equal(-120, pair[0][1].GetDouble());
        Assert.Equal(40, pair[1][0].GetDouble());
        Assert.Equal(-110, pair[1][1].GetDouble());
        Assert.Equal("KEY", doc.RootElement.GetProperty("APIKey").GetString());
    }

    [Fact]
    public void Subscribe_frame_uses_world_box_when_area_is_null()
    {
        var frame = AisStreamIoJson.BuildSubscribeFrame("KEY", new AisSubscriptionRequest());
        using var doc = JsonDocument.Parse(frame);
        var pair = doc.RootElement.GetProperty("BoundingBoxes")[0];
        Assert.Equal(-90, pair[0][0].GetDouble());
        Assert.Equal(-180, pair[0][1].GetDouble());
        Assert.Equal(90, pair[1][0].GetDouble());
        Assert.Equal(180, pair[1][1].GetDouble());
    }

    [Fact]
    public void Subscribe_frame_writes_filter_message_types_when_subset_selected()
    {
        var frame = AisStreamIoJson.BuildSubscribeFrame("KEY", new AisSubscriptionRequest
        {
            Include = AisMessageKinds.PositionReports,
        });
        using var doc = JsonDocument.Parse(frame);
        var filters = doc.RootElement.GetProperty("FilterMessageTypes");
        Assert.Equal(1, filters.GetArrayLength());
        Assert.Equal("PositionReport", filters[0].GetString());
    }

    [Fact]
    public void Subscribe_frame_writes_mmsi_filter_as_strings()
    {
        var frame = AisStreamIoJson.BuildSubscribeFrame("KEY", new AisSubscriptionRequest
        {
            Mmsis = new uint[] { 1, 2, 3 },
        });
        using var doc = JsonDocument.Parse(frame);
        var mmsis = doc.RootElement.GetProperty("FiltersShipMMSI");
        Assert.Equal(3, mmsis.GetArrayLength());
        Assert.Equal("1", mmsis[0].GetString());
        Assert.Equal("3", mmsis[2].GetString());
    }

    [Fact]
    public void Position_report_parses_with_sentinel_collapse()
    {
        const string json = """
        {
          "MessageType": "PositionReport",
          "MetaData": {
            "MMSI": 123456789,
            "ShipName": "EXAMPLE",
            "time_utc": "2026-05-29 14:23:01.234567 +0000 UTC"
          },
          "Message": {
            "PositionReport": {
              "Latitude": 37.81234,
              "Longitude": -122.43210,
              "Cog": 360.0,
              "TrueHeading": 511,
              "Sog": 102.3,
              "RateOfTurn": -128,
              "NavigationalStatus": 5
            }
          }
        }
        """;

        var msg = AisStreamIoJson.ParseInbound(json);
        var pr = Assert.IsType<AisPositionReport>(msg);
        Assert.Equal(123456789u, pr.Mmsi);
        Assert.Equal(37.81234, pr.Latitude);
        Assert.Equal(-122.4321, pr.Longitude);
        Assert.Null(pr.CourseOverGroundDeg);
        Assert.Null(pr.HeadingDeg);
        Assert.Null(pr.SpeedOverGroundKn);
        Assert.Null(pr.RateOfTurnDegPerMin);
        Assert.Equal(AisNavigationStatus.Moored, pr.NavigationStatus);
    }

    [Fact]
    public void Position_report_keeps_real_values_through_parse()
    {
        const string json = """
        {
          "MessageType": "PositionReport",
          "MetaData": { "MMSI": 1, "time_utc": "2026-01-01 00:00:00 +0000 UTC" },
          "Message": { "PositionReport": { "Latitude": 1.5, "Longitude": 2.5, "Cog": 90.5, "TrueHeading": 92, "Sog": 12.3, "NavigationalStatus": 0 } }
        }
        """;

        var pr = Assert.IsType<AisPositionReport>(AisStreamIoJson.ParseInbound(json));
        Assert.Equal(90.5, pr.CourseOverGroundDeg);
        Assert.Equal(92, pr.HeadingDeg);
        Assert.Equal(12.3, pr.SpeedOverGroundKn);
    }

    [Fact]
    public void Ship_static_data_folds_dimensions_into_length_beam_offsets()
    {
        const string json = """
        {
          "MessageType": "ShipStaticData",
          "MetaData": { "MMSI": 42, "time_utc": "2026-01-01 00:00:00 +0000 UTC", "ShipName": "META NAME" },
          "Message": {
            "ShipStaticData": {
              "ImoNumber": 9876543,
              "CallSign": "EXMPL ",
              "Name": "EXAMPLE@@@@",
              "Type": 70,
              "Dimension": { "A": 100, "B": 30, "C": 5, "D": 12 },
              "MaximumStaticDraught": 8.5,
              "Destination": "USOAK"
            }
          }
        }
        """;

        var sd = Assert.IsType<AisStaticVoyageData>(AisStreamIoJson.ParseInbound(json));
        Assert.Equal(42u, sd.Mmsi);
        Assert.Equal(9876543u, sd.ImoNumber);
        Assert.Equal("EXMPL", sd.CallSign);
        Assert.Equal("EXAMPLE", sd.VesselName);
        Assert.Equal(AisShipType.Cargo, sd.ShipType);
        Assert.Equal(AisShipTypeClass.Cargo, sd.ShipTypeClass);
        Assert.NotNull(sd.Dimensions);
        Assert.Equal(130, sd.Dimensions!.LengthMetres);
        Assert.Equal(17, sd.Dimensions.BeamMetres);
        Assert.Equal(100, sd.Dimensions.BowOffsetMetres);
        Assert.Equal(5, sd.Dimensions.PortOffsetMetres);
        Assert.Equal(8.5, sd.DraughtMetres);
        Assert.Equal("USOAK", sd.Destination);
    }

    [Fact]
    public void Ship_static_data_drops_zero_dimensions()
    {
        const string json = """
        {
          "MessageType": "ShipStaticData",
          "MetaData": { "MMSI": 42, "time_utc": "2026-01-01 00:00:00 +0000 UTC" },
          "Message": { "ShipStaticData": { "Type": 70, "Dimension": { "A": 0, "B": 0, "C": 0, "D": 0 } } }
        }
        """;
        var sd = Assert.IsType<AisStaticVoyageData>(AisStreamIoJson.ParseInbound(json));
        Assert.Null(sd.Dimensions);
    }

    [Fact]
    public void Unknown_message_types_yield_null()
    {
        Assert.Null(AisStreamIoJson.ParseInbound("""{"MessageType":"UnknownEverything"}"""));
    }

    [Fact]
    public void Malformed_json_yields_null()
    {
        Assert.Null(AisStreamIoJson.ParseInbound("not json"));
    }

    [Fact]
    public void Redact_api_key_replaces_every_occurrence()
    {
        var frame = """{"APIKey":"super-secret","BoundingBoxes":[]}""";
        var redacted = AisStreamIoJson.RedactApiKey(frame, "super-secret");
        Assert.DoesNotContain("super-secret", redacted, StringComparison.Ordinal);
        Assert.Contains("***REDACTED***", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void Timestamp_parser_handles_go_format_with_zone_label()
    {
        Assert.True(AisStreamIoJson.TryParseAisStreamTimestamp(
            "2026-05-29 14:23:01.234567 +0000 UTC", out var value));
        Assert.Equal(new DateTimeOffset(2026, 5, 29, 14, 23, 1, TimeSpan.Zero).AddTicks(2345670),
            value);
    }
}
