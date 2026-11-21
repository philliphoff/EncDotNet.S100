using System.ComponentModel;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Mcp.Tools;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>
/// What the chart says about the water at a waypoint just placed or moved
/// (#1628), as <c>sample</c> with <c>quantity: depth</c> reads it.
/// </summary>
/// <param name="WaypointIndex">The waypoint's index in the route (where it is, or would be with <c>validate</c>).</param>
/// <param name="Applied">Whether the edit was made; <c>false</c> for a <c>validate</c> dry run.</param>
/// <param name="WaterMask">What the loaded charts show there: <c>water</c>, <c>land</c> or <c>uncharted</c>.</param>
/// <param name="Depth">The charted depth there and where it came from, or <c>null</c>.</param>
/// <param name="MinDepthMeters">The least depth the point was checked against, or <c>null</c> when it wasn't.</param>
/// <param name="MinDepthFrom"><c>request</c> (minDepthMeters) or <c>route</c> (its draft + squat + required UKC); <c>null</c> when not checked.</param>
/// <param name="Navigable">Whether the point is navigable at <paramref name="MinDepthMeters"/>; <c>null</c> when not checked.</param>
/// <param name="NotNavigableReason">Why it isn't: land, uncharted, shoal, hazard, no_depth or too_close.</param>
/// <param name="NearestNavigable">When it isn't, the nearest point that is, or why there's none.</param>
internal sealed record WaypointWaterDto(
    [property: Description("The waypoint's index in the route (where it is, or would be with validate).")] int WaypointIndex,
    [property: Description("Whether the edit was made; false for a validate dry run.")] bool Applied,
    [property: Description("What the loaded charts show at the point: \"water\", \"land\" or \"uncharted\".")] string WaterMask,
    [property: Description("The charted depth at the point and where it came from, as sample reads it; null where no source has one.")] ChartedDepthValue? Depth,
    [property: Description("The least charted depth the point was checked against, in metres; null when no minDepthMeters was given and the route has no draft.")] double? MinDepthMeters = null,
    [property: Description("\"request\" (minDepthMeters) or \"route\" (its draft + squat + required UKC); null when not checked.")] string? MinDepthFrom = null,
    [property: Description("Whether the point is navigable at minDepthMeters, as sample judges it; null when not checked.")] bool? Navigable = null,
    [property: Description("Why it isn't navigable: \"land\", \"uncharted\", \"shoal\", \"hazard\", \"no_depth\" or \"too_close\".")] string? NotNavigableReason = null,
    [property: Description("When it isn't navigable, the nearest point that is (move the waypoint there with move_waypoint), or a reason when none is in reach.")] NearestNavigablePoint? NearestNavigable = null);

/// <summary>
/// Checks the water at a waypoint with the <c>sample</c> tool itself, so a
/// route tool, <c>sample</c> and <c>plan_route</c> judge a point the same way.
/// </summary>
internal sealed class WaypointWater
{
    private readonly SampleTool _sample;

    public WaypointWater(SampleTool sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        _sample = sample;
    }

    /// <summary>The water at a point, checked against a minimum depth when one is given.</summary>
    public async Task<WaypointWaterDto?> CheckAsync(
        int index, GeoPosition position, double? minDepthMeters, string? minDepthFrom, bool applied, CancellationToken cancellationToken)
    {
        var result = await _sample.InvokeAsync(
            new SampleRequest(SampleQuantity.Depth, Latitude: position.Latitude, Longitude: position.Longitude, MinDepthMeters: minDepthMeters),
            cancellationToken).ConfigureAwait(false);
        if (!result.TryGetValue(out var output) || output?.Depth is not { } depth)
        {
            return null;
        }

        return new WaypointWaterDto(
            index,
            applied,
            depth.WaterMask,
            depth.Depth,
            minDepthMeters is null ? null : Math.Round(minDepthMeters.Value, 2),
            minDepthMeters is null ? null : minDepthFrom,
            depth.Navigable,
            depth.NotNavigableReason,
            depth.NearestNavigable);
    }
}
