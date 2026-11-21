using System.Text.Json;
using EncDotNet.S100.Mcp.Tools;

namespace EncDotNet.S100.Mcp.Tests;

/// <summary><c>plan_route</c> reads its arguments by <see cref="PlanRouteRequest"/>'s own names (#1560).</summary>
public class PlanRouteArgumentsTests
{
    [Fact]
    public void Points_may_be_objects_or_lat_lon_pairs()
    {
        var request = S100McpServerToolFactory.ReadPlanRouteRequest(new Dictionary<string, object?>
        {
            ["from"] = JsonSerializer.SerializeToElement(new { latitude = 48.5, longitude = -122.6 }),
            ["to"] = JsonSerializer.SerializeToElement(new[] { 48.54, -123.01 }),
            ["via"] = JsonSerializer.SerializeToElement(new object[] { new[] { 48.52, -122.8 }, new { latitude = 48.53, longitude = -122.9 } }),
            ["vessel"] = JsonSerializer.SerializeToElement(new { draftMeters = 1.1, safetyContourMeters = 1.5 }),
            ["prefer"] = JsonSerializer.SerializeToElement(new[] { "fairways" }),
            ["check"] = JsonSerializer.SerializeToElement(false),
        });

        Assert.Equal(48.5, request.From.Latitude);
        Assert.Equal(-123.01, request.To.Longitude);
        Assert.Equal([48.52, 48.53], request.Via!.Select(v => v.Latitude));
        Assert.Equal(1.5, request.Vessel!.SafetyContourMeters);
        Assert.Equal(["fairways"], request.Prefer);
        Assert.False(request.Check);
    }

    [Fact]
    public void Points_and_groups_may_be_json_text()
    {
        var request = S100McpServerToolFactory.ReadPlanRouteRequest(new Dictionary<string, object?>
        {
            ["from"] = JsonSerializer.SerializeToElement("[48.5, -122.6]"),
            ["to"] = JsonSerializer.SerializeToElement("""{"latitude":48.54,"longitude":-123.01}"""),
            ["vessel"] = JsonSerializer.SerializeToElement("""{"draftMeters":2}"""),
            ["routeName"] = JsonSerializer.SerializeToElement("[day 1]"),
        });

        Assert.Equal(-122.6, request.From.Longitude);
        Assert.Equal(48.54, request.To.Latitude);
        Assert.Equal(2, request.Vessel!.DraftMeters);
        Assert.Equal("[day 1]", request.RouteName);
        Assert.True(request.Check);
    }

    [Fact]
    public void From_and_to_are_required()
    {
        Assert.Throws<ArgumentException>(() => S100McpServerToolFactory.ReadPlanRouteRequest(new Dictionary<string, object?>
        {
            ["from"] = JsonSerializer.SerializeToElement(new[] { 48.5, -122.6 }),
        }));

        var schema = S100McpServerToolFactory.PlanRouteInputSchema;
        Assert.Equal(["from", "to", "vessel"], schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()));
        Assert.True(schema.GetProperty("properties").TryGetProperty("via", out _));
        Assert.True(schema.GetProperty("properties").TryGetProperty("limits", out _));
    }
}
