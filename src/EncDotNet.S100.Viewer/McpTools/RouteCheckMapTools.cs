using System.ComponentModel;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Tools;
using SkiaSharp;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>What the map draws for the selected finding.</summary>
[Description("How the map shows the selected finding.")]
internal sealed record MapHighlightDto(
    [property: Description("marker (a point finding's marker, ringed, with the pill), marker_and_sleeve (a span finding: its sleeve drawn wider and stronger, and its start marker ringed, with the pill), outline (a clearance-check finding, which the UKC line already shows: outlined dashed), or none (nothing selected, or no place on the map).")] string Kind,
    [property: Description("The marker's shape, which carries the group: octagon (must_review), triangle (check) or circle (info); null without a marker.")] string? Shape,
    [property: Description("True when a dotted leader runs from the marker's stem to the feature, which is off the centreline (beyond the corridor or more than 40 m).")] bool Leader,
    [property: Description("Zero-based legs whose corridor is filled in the accent: the selected route finding's legs; empty otherwise.")] IReadOnlyList<int> CorridorFilledLegs);

/// <summary>What the map shows for the selected finding, for <c>select_finding</c> and <c>get_findings</c>.</summary>
internal static class MapHighlight
{
    public static MapHighlightDto For(Routing.Route? route, IReadOnlyList<RouteFinding> findings, RouteFinding? selected, DateTimeOffset viewTime)
    {
        if (selected?.Location is not { Path.Count: > 0 })
            return new MapHighlightDto("none", null, false, []);
        if (!RouteCheckMap.Draws(selected))
            return new MapHighlightDto("outline", null, false, []);

        var map = RouteCheckMap.Build(route, findings, selected, RouteCheckColors.For(PaletteType.Day), SKColors.Blue, PaletteType.Day,
            viewTime, TimeFormat.Utc, RouteCheckOptions.Default.DefaultLimit.TotalMetres);
        if (map.SelectedMark is not { } mark)
            return new MapHighlightDto("none", null, false, []);
        var shape = mark.Group switch
        {
            RouteFindingGroup.MustReview => "octagon",
            RouteFindingGroup.Check => "triangle",
            _ => "circle",
        };
        return new MapHighlightDto(map.SelectedSleeve is null ? "marker" : "marker_and_sleeve", shape, mark.Feature is not null, map.FilledLegs);
    }
}
