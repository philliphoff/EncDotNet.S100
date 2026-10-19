using System.ComponentModel;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.PassageCheck;
using EncDotNet.S100.Viewer.ViewModels;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>A route's findings list (#905).</summary>
[Description("A route's findings list: what every check found (clearance, route checks, S-124 warnings, confidence and data), grouped must_review, check and info.")]
internal sealed record RouteFindingsDto(
    [property: Description("Id of the route the findings are for.")] string RouteId,
    [property: Description("True when the findings are the live Check route list shown in the Routes panel (the checked route); false for a one-off check of another route, or of the active route while Check route is off.")] bool Live,
    [property: Description("Number of findings; the Findings segment shows it.")] int Count,
    [property: Description("Number of must_review findings.")] int MustReview,
    [property: Description("Number of check findings.")] int Check,
    [property: Description("Number of info findings.")] int Info,
    [property: Description("The findings summary, e.g. \"2 to review\", \"1 to check\" or \"No findings for your settings\"; the verdict card's title when the route has no draft.")] string Summary,
    [property: Description("True while a check is recomputing and part of the list is out of date (live list only).")] bool Stale,
    [property: Description("Id of the selected finding (outlined on the map), or null. Only the live list has a selection.")] string? SelectedId,
    [property: Description("The findings, in list order: by group, then by distance along the route.")] IReadOnlyList<RouteFindingDto> Findings)
{
    /// <summary>The filter chip.</summary>
    [Description("The list's filter chip: all, clearance, route or warnings (live list only; a one-off check reports all). Set it with set_findings_view or a verdict-card summary row.")]
    public string Filter { get; init; } = "all";

    /// <summary>Per-chip counts.</summary>
    [Description("How many findings each filter chip shows: all, clearance (the clearance check's findings and its confidence and data findings), route (route checks against the chart) and warnings (S-124).")]
    public FindingsCountsDto Counts { get; init; } = new(0, 0, 0, 0);

    /// <summary>The merged rows.</summary>
    [Description("The list's merged rows (after the filter): check and info findings that share a code show as one row with a count (×N); must_review never merges. Each gives every member finding's id.")]
    public IReadOnlyList<MergedFindingsDto> Merged { get; init; } = [];

    /// <summary>Whether INFO is unfolded.</summary>
    [Description("True when the INFO group is unfolded; it's folded by default and remembered per route (live list only).")]
    public bool InfoOpen { get; init; }

    /// <summary>The expanded merged row.</summary>
    [Description("Id of the merged row expanded into its findings, or null (live list only).")]
    public string? ExpandedId { get; init; }
}

/// <summary>How many findings each filter chip shows.</summary>
[Description("How many findings each filter chip shows.")]
internal sealed record FindingsCountsDto(
    [property: Description("Every finding.")] int All,
    [property: Description("The clearance check's findings, with its confidence and data findings.")] int Clearance,
    [property: Description("Route checks against the chart (land, drying, dangers, areas, chart coverage).")] int Route,
    [property: Description("S-124 navigational warnings.")] int Warnings);

/// <summary>A merged row.</summary>
[Description("Check or info findings with the same code, shown as one row with a count.")]
internal sealed record MergedFindingsDto(
    [property: Description("The row's id, e.g. \"merged.check.confidence.charted_depth_from_s101\"; pass it to set_findings_view's expandedId.")] string Id,
    [property: Description("The shared code.")] string Code,
    [property: Description("The group: check or info.")] string Group,
    [property: Description("How many findings the row merges (the \"×N\").")] int Count,
    [property: Description("The row's meta line, e.g. \"Legs 7 → 9 · 9 stretches\".")] string Meta,
    [property: Description("The member findings' ids, in list order; each can be selected with select_finding.")] IReadOnlyList<string> Ids,
    [property: Description("True when the row is expanded into its findings.")] bool Expanded);

/// <summary>Request for <see cref="SetFindingsViewTool"/>.</summary>
[Description("Request for set_findings_view: set the findings list's filter chip, INFO fold and expanded merged row.")]
internal sealed record SetFindingsViewRequest(
    [property: Description("The filter chip: all, clearance, route or warnings; omit to keep it.")] string? Filter = null,
    [property: Description("True to unfold INFO, false to fold it; omit to keep it.")] bool? InfoOpen = null,
    [property: Description("Id of a merged row to expand (from get_findings' merged), or an empty string to collapse; omit to keep it.")] string? ExpandedId = null);

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
    [property: Description("True when this is the selected finding.")] bool Selected,
    [property: Description("For an s124 finding, the navigational warning and how it stands on the route at the ETA; null for other sources.")] RouteFindingWarningDto? Warning = null);

/// <summary>An S-124 finding's warning.</summary>
[Description("The navigational warning behind an s124 finding, and how it stands on the route.")]
internal sealed record RouteFindingWarningDto(
    [property: Description("The series and number, e.g. \"NAVAREA I 012/26\", or null when the preamble gives no number.")] string? Number,
    [property: Description("The warning's title, or null.")] string? Title,
    [property: Description("The locality (else the general area), or null.")] string? Locality,
    [property: Description("How it relates to the route: crosses (a line or area reaches the corridor between the cross-track limits) or near (within 0.5 NM of the track, or a point inside the corridor).")] string Relation,
    [property: Description("Whether it applies while the vessel is there: in_force, not_in_force or cancelled.")] string Status,
    [property: Description("When the vessel reaches it, ISO 8601 UTC: the ETA, or the view time when the legs have no speed (see atEta).")] DateTimeOffset From,
    [property: Description("When the vessel leaves it, ISO 8601 UTC; equal to from for a crossing point or a near warning.")] DateTimeOffset Until,
    [property: Description("True when from and until are ETAs; false when the route has no speed and the view time is used.")] bool AtEta,
    [property: Description("When the warning applies: its validity ranges in document order; empty when it gives none (in force until cancelled).")] IReadOnlyList<RouteFindingValidityDto> Validity,
    [property: Description("When the warning cancels itself, ISO 8601 UTC, or null.")] DateTimeOffset? CancellationDate,
    [property: Description("The full message text, one entry per language in document order.")] IReadOnlyList<RouteFindingWarningTextDto> Text);

/// <summary>A validity range.</summary>
[Description("A period when a navigational warning applies; either end may be open (null).")]
internal sealed record RouteFindingValidityDto(
    [property: Description("Start, ISO 8601 UTC, or null when open.")] DateTimeOffset? Start,
    [property: Description("End, ISO 8601 UTC, or null when open.")] DateTimeOffset? End);

/// <summary>One language of a warning's text.</summary>
[Description("One language of a navigational warning's message text.")]
internal sealed record RouteFindingWarningTextDto(
    [property: Description("The language code as the dataset writes it (e.g. \"eng\"), or null.")] string? Language,
    [property: Description("The text, with its line breaks kept.")] string Text);

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
        : base(routes, invoker, check, findings)
    {
        ArgumentNullException.ThrowIfNull(findings);
        Findings = findings;
    }

    protected RouteFindingsService Findings { get; }

    /// <summary>Waits for the check, then for the findings producers, as <see cref="PassageCheckToolBase.SettleAsync"/>.</summary>
    protected async Task SettleFindingsAsync(CancellationToken cancellationToken)
    {
        await SettleAsync(cancellationToken).ConfigureAwait(false);
        var idle = await Invoker.InvokeAsync(() => Findings.WhenIdle()).ConfigureAwait(false);
        await idle.WaitAsync(WaitTimeout, cancellationToken).ConfigureAwait(false);
        // Results are posted to the UI thread; hop there so they have landed.
        await Invoker.InvokeAsync(() => true).ConfigureAwait(false);
    }

    internal static RouteFindingsDto ToDto(string routeId, bool live, IReadOnlyList<RouteFinding> findings, RouteFinding? selected, bool stale = false, RouteFindingsService? view = null)
    {
        var filter = live && view is not null ? view.Filter : FindingsFilter.All;
        var expanded = live ? view?.ExpandedId : null;
        var counts = RouteFindingsViewModel.Counts(findings);
        return Dto(routeId, live, findings, selected, stale) with
        {
            Filter = PassageCheckToolBase.Filter(filter),
            Counts = new FindingsCountsDto(counts[FindingsFilter.All], counts[FindingsFilter.Clearance], counts[FindingsFilter.Route], counts[FindingsFilter.Warnings]),
            Merged = RouteFindingsViewModel.Entries(findings, filter)
                .Where(e => e.IsMerged)
                .Select(e => new MergedFindingsDto(
                    e.Id,
                    e.Members[0].Code,
                    FindingsEntry.GroupCode(e.Group),
                    e.Members.Count,
                    FindingsEntry.MergedMeta(e.Members),
                    e.Members.Select(m => m.Id).ToList(),
                    e.Id == expanded))
                .ToList(),
            InfoOpen = live && (view?.InfoOpen ?? false),
            ExpandedId = expanded,
        };
    }

    private static RouteFindingsDto Dto(string routeId, bool live, IReadOnlyList<RouteFinding> findings, RouteFinding? selected, bool stale) => new(
        routeId,
        live,
        findings.Count,
        findings.Count(f => f.Group == RouteFindingGroup.MustReview),
        findings.Count(f => f.Group == RouteFindingGroup.Check),
        findings.Count(f => f.Group == RouteFindingGroup.Info),
        RouteCheckVerdict.NoDraftTitle(findings),
        live && stale,
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
        selected,
        f.Warning is { } warning ? ToDto(warning) : null);

    private static RouteFindingWarningDto ToDto(RouteFindingWarning w) => new(
        w.Info.Label,
        w.Info.Title,
        w.Info.Locality ?? w.Info.GeneralArea,
        w.Relation == RouteWarningRelation.Crosses ? "crosses" : "near",
        w.Status switch
        {
            RouteWarningStatus.InForce => "in_force",
            RouteWarningStatus.NotInForce => "not_in_force",
            _ => "cancelled",
        },
        w.From.ToUniversalTime(),
        w.Until.ToUniversalTime(),
        w.AtEta,
        w.Info.Validity.Select(v => new RouteFindingValidityDto(v.Start?.ToUniversalTime(), v.End?.ToUniversalTime())).ToList(),
        w.Info.CancellationDate?.ToUniversalTime(),
        w.Info.Texts.Select(t => new RouteFindingWarningTextDto(t.Language, t.Text)).ToList());

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
            await SettleFindingsAsync(cancellationToken).ConfigureAwait(false);

        var pending = await Invoker.InvokeAsync(async () =>
        {
            var route = Resolve(routeId, out var notFound);
            if (route is null)
                return ToolResult<RouteFindingsDto>.Err(notFound!);
            var live = Check.Input?.RouteId == route.Id && ReferenceEquals(route, Routes.Routes.ActiveRoute);
            var findings = await Findings.FindingsForAsync(route, cancellationToken);
            if (findings is null)
                return ToolResult<RouteFindingsDto>.Err(new InvalidArgument("routeId", "the route needs at least two waypoints"));

            return ToolResult<RouteFindingsDto>.Ok(ToDto(route.Id, live, findings, Findings.Selected, Findings.IsStale, Findings));
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
        await SettleFindingsAsync(cancellationToken).ConfigureAwait(false);
        return await Invoker.InvokeAsync(() =>
        {
            if (Check.Input is not { } current)
                return ToolResult<RouteFindingsDto>.Err(new InvalidArgument("id", "Check route is off; turn it on with set_check_mode"));

            RouteFinding? finding = null;
            if (request.Id is { } id)
            {
                finding = Findings.Findings.FirstOrDefault(f => f.Id == id);
                if (finding is null)
                    return ToolResult<RouteFindingsDto>.Err(new InvalidArgument("id", $"no finding '{id}' in the list; read the ids with get_findings"));
            }

            Findings.Select(finding, request.Zoom);
            return ToolResult<RouteFindingsDto>.Ok(ToDto(current.RouteId, live: true, Findings.Findings, Findings.Selected, Findings.IsStale, Findings));
        }).ConfigureAwait(false);
    }
}

/// <summary>
/// Sets the live findings list's view (#959, D1–D3), as its filter chips,
/// the verdict card's summary rows, the INFO heading and a merged row do.
/// </summary>
internal sealed class SetFindingsViewTool : RouteFindingsToolBase
{
    public const string Name = "set_findings_view";

    public SetFindingsViewTool(RoutesService routes, IRouteEditInvoker invoker, PassageCheckService check, RouteFindingsService findings)
        : base(routes, invoker, check, findings) { }

    public async Task<ToolResult<RouteFindingsDto>> InvokeAsync(SetFindingsViewRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        await SettleFindingsAsync(cancellationToken).ConfigureAwait(false);
        return await Invoker.InvokeAsync(() =>
        {
            if (Check.Input is not { } current)
                return ToolResult<RouteFindingsDto>.Err(new InvalidArgument("filter", "Check route is off; turn it on with set_check_mode"));

            FindingsFilter? filter = null;
            if (request.Filter is { } name)
            {
                filter = name.ToLowerInvariant() switch
                {
                    "all" => FindingsFilter.All,
                    "clearance" => FindingsFilter.Clearance,
                    "route" => FindingsFilter.Route,
                    "warnings" => FindingsFilter.Warnings,
                    _ => null,
                };
                if (filter is null)
                    return ToolResult<RouteFindingsDto>.Err(new InvalidArgument("filter", $"'{name}' isn't a filter; use all, clearance, route or warnings"));
            }

            string? expand = null;
            if (request.ExpandedId is { Length: > 0 } id)
            {
                var entries = RouteFindingsViewModel.Entries(Findings.Findings, filter ?? Findings.Filter);
                if (!entries.Any(e => e.IsMerged && e.Id == id))
                    return ToolResult<RouteFindingsDto>.Err(new InvalidArgument("expandedId", $"no merged row '{id}' in the list; read the ids from get_findings' merged"));
                expand = id;
            }

            Findings.SetView(filter, request.InfoOpen, expand, collapse: request.ExpandedId is { Length: 0 });
            return ToolResult<RouteFindingsDto>.Ok(ToDto(current.RouteId, live: true, Findings.Findings, Findings.Selected, Findings.IsStale, Findings));
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
            [Description("Wait for a pending Check route recompute (and the route checks) first (default true).")] bool wait = true,
            CancellationToken ct = default) =>
            McpAdapterShared.DispatchAsync(() => inner.InvokeAsync(routeId, wait, ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = GetFindingsTool.Name,
            Description =
                "Returns a route's findings list, as the Routes panel's Findings tab shows it in Check route mode: " +
                "each finding's group (must_review, check, info), source tag, title, meta line, leg, distance along the " +
                "route, location (point or span) and feature reference. An s124 finding also gives its warning: number, " +
                "relation (crosses or near), status at the ETA (in_force, not_in_force, cancelled; the view time without " +
                "speeds), validity and full text; must_review = crosses and in force, check = within 0.5 NM and in force, " +
                "info = not in force or cancelled. For the checked route it's the live list, with " +
                "the selection; for another route (or with Check route off) the route is checked once without changing " +
                "the view. The route needs at least two waypoints; without a draft (set_passage) there are no clearance " +
                "findings, only the route checks'. summary is the verdict card's title when the route has no draft. " +
                "It also reports the list's view: the filter chip and per-chip counts, the merged rows (check and info " +
                "findings sharing a code, with every member id), whether INFO is unfolded and the expanded row; change " +
                "it with set_findings_view.",
            SerializerOptions = McpAdapterShared.Options,
        });
    }

    public static McpServerTool Create(SetFindingsViewTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("The filter chip: all, clearance, route or warnings; omit to keep it.")] string? filter = null,
            [Description("True to unfold the INFO group, false to fold it; omit to keep it.")] bool? infoOpen = null,
            [Description("Id of a merged row to expand (get_findings' merged), or an empty string to collapse; omit to keep it.")] string? expandedId = null,
            CancellationToken ct = default) =>
            McpAdapterShared.DispatchAsync(() => inner.InvokeAsync(new SetFindingsViewRequest(filter, infoOpen, expandedId), ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = SetFindingsViewTool.Name,
            Description =
                "Sets the checked route's findings list view, as its controls do: the filter chip (all, clearance, " +
                "route, warnings; the verdict card's summary rows set it too), whether the INFO group is unfolded " +
                "(folded by default, remembered per route), and which merged row (check or info findings sharing a " +
                "code, shown once with ×N) is expanded into its findings. Needs Check route on (set_check_mode). " +
                "Returns the list get_findings returns. Viewer-injected tool — mutates live state.",
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
                "vessel move to it. Needs Check route on (set_check_mode). Returns the list get_findings returns. " +
                "Viewer-injected tool — mutates live state.",
            SerializerOptions = McpAdapterShared.Options,
        });
    }
}
