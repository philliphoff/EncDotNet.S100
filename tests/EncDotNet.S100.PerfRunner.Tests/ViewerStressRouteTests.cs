namespace EncDotNet.S100.PerfRunner.Tests;

public class ViewerStressRouteTests
{
    [Fact]
    public void Create_visits_bounds_in_snake_order_and_pulses_zoom()
    {
        var route = ViewerStressRoute.Create(
            new GeographicBounds(50, -6, 58, 2),
            minimumZoom: 6,
            maximumZoom: 12,
            stepCount: 9);

        Assert.Equal(9, route.Count);
        Assert.Equal((50, -6), (route[0].Latitude, route[0].Longitude));
        Assert.Equal((50, 2), (route[2].Latitude, route[2].Longitude));
        Assert.Equal((54, 2), (route[3].Latitude, route[3].Longitude));
        Assert.Equal((54, -6), (route[5].Latitude, route[5].Longitude));
        Assert.Equal(6, route[0].Zoom);
        Assert.Equal(12, route[4].Zoom);
        Assert.Equal(6, route[^1].Zoom);
    }

    [Fact]
    public void CreateNavigation_separates_pan_and_zoom_legs()
    {
        var route = ViewerStressRoute.CreateNavigation(
            new GeographicBounds(50, -2, 52, 2),
            minimumZoom: 10,
            maximumZoom: 16,
            stepCount: 13);

        Assert.Equal(13, route.Count);
        Assert.Equal((50.6, -1.6, 10), (
            route[0].Latitude, route[0].Longitude, route[0].Zoom));
        Assert.Equal((50.6, 1.6, 10), (
            route[2].Latitude, route[2].Longitude, route[2].Zoom));
        Assert.Equal((50.6, 1.6, 16), (
            route[4].Latitude, route[4].Longitude, route[4].Zoom));
        Assert.Equal((51.4, -1.6, 16), (
            route[8].Latitude, route[8].Longitude, route[8].Zoom));
        Assert.Equal((51, 0, 13), (
            route[^1].Latitude, route[^1].Longitude, route[^1].Zoom));
    }

    [Theory]
    [InlineData("49.8,-6.5,59.0,2.0", true)]
    [InlineData("59,-6,49,2", false)]
    [InlineData("49,2,59,-6", false)]
    [InlineData("49,-181,59,2", false)]
    [InlineData("not-a-bbox", false)]
    public void TryParseBounds_validates_shape_and_ranges(
        string value,
        bool expected)
    {
        Assert.Equal(expected, ViewerStressCommand.TryParseBounds(value, out _));
    }

    [Fact]
    public void Settings_require_exactly_one_endpoint_source()
    {
        var settings = new ViewerStressCommand.Settings
        {
            BoundingBox = "49.8,-6.5,59.0,2.0",
        };

        Assert.False(settings.Validate().Successful);

        settings.Endpoint = "http://127.0.0.1:1234/";
        Assert.True(settings.Validate().Successful);

        settings.PortFile = "/tmp/mcp.url";
        Assert.False(settings.Validate().Successful);
    }

    [Fact]
    public void TryReadUnionBounds_unions_loaded_dataset_extents()
    {
        var payload = System.Text.Json.Nodes.JsonNode.Parse("""
            {
              "datasets": [
                {
                  "bounds": {
                    "southLatitude": 50,
                    "westLongitude": -2,
                    "northLatitude": 51,
                    "eastLongitude": -1
                  }
                },
                {
                  "bounds": {
                    "southLatitude": 49,
                    "westLongitude": -3,
                    "northLatitude": 52,
                    "eastLongitude": 1
                  }
                }
              ]
            }
            """)!;

        Assert.True(ViewerStressCommand.TryReadUnionBounds(payload, out var bounds));
        Assert.Equal(new GeographicBounds(49, -3, 52, 1), bounds);
    }
}
