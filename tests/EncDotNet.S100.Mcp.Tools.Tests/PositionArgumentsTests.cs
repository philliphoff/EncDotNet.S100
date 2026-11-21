using System.Text.Json;

namespace EncDotNet.S100.Mcp.Tools.Tests;

/// <summary>The one way the tools take a position (#1628).</summary>
public sealed class PositionArgumentsTests
{
    private static readonly (string, string, string, string) Names = ("latitude", "longitude", "lat", "lon");

    [Fact]
    public void The_canonical_names_or_the_aliases_give_the_position()
    {
        Assert.Null(PositionArguments.Resolve(48.5, -122.7, null, null, Names, out var canonical));
        Assert.Null(PositionArguments.Resolve(null, null, 48.5, -122.7, Names, out var alias));
        Assert.Null(PositionArguments.Resolve(48.5, null, null, -122.7, Names, out var mixed));

        Assert.Equal(canonical, alias);
        Assert.Equal(canonical, mixed);
        Assert.Equal(48.5, canonical!.Value.Latitude);
    }

    [Fact]
    public void Nothing_given_is_no_position_and_no_error()
    {
        Assert.Null(PositionArguments.Resolve(null, null, null, null, Names, out var position));
        Assert.Null(position);
    }

    [Theory]
    [InlineData(48.5, null, 48.6, null, "latitude")]
    [InlineData(null, -122.7, null, -122.8, "longitude")]
    [InlineData(48.5, null, null, null, "longitude")]
    [InlineData(null, -122.7, null, null, "latitude")]
    public void Conflicting_or_half_given_positions_are_refused(double? latitude, double? longitude, double? lat, double? lon, string parameter)
    {
        var error = PositionArguments.Resolve(latitude, longitude, lat, lon, Names, out _);

        Assert.Equal(parameter, error!.Parameter);
    }

    [Theory]
    [InlineData("""{"latitude":48.5,"longitude":-122.7}""")]
    [InlineData("""{"lat":48.5,"lon":-122.7}""")]
    [InlineData("""{"Latitude":"48.5","Longitude":"-122.7"}""")]
    [InlineData("[48.5,-122.7]")]
    public void A_position_value_takes_either_object_shape_or_a_pair(string json)
    {
        Assert.True(PositionArguments.TryRead(JsonDocument.Parse(json).RootElement, out var latitude, out var longitude));
        Assert.Equal(48.5, latitude);
        Assert.Equal(-122.7, longitude);
    }

    [Theory]
    [InlineData("""{"latitude":48.5}""")]
    [InlineData("[48.5]")]
    [InlineData("\"48.5, -122.7\"")]
    [InlineData("""{"lat":"north","lon":1}""")]
    public void Other_values_are_not_positions(string json) =>
        Assert.False(PositionArguments.TryRead(JsonDocument.Parse(json).RootElement, out _, out _));
}
