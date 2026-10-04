using EncDotNet.S100.Features;

namespace EncDotNet.S100.Core.Tests;

/// <summary>
/// Tests for <see cref="GmlCoordinateParser"/>, focusing on the axis-order
/// producer-bug compensation (S-100 Part 10b §6.2 mandates latitude-first, but
/// the US NWS S-411 sea-ice product encodes longitude-first). See issue #413.
/// </summary>
public class GmlCoordinateParserTests
{
    [Fact]
    public void ParsePos_conformant_lat_first_is_unchanged()
    {
        var coord = GmlCoordinateParser.ParsePos("70.7762 -155.86");

        Assert.NotNull(coord);
        Assert.Equal(70.7762, coord!.Value.Latitude, 6);
        Assert.Equal(-155.86, coord.Value.Longitude, 6);
    }

    [Fact]
    public void ParsePos_lon_first_is_swapped_when_lat_slot_exceeds_90()
    {
        // NWS S-411 encoding: "<lon> <lat>" with lon in the 0–360 convention.
        var coord = GmlCoordinateParser.ParsePos("205.8599 70.7762");

        Assert.NotNull(coord);
        Assert.Equal(70.7762, coord!.Value.Latitude, 6);
        Assert.Equal(205.8599, coord.Value.Longitude, 6);
    }

    [Fact]
    public void ParsePos_high_longitude_within_lat_bound_is_not_swapped()
    {
        // Genuine lat-first pair whose longitude (95°) is > 90 but sits in the
        // second slot must NOT be swapped; the first ordinate is a valid latitude.
        var coord = GmlCoordinateParser.ParsePos("60.0 95.0");

        Assert.NotNull(coord);
        Assert.Equal(60.0, coord!.Value.Latitude, 6);
        Assert.Equal(95.0, coord.Value.Longitude, 6);
    }

    [Fact]
    public void ParsePosList_lon_first_stream_is_swapped()
    {
        var coords = GmlCoordinateParser.ParsePosList("205.86 70.77 210.00 71.50 225.00 66.00");

        Assert.Equal(3, coords.Count);
        Assert.All(coords, c => Assert.InRange(c.Latitude, -90.0, 90.0));
        Assert.Equal(205.86, coords[0].Longitude, 6);
        Assert.Equal(70.77, coords[0].Latitude, 6);
        Assert.Equal(225.00, coords[2].Longitude, 6);
        Assert.Equal(66.00, coords[2].Latitude, 6);
    }

    [Fact]
    public void ParsePosList_conformant_lat_first_stream_is_unchanged()
    {
        var coords = GmlCoordinateParser.ParsePosList("62.1414 -67.3632 66.3292 -65.5028");

        Assert.Equal(2, coords.Count);
        Assert.Equal(62.1414, coords[0].Latitude, 6);
        Assert.Equal(-67.3632, coords[0].Longitude, 6);
        Assert.Equal(66.3292, coords[1].Latitude, 6);
        Assert.Equal(-65.5028, coords[1].Longitude, 6);
    }

    [Fact]
    public void ParsePos_negative_lon_first_is_swapped()
    {
        // Longitude-first with a negative longitude beyond ±90° also swaps.
        var coord = GmlCoordinateParser.ParsePos("-155.0 70.0");

        Assert.NotNull(coord);
        Assert.Equal(70.0, coord!.Value.Latitude, 6);
        Assert.Equal(-155.0, coord.Value.Longitude, 6);
    }

    [Fact]
    public void ParsePosList_list_with_one_lon_first_pair_is_read_lon_first_throughout()
    {
        // NIC Arctic S-411: a ring straddling 90°E. "45 81" alone looks
        // latitude first, but "120 80" shows the list is longitude first.
        var coords = GmlCoordinateParser.ParsePosList("45 81 120 80");

        Assert.Equal(81, coords[0].Latitude, 6);
        Assert.Equal(45, coords[0].Longitude, 6);
        Assert.Equal(80, coords[1].Latitude, 6);
        Assert.Equal(120, coords[1].Longitude, 6);
    }

    [Fact]
    public void ParsePosList_longitude_first_order_swaps_every_pair()
    {
        // A ring lying wholly within ±90° longitude in a longitude-first
        // dataset (issue #760).
        var coords = GmlCoordinateParser.ParsePosList("30 75 60 76", GmlAxisOrder.LongitudeFirst);

        Assert.Equal(75, coords[0].Latitude, 6);
        Assert.Equal(30, coords[0].Longitude, 6);
        Assert.Equal(76, coords[1].Latitude, 6);
        Assert.Equal(60, coords[1].Longitude, 6);
    }

    [Fact]
    public void DetectAxisOrder_finds_a_lon_first_position_anywhere_in_the_dataset()
    {
        var root = System.Xml.Linq.XElement.Parse("""
            <d xmlns:gml="http://www.opengis.net/gml/3.2">
              <gml:posList>30 75 60 76</gml:posList>
              <gml:posList>200 70 210 71</gml:posList>
            </d>
            """);

        Assert.Equal(GmlAxisOrder.LongitudeFirst, GmlCoordinateParser.DetectAxisOrder(root));
    }

    [Fact]
    public void DetectAxisOrder_is_auto_for_conformant_lat_first_data()
    {
        var root = System.Xml.Linq.XElement.Parse("""
            <d xmlns:gml="http://www.opengis.net/gml/3.2">
              <gml:posList>62.1 -167.3 66.3 -165.5</gml:posList>
              <gml:pos>60 95</gml:pos>
            </d>
            """);

        Assert.Equal(GmlAxisOrder.Auto, GmlCoordinateParser.DetectAxisOrder(root));
    }
}
