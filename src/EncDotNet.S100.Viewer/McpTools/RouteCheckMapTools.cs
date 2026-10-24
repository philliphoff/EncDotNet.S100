using System.ComponentModel;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.PassageCheck;
using EncDotNet.S100.Viewer.Tools;
using ModelContextProtocol.Server;
using SkiaSharp;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>The Check route map legend (#959, handoff C8).</summary>
[Description("The Check route map legend at the map's bottom left: whether it's shown and open, and what it lists.")]
internal sealed record MapLegendDto(
    [property: Description("True while Check route is on, so the legend is on the map.")] bool Shown,
    [property: Description("True when the legend is open; false when it's folded to a 28 px \"Legend\" chip. Remembered per user.")] bool Open,
    [property: Description("The legend's sections, in order: \"UKC AT ETA\" (only when the checked route has a draft, so the line is coloured by clearance) and \"FINDINGS\". Listed whether or not the legend is open.")] IReadOnlyList<MapLegendSectionDto> Sections);

/// <summary>One legend section.</summary>
[Description("One section of the map legend.")]
internal sealed record MapLegendSectionDto(
    [property: Description("The section's title: \"UKC AT ETA\" or \"FINDINGS\".")] string Title,
    [property: Description("Each row's label, in order, e.g. \"Must review\", \"Check\", \"Info (on select)\", \"Stretch of the route\", \"Cross-track corridor\".")] IReadOnlyList<string> Rows);

/// <summary>What the map draws for the selected finding.</summary>
[Description("How the map shows the selected finding.")]
internal sealed record MapHighlightDto(
    [property: Description("marker (a point finding's marker, ringed, with the pill), marker_and_sleeve (a span finding: its sleeve drawn wider and stronger, and its start marker ringed, with the pill), outline (a clearance-check finding, which the UKC line already shows: outlined dashed), or none (nothing selected, or no place on the map).")] string Kind,
    [property: Description("The marker's shape, which carries the group: octagon (must_review), triangle (check) or circle (info); null without a marker.")] string? Shape,
    [property: Description("True when a dotted leader runs from the marker's stem to the feature, which is off the centreline (beyond the corridor or more than 40 m).")] bool Leader,
    [property: Description("Zero-based legs whose corridor is filled in the accent: the selected route finding's legs; empty otherwise.")] IReadOnlyList<int> CorridorFilledLegs);

/// <summary>Reads the map legend.</summary>
internal sealed class GetMapLegendTool
{
    public const string Name = "get_map_legend";

    private readonly RouteCheckLegendState _state;
    private readonly RoutesService _routes;
    private readonly PassageCheckService _check;
    private readonly IRouteEditInvoker _invoker;

    public GetMapLegendTool(RouteCheckLegendState state, RoutesService routes, PassageCheckService check, IRouteEditInvoker invoker)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(check);
        ArgumentNullException.ThrowIfNull(invoker);
        _state = state;
        _routes = routes;
        _check = check;
        _invoker = invoker;
    }

    public Task<ToolResult<MapLegendDto>> InvokeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _invoker.InvokeAsync(() => ToolResult<MapLegendDto>.Ok(Dto(_state, _routes, _check)));
    }

    internal static MapLegendDto Dto(RouteCheckLegendState state, RoutesService routes, PassageCheckService check)
    {
        var sections = new List<MapLegendSectionDto>();
        if (check.HasDraft)
        {
            sections.Add(new MapLegendSectionDto(Strings.Check_LegendTitle,
                [Strings.Check_LegendMeets, Strings.Check_LegendTight, Strings.Check_LegendBelow, Strings.Check_LegendLowConfidence, Strings.Check_LegendNoData]));
        }

        sections.Add(new MapLegendSectionDto(Strings.Map_LegendFindings,
            [Strings.Map_LegendMustReview, Strings.Map_LegendCheck, Strings.Map_LegendInfo, Strings.Map_LegendSleeve, Strings.Map_LegendCorridor]));
        return new MapLegendDto(routes.IsCheckModeActive, state.IsOpen, sections);
    }
}

/// <summary>Opens or folds the map legend, as its header does.</summary>
internal sealed class SetMapLegendTool
{
    public const string Name = "set_map_legend";

    private readonly RouteCheckLegendState _state;
    private readonly RoutesService _routes;
    private readonly PassageCheckService _check;
    private readonly IRouteEditInvoker _invoker;

    public SetMapLegendTool(RouteCheckLegendState state, RoutesService routes, PassageCheckService check, IRouteEditInvoker invoker)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(check);
        ArgumentNullException.ThrowIfNull(invoker);
        _state = state;
        _routes = routes;
        _check = check;
        _invoker = invoker;
    }

    public Task<ToolResult<MapLegendDto>> InvokeAsync(bool open, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _invoker.InvokeAsync(() =>
        {
            _state.IsOpen = open;
            return ToolResult<MapLegendDto>.Ok(GetMapLegendTool.Dto(_state, _routes, _check));
        });
    }
}

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

/// <summary>Wraps the map legend tools as <see cref="McpServerTool"/>s.</summary>
internal static class RouteCheckMapMcpAdapters
{
    public static McpServerTool Create(GetMapLegendTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (CancellationToken ct = default) => McpAdapterShared.DispatchAsync(() => inner.InvokeAsync(ct));
        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = GetMapLegendTool.Name,
            Description =
                "Returns the Check route map legend (bottom left of the map): whether it's shown (Check route on), " +
                "whether it's open or folded to a \"Legend\" chip (remembered per user), and its sections: \"UKC AT ETA\" " +
                "(with a draft) and \"FINDINGS\" (the marker shapes, the sleeve and the corridor edge). Change it with set_map_legend.",
            SerializerOptions = McpAdapterShared.Options,
        });
    }

    public static McpServerTool Create(SetMapLegendTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("True to open the legend, false to fold it to its \"Legend\" chip.")] bool open,
            CancellationToken ct = default) =>
            McpAdapterShared.DispatchAsync(() => inner.InvokeAsync(open, ct));
        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = SetMapLegendTool.Name,
            Description =
                "Opens or folds the Check route map legend, as clicking its header does; the choice is remembered per " +
                "user. Works whether or not Check route is on (the legend shows only while it is). Returns what " +
                "get_map_legend returns. Viewer-injected tool — mutates live state.",
            SerializerOptions = McpAdapterShared.Options,
        });
    }
}
