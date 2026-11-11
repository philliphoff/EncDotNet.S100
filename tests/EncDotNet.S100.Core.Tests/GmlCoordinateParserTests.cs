using System.Xml.Linq;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Features;

namespace EncDotNet.S100.Core.Tests;

/// <summary>
/// Tests for <see cref="GmlCoordinateParser"/>'s axis-order handling. S-100
/// Part 10b §6.2 mandates latitude first; producers that write longitude first
/// (the UK S-122 trial, US NWS / NIC S-411 sea ice) are caught by the
/// dataset-level <see cref="GmlCoordinateParser.AssessAxisOrder"/> (issues
/// #413, #760, #650), never per position.
/// </summary>
public class GmlCoordinateParserTests
{
    [Fact]
    public void ParsePos_reads_latitude_first_by_default()
    {
        var p = GmlCoordinateParser.ParsePos("62.1 -167.3");

        Assert.NotNull(p);
        Assert.Equal(62.1, p!.Value.Latitude, 6);
        Assert.Equal(-167.3, p.Value.Longitude, 6);
    }

    [Fact]
    public void ParsePos_does_not_swap_a_position_on_its_own()
    {
        // The pre-#650 parser swapped any pair whose first ordinate exceeded
        // ±90°. That is now the dataset-level check's call.
        var p = GmlCoordinateParser.ParsePos("-167.3 62.1");

        Assert.NotNull(p);
        Assert.Equal(-167.3, p!.Value.Latitude, 6);
        Assert.Equal(62.1, p.Value.Longitude, 6);
    }

    [Fact]
    public void ParsePos_longitude_first_order_swaps()
    {
        var p = GmlCoordinateParser.ParsePos("-1.3 50.8", GmlAxisOrder.LongitudeFirst);

        Assert.NotNull(p);
        Assert.Equal(50.8, p!.Value.Latitude, 6);
        Assert.Equal(-1.3, p.Value.Longitude, 6);
    }

    [Fact]
    public void ParsePosList_conformant_lat_first_stream_is_unchanged()
    {
        var coords = GmlCoordinateParser.ParsePosList("62.1 -167.3 66.3 -165.5");

        Assert.Equal(2, coords.Count);
        Assert.Equal(62.1, coords[0].Latitude, 6);
        Assert.Equal(-167.3, coords[0].Longitude, 6);
        Assert.Equal(66.3, coords[1].Latitude, 6);
        Assert.Equal(-165.5, coords[1].Longitude, 6);
    }

    [Fact]
    public void ParsePosList_longitude_first_order_swaps_every_pair()
    {
        var coords = GmlCoordinateParser.ParsePosList("30 75 60 76", GmlAxisOrder.LongitudeFirst);

        Assert.Equal(2, coords.Count);
        Assert.Equal(75, coords[0].Latitude, 6);
        Assert.Equal(30, coords[0].Longitude, 6);
        Assert.Equal(76, coords[1].Latitude, 6);
        Assert.Equal(60, coords[1].Longitude, 6);
    }

    [Fact]
    public void AssessAxisOrder_finds_a_lon_first_position_anywhere_in_the_dataset()
    {
        // A ring east of Greenwich (longitudes within ±90°) is read
        // longitude first because another ring proves the dataset is (#760).
        var root = XElement.Parse("""
            <d xmlns:gml="http://www.opengis.net/gml/3.2">
              <gml:posList>30 75 60 76</gml:posList>
              <gml:posList>200 70 210 71</gml:posList>
            </d>
            """);

        var (order, assessment) = GmlCoordinateParser.AssessAxisOrder(root);

        Assert.Equal(GmlAxisOrder.LongitudeFirst, order);
        Assert.Equal(AxisOrderOutcome.Swapped, assessment!.Outcome);
        Assert.Equal(AxisOrderEvidence.PhysicalBounds, assessment.Evidence);
    }

    [Fact]
    public void AssessAxisOrder_conformant_data_without_envelope_is_unverified_and_lat_first()
    {
        var root = XElement.Parse("""
            <d xmlns:gml="http://www.opengis.net/gml/3.2">
              <gml:posList>62.1 -167.3 66.3 -165.5</gml:posList>
              <gml:pos>60 95</gml:pos>
            </d>
            """);

        var (order, assessment) = GmlCoordinateParser.AssessAxisOrder(root);

        Assert.Equal(GmlAxisOrder.LatitudeFirst, order);
        Assert.Equal(AxisOrderOutcome.Unverified, assessment!.Outcome);
        Assert.Equal(3, assessment.PositionCount);
    }

    [Fact]
    public void AssessAxisOrder_uses_the_envelope_to_catch_a_swap_within_physical_bounds()
    {
        // The UK S-122 trial shape: lat-lon envelope, lon-lat posList, and
        // every ordinate within ±90° so only the envelope can tell.
        var root = XElement.Parse("""
            <d xmlns:gml="http://www.opengis.net/gml/3.2">
              <gml:boundedBy>
                <gml:Envelope srsName="EPSG:4326">
                  <gml:lowerCorner>50.0 -2.5</gml:lowerCorner>
                  <gml:upperCorner>51.0 0.0</gml:upperCorner>
                </gml:Envelope>
              </gml:boundedBy>
              <gml:posList>-2.5 50.95 -2.5 50.3 0.0 50.3 0.0 50.95</gml:posList>
            </d>
            """);

        var (order, assessment) = GmlCoordinateParser.AssessAxisOrder(root);

        Assert.Equal(GmlAxisOrder.LongitudeFirst, order);
        Assert.Equal(AxisOrderEvidence.DeclaredExtent, assessment!.Evidence);
        Assert.Equal(4, assessment.FitSwapped);
        Assert.Equal(AxisOrderOutcome.Swapped, assessment.Outcome);
        Assert.True(assessment.Normalized);
    }

    [Fact]
    public void AssessAxisOrder_conformant_data_fitting_its_envelope_is_verified()
    {
        var root = XElement.Parse("""
            <d xmlns:gml="http://www.opengis.net/gml/3.2">
              <gml:boundedBy>
                <gml:Envelope srsName="EPSG:4326">
                  <gml:lowerCorner>50.0 -2.5</gml:lowerCorner>
                  <gml:upperCorner>51.0 0.0</gml:upperCorner>
                </gml:Envelope>
              </gml:boundedBy>
              <gml:pos>50.8 -1.3</gml:pos>
            </d>
            """);

        var (order, assessment) = GmlCoordinateParser.AssessAxisOrder(root);

        Assert.Equal(GmlAxisOrder.LatitudeFirst, order);
        Assert.Equal(AxisOrderOutcome.AsEncoded, assessment!.Outcome);
        Assert.False(assessment.IsNotice);
    }

    [Fact]
    public void AssessAxisOrder_pairs_split_single_ordinate_positions()
    {
        // S-128 GML 1.0 IC-ENC/DK catalogues split each coordinate across
        // consecutive single-value gml:pos elements.
        var root = XElement.Parse("""
            <d xmlns:gml="http://www.opengis.net/gml/3.2">
              <gml:Polygon><gml:pos>21.1</gml:pos><gml:pos>41.1</gml:pos><gml:pos>21.9</gml:pos><gml:pos>41.9</gml:pos></gml:Polygon>
              <gml:boundedBy><gml:Envelope><gml:lowerCorner>41 21</gml:lowerCorner><gml:upperCorner>42 22</gml:upperCorner></gml:Envelope></gml:boundedBy>
            </d>
            """);

        var (order, assessment) = GmlCoordinateParser.AssessAxisOrder(root);

        Assert.Equal(GmlAxisOrder.LongitudeFirst, order);
        Assert.Equal(2, assessment!.PositionCount);
    }

    [Fact]
    public void AssessAxisOrder_returns_no_assessment_without_positions()
    {
        var root = XElement.Parse("""<d xmlns:gml="http://www.opengis.net/gml/3.2" />""");

        var (order, assessment) = GmlCoordinateParser.AssessAxisOrder(root);

        Assert.Equal(GmlAxisOrder.LatitudeFirst, order);
        Assert.Null(assessment);
    }
}
