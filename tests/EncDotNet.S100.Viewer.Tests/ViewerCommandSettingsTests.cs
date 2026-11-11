using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Parsing and validation of the agent-automation command-line options
/// on <see cref="ViewerCommandSettings"/>.
/// </summary>
public class ViewerCommandSettingsTests
{
    private static bool Ok(ViewerCommandSettings s) => s.Validate().Successful;

    [Theory]
    [InlineData("47.6,-122.3", true, 47.6, -122.3)]
    [InlineData(" 47.6 , -122.3 ", true, 47.6, -122.3)]
    [InlineData("0,0", true, 0, 0)]
    [InlineData("91,0", false, 0, 0)]
    [InlineData("0,181", false, 0, 0)]
    [InlineData("47.6", false, 0, 0)]
    [InlineData("a,b", false, 0, 0)]
    public void TryParseLatLon_parses_and_range_checks(string raw, bool expected, double lat, double lon)
    {
        var ok = ViewerCommandSettings.TryParseLatLon(raw, out var gotLat, out var gotLon);
        Assert.Equal(expected, ok);
        if (expected)
        {
            Assert.Equal(lat, gotLat, 3);
            Assert.Equal(lon, gotLon, 3);
        }
    }

    [Theory]
    [InlineData("47.5,-122.5,47.7,-122.1", true)]
    [InlineData("47.7,-122.5,47.5,-122.1", false)] // south >= north
    [InlineData("47.5,-122.1,47.7,-122.5", false)] // west >= east
    [InlineData("47.5,-122.5,47.7", false)]        // too few
    public void TryParseBoundingBox_validates_ordering(string raw, bool expected)
    {
        Assert.Equal(expected, ViewerCommandSettings.TryParseBoundingBox(raw, out _, out _, out _, out _));
    }

    [Fact]
    public void Center_without_zoom_is_invalid()
    {
        var s = new ViewerCommandSettings { Center = "47.6,-122.3" };
        Assert.False(Ok(s));
    }

    [Fact]
    public void Center_with_zoom_is_valid_and_sets_explicit_viewport()
    {
        var s = new ViewerCommandSettings { Center = "47.6,-122.3", Zoom = 12 };
        Assert.True(Ok(s));
        Assert.True(s.HasExplicitViewport);
        Assert.Equal(new GeoPosition(47.6, -122.3), s.ParsedCenter);
    }

    [Fact]
    public void Center_and_bbox_together_is_invalid()
    {
        var s = new ViewerCommandSettings
        {
            Center = "47.6,-122.3",
            Zoom = 12,
            BoundingBox = "47.5,-122.5,47.7,-122.1",
        };
        Assert.False(Ok(s));
    }

    [Fact]
    public void Ephemeral_and_settings_are_mutually_exclusive()
    {
        Assert.False(Ok(new ViewerCommandSettings { Ephemeral = true, SettingsPath = "/tmp/s.json" }));
    }

    [Theory]
    [InlineData("Day", true)]
    [InlineData("night", true)]
    [InlineData("Bright", false)]
    public void Palette_is_validated(string palette, bool expected)
    {
        Assert.Equal(expected, Ok(new ViewerCommandSettings { Palette = palette }));
    }

    [Theory]
    [InlineData("Standard", true)]
    [InlineData("displaybase", true)]
    [InlineData("All", true)]
    [InlineData("Everything", false)]
    public void DisplayCategory_is_validated(string category, bool expected)
    {
        Assert.Equal(expected, Ok(new ViewerCommandSettings { DisplayCategory = category }));
    }

    [Theory]
    [InlineData("70000", false)]
    [InlineData("-1", false)]
    [InlineData("0", true)]
    [InlineData("54321", true)]
    public void McpPort_is_range_checked(string port, bool expected)
    {
        Assert.Equal(expected, Ok(new ViewerCommandSettings { McpPort = int.Parse(port) }));
    }

    [Fact]
    public void McpRequested_true_for_any_mcp_option()
    {
        Assert.True(new ViewerCommandSettings { Mcp = true }.McpRequested);
        Assert.True(new ViewerCommandSettings { McpPort = 0 }.McpRequested);
        Assert.True(new ViewerCommandSettings { McpBind = "127.0.0.1" }.McpRequested);
        Assert.True(new ViewerCommandSettings { McpPortFile = "/tmp/p" }.McpRequested);
        Assert.False(new ViewerCommandSettings().McpRequested);
    }

    [Fact]
    public void Bad_mcp_bind_is_invalid()
    {
        Assert.False(Ok(new ViewerCommandSettings { McpBind = "not-an-ip" }));
        Assert.True(Ok(new ViewerCommandSettings { McpBind = "127.0.0.1" }));
    }

    [Theory]
    [InlineData("47.6,-122.3", true)]
    [InlineData("91,0", false)]
    [InlineData("47.6", false)]
    public void OwnShipPosition_is_validated(string raw, bool expected)
    {
        Assert.Equal(expected, Ok(new ViewerCommandSettings { OwnShipPosition = raw }));
    }

    [Theory]
    [InlineData(0.0, true)]
    [InlineData(359.9, true)]
    [InlineData(360.0, false)]
    [InlineData(-1.0, false)]
    public void OwnShipCourse_is_range_checked(double cog, bool expected)
    {
        Assert.Equal(expected, Ok(new ViewerCommandSettings { OwnShipCourse = cog }));
    }

    [Theory]
    [InlineData(0.0, true)]
    [InlineData(5.0, true)]
    [InlineData(-0.1, false)]
    public void OwnShipSpeed_is_range_checked(double sog, bool expected)
    {
        Assert.Equal(expected, Ok(new ViewerCommandSettings { OwnShipSpeed = sog }));
    }

    [Fact]
    public void ParsedOwnShipPosition_parses_latlon()
    {
        var s = new ViewerCommandSettings { OwnShipPosition = "47.6,-122.3" };
        Assert.Equal(new GeoPosition(47.6, -122.3), s.ParsedOwnShipPosition);
        Assert.Null(new ViewerCommandSettings().ParsedOwnShipPosition);
    }

    [Fact]
    public void HasOwnShipOption_true_for_any_own_ship_option()
    {
        Assert.True(new ViewerCommandSettings { OwnShipPosition = "0,0" }.HasOwnShipOption);
        Assert.True(new ViewerCommandSettings { OwnShipCourse = 90 }.HasOwnShipOption);
        Assert.True(new ViewerCommandSettings { OwnShipSpeed = 1 }.HasOwnShipOption);
        Assert.False(new ViewerCommandSettings().HasOwnShipOption);
    }
}
