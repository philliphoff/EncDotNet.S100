using System.Text.Json;
using EncDotNet.S100.Viewer.McpTools;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The viewer tools take a position the same way as the server's (#1628):
/// {latitude, longitude}, the older {lat, lon}, or a [lat, lon] pair.
/// </summary>
public class PositionInputsTests
{
    [Fact]
    public void Measure_points_take_every_position_shape()
    {
        var points = JsonDocument.Parse("""[{"latitude":48.5,"longitude":-122.7},{"lat":48.6,"lon":-122.8},[48.7,-122.9],"48.8, -123.0"]""").RootElement;

        Assert.Null(SetMeasureTool.TryParsePoints(points, out var positions));

        Assert.Equal([48.5, 48.6, 48.7, 48.8], positions.Select(p => p.Latitude));
    }

    [Fact]
    public void A_measure_point_out_of_range_names_latitude()
    {
        var points = JsonDocument.Parse("""[{"latitude":95,"longitude":0}]""").RootElement;

        var error = SetMeasureTool.TryParsePoints(points, out _);

        Assert.Contains("latitude", error!.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"latitude":-32.587,"longitude":61.295}""")]
    [InlineData("""{"lat":-32.587,"lon":61.295}""")]
    [InlineData("[-32.587,61.295]")]
    public void A_range_and_bearing_origin_takes_every_position_shape(string json)
    {
        Assert.True(SetRangeBearingTool.TryPosition(JsonDocument.Parse(json).RootElement, out var position, out var error), error);

        Assert.Equal(-32.587, position!.Value.Latitude);
        Assert.Equal(61.295, position.Value.Longitude);
    }
}
