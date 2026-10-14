using System.ComponentModel;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.PassageCheck;
using EncDotNet.S100.Viewer.ViewModels;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>A route's findings list (#905).</summary>
[Description("A route's findings list: the clearance findings (and, once #888 lands, route-check findings), grouped must_review, check and info.")]
internal sealed record RouteFindingsDto(
    [property: Description("Id of the route the findings are for.")] string RouteId,
    [property: Description("True when the findings are the live Check passage list shown in the Routes panel (the checked route); false for a one-off check of another route, or of the active route while Check passage is off.")] bool Live,
    [property: Description("Number of findings; the Findings segment shows it.")] int Count,
    [property: Description("Number of must_review findings.")] int MustReview,
    [property: Description("Number of check findings.")] int Check,
    [property: Description("Number of info findings.")] int Info,
    [property: Description("Id of the selected finding (outlined on the map), or null. Only the live list has a selection.")] string? SelectedId,
    [property: Description("The findings, in list order: by group, then by distance along the route.")] IReadOnlyList<RouteFindingDto> Findings);

/// <summary>One finding.</summary>
[Description("One finding in a route's findings list.")]
internal sealed record RouteFindingDto(
    [property: Description("Id, stable while a recompute finds the same thing, e.g. \"clearance.below_setting.0\"; pass it to select_finding.")] string Id,
    [property: Description("Kind of finding, e.g. clearance.below_setting, confidence.charted_depth_from_s101, data.water_level_ends or data.s111_forecast_expired.")] string Code,
    [property: Description("What to do: must_review, check or info.")] string Group,
    [property: Description("The check that produced it: clearance, route_check, s124, confidence or data.")] string Source,
    [property: Description("The tag the row shows, e.g. \"Clearance\" or \"Data\".")] string Tag,
    [property: Description("The row's title, e.g. \"Below your UKC setting for 12 min\".")] string Title,
    [property: Description("The row's meta line, e.g. \"Leg 6 → 7 · min 0.4 m at 07:52\".")] string Meta,
    [property: Description("Zero-based leg the finding is on (its first leg for a span), or null.")] int? LegIndex,
    [property: Description("Distance from departure in nautical miles to the point the profile cursor moves to, or null when the finding isn't on the route.")] double? DistanceNm,
    [property: Description("Where the finding is, or null when it has no place on the map.")] RouteFindingLocationDto? Location,
    [property: Description("The feature behind the finding (open it with pick or describe), or null.")] RouteFindingFeatureDto? Feature,
    [property: Description("True when this is the selected finding.")] bool Selected);

/// <summary>Where a finding is.</summary>
[Description("Where a finding is: a point, or a span of the route.")]
internal sealed record RouteFindingLocationDto(
    [property: Description("\"point\" or \"span\".")] string Kind,
    [property: Description("Distance from departure to the start of the span, in nautical miles (the point's distance for a point).")] double StartNm,
    [property: Description("Distance from departure to the end of the span, in nautical miles.")] double EndNm,
    [property: Description("The point, or the positions along the span in route order.")] IReadOnlyList<RouteFindingPointDto> Path);

/// <summary>A position.</summary>
[Description("A WGS-84 position.")]
internal sealed record RouteFindingPointDto(
    [property: Description("Latitude in decimal degrees.")] double Latitude,
    [property: Description("Longitude in decimal degrees.")] double Longitude);

/// <summary>A feature reference.</summary>
[Description("A feature behind a finding.")]
internal sealed record RouteFindingFeatureDto(
    [property: Description("Catalog id of the dataset that holds the feature (as list_datasets).")] string DatasetId,
    [property: Description("The feature's id within the dataset.")] string FeatureId);

/// <summary>Request for <see cref="SelectFindingTool"/>.</summary>
[Description("Request for select_finding: select a finding in the live findings list, or clear the selection.")]
internal sealed record SelectFindingRequest(
    [property: Description("Id of the finding (from get_findings); null clears the selection.")] string? Id,
    [property: Description("Zoom the map to the finding (default true), as clicking its row does.")] bool Zoom = true);

/// <summary>Shared plumbing for the findings tools.</summary>
internal abstract class RouteFindingsToolBase : PassageCheckToolBase
{
    protected RouteFindingsToolBase(RoutesService routes, IRouteEditInvoker invoker, PassageCheckService check, RouteFindingsService findings)
        : base(routes, invoker, check)
    {
        ArgumentNullException.ThrowIfNull(findings);
        Findings = findings;
    }

    protected RouteFindingsService Findings { get; }

    internal static RouteFindingsDto ToDto(string routeId, bool live, IReadOnlyList<RouteFinding> findings, RouteFinding? selected) => new(
        routeId,
        live,
        findings.Count,
        findings.Count(f => f.Group == RouteFindingGroup.MustReview),
        findings.Count(f => f.Group == RouteFindingGroup.Check),
        findings.Count(f => f.Group == RouteFindingGroup.Info),
        live ? selected?.Id : null,
        findings.Select(f => ToDto(f, live && f.Id == selected?.Id)).ToList());

    private static RouteFindingDto ToDto(RouteFinding f, bool selected) => new(
        f.Id,
        f.Code,
        f.Group switch
        {
            RouteFindingGroup.MustReview => "must_review",
            RouteFindingGroup.Check => "check",
            _ => "info",
        },
        f.Source switch
        {
            RouteFindingSource.Clearance => "clearance",
            RouteFindingSource.RouteCheck => "route_check",
            RouteFindingSource.S124 => "s124",
            RouteFindingSource.Confidence => "confidence",
            _ => "data",
        },
        RouteFindingRowViewModel.TagOf(f.Source),
        f.Title,
        f.Meta,
        f.LegIndex,
        f.DistanceMeters is { } d ? Nm(d) : null,
        f.Location is { } l
            ? new RouteFindingLocationDto(
                l.IsPoint ? "point" : "span",
                Nm(l.StartDistanceMeters),
                Nm(l.EndDistanceMeters),
                l.Path.Select(p => new RouteFindingPointDto(p.Latitude, p.Longitude)).ToList())
            : null,
        f.Feature is { } feature ? new RouteFindingFeatureDto(feature.DatasetId, feature.FeatureId) : null,
        selected);

    private static double Nm(double meters) => Math.Round(meters / PassageGeodesy.MetersPerNauticalMile, 3);
}

/// <summary>Returns the findings list for the active (or a named) route.</summary>
internal sealed class GetFindingsTool : RouteFindingsToolBase
{
    public const string Name = "get_findings";

    public GetFindingsTool(RoutesService routes, IRouteEditInvoker invoker, PassageCheckService check, RouteFindingsService findings)
        : base(routes, invoker, check, findings) { }

    public async Task<ToolResult<RouteFindingsDto>> InvokeAsync(string? routeId, bool wait = true, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (wait)
            await SettleAsync(cancellationToken).ConfigureAwait(false);

        var pending = await Invoker.InvokeAsync(async () =>
        {
            var route = Resolve(routeId, out var notFound);
            if (route is null)
                return ToolResult<RouteFindingsDto>.Err(notFound!);
            var live = Check.IsActive && Check.Current?.RouteId == route.Id && ReferenceEquals(route, Routes.Routes.ActiveRoute);
            var findings = await Findings.FindingsForAsync(route, cancellationToken);
            if (findings is null)
            {
                return ToolResult<RouteFindingsDto>.Err(new InvalidArgument(
                    "routeId", "the route needs at least two waypoints and a draft; set one with set_passage"));
            }

            return ToolResult<RouteFindingsDto>.Ok(ToDto(route.Id, live, findings, Findings.Selected));
        }).ConfigureAwait(false);
        return await pending.ConfigureAwait(false);
    }
}

/// <summary>Selects a finding in the live list, as clicking its row does.</summary>
internal sealed class SelectFindingTool : RouteFindingsToolBase
{
    public const string Name = "select_finding";

    public SelectFindingTool(RoutesService routes, IRouteEditInvoker invoker, PassageCheckService check, RouteFindingsService findings)
        : base(routes, invoker, check, findings) { }

    public async Task<ToolResult<RouteFindingsDto>> InvokeAsync(SelectFindingRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        await SettleAsync(cancellationToken).ConfigureAwait(false);
        return await Invoker.InvokeAsync(() =>
        {
            if (!Check.IsActive || Check.Current is not { } current)
                return ToolResult<RouteFindingsDto>.Err(new InvalidArgument("id", "Check passage is off or has no result; turn it on with set_check_mode"));

            RouteFinding? finding = null;
            if (request.Id is { } id)
            {
                finding = Findings.Findings.FirstOrDefault(f => f.Id == id);
                if (finding is null)
                    return ToolResult<RouteFindingsDto>.Err(new InvalidArgument("id", $"no finding '{id}' in the list; read the ids with get_findings"));
            }

            Findings.Select(finding, request.Zoom);
            return ToolResult<RouteFindingsDto>.Ok(ToDto(current.RouteId, live: true, Findings.Findings, Findings.Selected));
        }).ConfigureAwait(false);
    }
}

/// <summary>Wraps the findings tools as <see cref="McpServerTool"/>s.</summary>
internal static class RouteFindingsMcpAdapters
{
    public static McpServerTool Create(GetFindingsTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("Route to read; omit for the active route.")] string? routeId = null,
            [Description("Wait for a pending Check passage recompute first (default true).")] bool wait = true,
            CancellationToken ct = default) =>
            McpAdapterShared.DispatchAsync(() => inner.InvokeAsync(routeId, wait, ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = GetFindingsTool.Name,
            Description =
                "Returns a route's findings list, as the Routes panel's Findings tab shows it in Check passage mode: " +
                "each finding's group (must_review, check, info), source tag, title, meta line, leg, distance along the " +
                "route, location (point or span) and feature reference. For the checked route it's the live list, with " +
                "the selection; for another route (or with Check passage off) the route is checked once without changing " +
                "the view. The route needs a draft (set_passage).",
            SerializerOptions = McpAdapterShared.Options,
        });
    }

    public static McpServerTool Create(SelectFindingTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("Id of the finding (from get_findings); omit or null to clear the selection.")] string? id = null,
            [Description("Zoom the map to the finding (default true).")] bool zoom = true,
            CancellationToken ct = default) =>
            McpAdapterShared.DispatchAsync(() => inner.InvokeAsync(new SelectFindingRequest(id, zoom), ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = SelectFindingTool.Name,
            Description =
                "Selects a finding in the checked route's findings list, as clicking its row does: the map zooms to it " +
                "and outlines it dashed in its tone with a \"Zoomed to finding\" chip, and the profile cursor and ghost " +
                "vessel move to it. Needs Check passage on (set_check_mode). Returns the list get_findings returns. " +
                "Viewer-injected tool — mutates live state.",
            SerializerOptions = McpAdapterShared.Options,
        });
    }
}
