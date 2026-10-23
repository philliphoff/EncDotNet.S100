using System.ComponentModel;
using System.Text.Json;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Viewer.Routing;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.PassageCheck;
using EncDotNet.S100.Viewer.ViewModels;
using RouteCheckVerdict = EncDotNet.S100.Viewer.ViewModels.RouteCheckVerdict;
using RouteCheckVerdictKind = EncDotNet.S100.Viewer.ViewModels.RouteCheckVerdictKind;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>What Check route is showing (#901, #959).</summary>
[Description("Check route state: whether it's on, whether a result is computing, and what the verdict card and cursor show.")]
internal sealed record CheckStateDto(
    [property: Description("True when Check route is on.")] bool On,
    [property: Description("Id of the checked (active) route, or null.")] string? RouteId,
    [property: Description("True when the checked route has a draft. Without one the clearance check doesn't run: there is no clearance verdict, minimum UKC, Clearance tab or departure window, and the summary's clearance row reads \"Not checked\" with the link \"+ Add draft\". The route checks and warnings run either way.")] bool HasDraft,
    [property: Description("True while a recompute is pending or running.")] bool Computing,
    [property: Description("True while the shown result is out of date (the route draws grey and dashed, the verdict is dimmed).")] bool Stale,
    [property: Description("The clearance verdict code, as get_passage_result: water_level_ends_before_arrival, below_setting, incomplete_data, meets_settings_tight or meets_settings; null without a clearance result (no draft, or none yet). verdictKind is the card's overall verdict.")] string? Verdict,
    [property: Description("The verdict card's title, naming the worst finding from any check, e.g. \"Crosses drying ground\", \"Below your UKC setting in 1 place\", \"Meets your settings · 2 to check\", or without a draft \"2 to review\", \"1 to check\" or \"No findings for your settings\"; null when there is nothing to show yet.")] string? VerdictTitle,
    [property: Description("Lower-confidence reasons anywhere on the passage, as get_passage_result: charted_depth_from_s101 and/or datum_unconfirmed; empty at full confidence or without a result.")] IReadOnlyList<string> Confidence,
    [property: Description("The verdict card's confidence line, e.g. \"Vertical datums unconfirmed: depth and water level may not share a reference\" (one line per reason); null at full confidence.")] string? ConfidenceText,
    [property: Description("The minimum UKC in metres; null when none could be computed.")] double? MinUkcMeters,
    [property: Description("Zero-based leg of the minimum; null when none.")] int? MinUkcLeg,
    [property: Description("Why the last check failed, or null.")] string? Error,
    [property: Description("The cursor (and ghost vessel) distance from departure in nautical miles; null when hidden.")] double? CursorNm,
    [property: Description("The cursor's latitude; null when hidden.")] double? CursorLatitude,
    [property: Description("The cursor's longitude; null when hidden.")] double? CursorLongitude,
    [property: Description("The cursor's leg; null when hidden.")] int? CursorLeg,
    [property: Description("The ETA at the cursor (UTC); null when hidden or without ETAs.")] DateTimeOffset? CursorEta,
    [property: Description("The departure being checked (UTC): the route's departure, or the view time when it has none; null when Check route is off.")] DateTimeOffset? Departure,
    [property: Description("True when the route has no departure of its own, so the check uses the view time.")] bool DepartureIsViewTime,
    [property: Description("The departure window swept across the S-104 range (the verdict card's window line and the Clearance tab's band); null when Check route is off, before the first sweep, or when no S-104 covers the route.")] DepartureWindowDto? Window,
    [property: Description("True while the departure window is being swept again (after a leg, vessel or dataset change); window, if present, is the previous sweep.")] bool WindowComputing)
{
    /// <summary>Whether the clearance check ran and has a result.</summary>
    [Description("True when the clearance check ran and has a result (a draft is set). False without a draft, or before the first result.")]
    public bool ClearanceChecked { get; init; }

    /// <summary>The overall verdict's kind.</summary>
    [Description("The verdict card's overall verdict, worst group first: must_review (any check), data_ends (arrival after the water-level data ends, nothing to review), check, or clear; null when there is nothing to show yet.")]
    public string? VerdictKind { get; init; }

    /// <summary>The verdict's tone.</summary>
    [Description("The verdict card's tone: destructive (must_review only), warning, success or neutral; null when there is nothing to show yet.")]
    public string? VerdictTone { get; init; }

    /// <summary>The line under the title.</summary>
    [Description("The line under the verdict title, e.g. \"Leg 6 → 7 · Calshot Spit · and 2 more to review\" or, without a draft, the must-review findings named and \"2 to check\"; null when none.")]
    public string? VerdictSubtitle { get; init; }

    /// <summary>The summary rows.</summary>
    [Description("The verdict card's summary rows, in order: clearance, route and (when S-124 warnings were checked) warnings. Clicking a row filters the findings list (set_findings_view does the same).")]
    public IReadOnlyList<CheckSummaryRowDto> Summary { get; init; } = [];

    /// <summary>The footer.</summary>
    [Description("The verdict card's one footer, \"Not for navigation · computed from S-101, S-124 for your settings\" (every product the checks used).")]
    public string? Footer { get; init; }
}

/// <summary>A verdict-card summary row (#959, B3).</summary>
[Description("A verdict-card summary row: what one part of the check found.")]
internal sealed record CheckSummaryRowDto(
    [property: Description("Which row, and the findings filter clicking it sets: clearance, route or warnings.")] string Key,
    [property: Description("The row's label, e.g. \"Clearance\".")] string Label,
    [property: Description("The row's value, e.g. \"min 0.4 m · required 1.0 m\", \"2 to review · 1 to check\", \"2 in force on route · 1 near\", \"Nothing found\" or \"Not checked\".")] string Text,
    [property: Description("The chip, e.g. \"2 below\", \"tight\", \"meets\", \"no tide\" or a count; null when none.")] string? Chip,
    [property: Description("The chip's tone: destructive, warning, success or neutral; null without a chip.")] string? ChipTone,
    [property: Description("The link at the end of the row, \"+ Add draft\" on the clearance row without a draft; null otherwise.")] string? Link);

/// <summary>The departure window (#903).</summary>
[Description("The departure window: departures swept across the S-104 range that covers the route, the runs that meet your settings, and the departures that arrive after the data ends.")]
internal sealed record DepartureWindowDto(
    [property: Description("First swept departure, UTC (the first whole step at or after the S-104 data starts).")] DateTimeOffset From,
    [property: Description("Last swept departure, UTC (where the S-104 data ends).")] DateTimeOffset To,
    [property: Description("Minutes between swept departures (5, or wider over a long forecast).")] double StepMinutes,
    [property: Description("Runs of departures that meet your settings (tight and lower-confidence ones included), in time order.")] IReadOnlyList<DepartureRangeDto> Windows,
    [property: Description("Lower-confidence reasons among the departures that meet your settings (the caveat on the window line); empty when none.")] IReadOnlyList<string> Confidence,
    [property: Description("The departures whose arrival is after the S-104 data ends (hatched on the band; never meeting your settings), or null.")] DepartureRangeDto? ArrivalAfterDataEnds,
    [property: Description("True when the checked departure meets your settings and a window exists (\"This departure is inside the window\").")] bool InsideWindow,
    [property: Description("The meeting departure nearest the checked one, which Depart {HH:MM} jumps to; null when the checked departure is inside or nothing meets.")] DateTimeOffset? NearestMeetingDeparture,
    [property: Description("The verdict card's window line, e.g. \"Departures 05:35–06:40 meet your settings.\", with \" Lower confidence: datums unconfirmed.\" appended when meeting departures are lower confidence.")] string? Text,
    [property: Description("The Depart button's label, e.g. \"Depart 06:40\"; null when it isn't shown.")] string? DepartButton);

/// <summary>A run of swept departures.</summary>
[Description("A run of swept departures, first to last inclusive.")]
internal sealed record DepartureRangeDto(
    [property: Description("First departure of the run, UTC.")] DateTimeOffset First,
    [property: Description("Last departure of the run, UTC.")] DateTimeOffset Last);

/// <summary>Request for <see cref="SetDepartureTool"/>.</summary>
[Description("Request for set_departure: set the checked route's departure, or jump to the nearest departure that meets your settings.")]
internal sealed record SetDepartureRequest(
    [property: Description("The new departure (ISO-8601). Give this or nearestMeeting.")] DateTimeOffset? Departure = null,
    [property: Description("True to jump to the meeting departure nearest the current one, as the verdict card's Depart {HH:MM} button does. Needs Check route on.")] bool NearestMeeting = false,
    [property: Description("Route to set; omit for the active route. nearestMeeting always uses the checked (active) route.")] string? RouteId = null,
    [property: Description("Wait for the recomputed check (and window) before returning (default true).")] bool Wait = true);

/// <summary>Request for <see cref="SetCheckModeTool"/>.</summary>
[Description("Request for set_check_mode: turn Check route on or off.")]
internal sealed record SetCheckModeRequest(
    [property: Description("True to turn Check route on; false to turn it off.")] bool On,
    [property: Description("Route to check, made active first; omit for the active route.")] string? RouteId = null,
    [property: Description("When turning on, wait for the result before returning (default true).")] bool Wait = true);

/// <summary>Request for <see cref="SetPassageCursorTool"/>.</summary>
[Description("Request for set_passage_cursor: move the profile cursor and ghost vessel along the checked route.")]
internal sealed record SetPassageCursorRequest(
    [property: Description("Distance from departure in nautical miles (clamped to the route); null hides the cursor.")] double? DistanceNm);

/// <summary>Shared plumbing for the Check route tools.</summary>
internal abstract class PassageCheckToolBase : RouteToolBase
{
    /// <summary>How long a waiting tool waits for a result.</summary>
    internal static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(60);

    protected PassageCheckToolBase(RoutesService routes, IRouteEditInvoker invoker, PassageCheckService check, RouteFindingsService? findings = null)
        : base(routes, invoker)
    {
        ArgumentNullException.ThrowIfNull(check);
        Check = check;
        RouteFindings = findings;
    }

    protected PassageCheckService Check { get; }

    /// <summary>The findings list, which the verdict and summary rows count; <c>null</c> without one.</summary>
    protected RouteFindingsService? RouteFindings { get; }

    /// <summary>
    /// Waits for the scheduled recompute, then hops onto the UI thread so the
    /// result (posted there) has landed before the state is read.
    /// </summary>
    protected async Task SettleAsync(CancellationToken cancellationToken)
    {
        for (var i = 0; i < 4; i++)
        {
            var idle = await Invoker.InvokeAsync(() => Check.WhenIdle()).ConfigureAwait(false);
            await idle.WaitAsync(WaitTimeout, cancellationToken).ConfigureAwait(false);
            var computing = await Invoker.InvokeAsync(() => Check.IsComputing).ConfigureAwait(false);
            if (!computing)
                break;
        }

        if (RouteFindings is { } findings)
        {
            var idle = await Invoker.InvokeAsync(() => findings.WhenIdle()).ConfigureAwait(false);
            await idle.WaitAsync(WaitTimeout, cancellationToken).ConfigureAwait(false);
            // Results are posted to the UI thread; hop there so they have landed.
            await Invoker.InvokeAsync(() => true).ConfigureAwait(false);
        }
    }

    protected Task<ToolResult<CheckStateDto>> StateAsync() =>
        Invoker.InvokeAsync(() => ToolResult<CheckStateDto>.Ok(State(Routes, Check, RouteFindings)));

    internal static CheckStateDto State(RoutesService routes, PassageCheckService check, RouteFindingsService? findings = null)
    {
        var current = check.IsActive && check.HasDraft ? check.Current : null;
        var verdict = RouteCheckVerdict.For(check, findings, Pipelines.DepthUnit.Metres, TimeFormat.Utc);
        var result = current?.Result;
        var cursor = check.Cursor;
        var window = current is null ? null : check.Window;
        return new CheckStateDto(
            check.IsActive,
            check.IsActive ? routes.Routes.ActiveRoute?.Id : null,
            check.HasDraft,
            check.IsComputing,
            check.IsStale,
            result is null ? null : Verdict(result.Verdict),
            verdict?.Title,
            result is null ? [] : Mcp.Tools.GetPassageResultTool.ConfidenceCodes(result.Confidence),
            result is null ? null : PassageCheckViewModel.ConfidenceLine(result),
            result?.Minimum is { } min ? Math.Round(min.ClearanceMeters, 2) : null,
            result?.Minimum?.LegIndex,
            check.Error,
            cursor is { } c ? Math.Round(c.DistanceMeters / PassageGeodesy.MetersPerNauticalMile, 3) : null,
            cursor?.Position.Latitude,
            cursor?.Position.Longitude,
            cursor?.LegIndex,
            cursor?.Eta,
            check.Departure,
            check.IsActive && !check.HasRouteDeparture,
            window is null || current is null || window.RouteId != current.RouteId ? null : Window(window, check),
            check.IsActive && check.IsWindowComputing)
        {
            ClearanceChecked = result is not null,
            VerdictKind = verdict is null ? null : VerdictKind(verdict.Kind),
            VerdictTone = verdict is null ? null : Tone(verdict.Tone),
            VerdictSubtitle = verdict?.Subtitle,
            Summary = verdict?.Rows.Select(Row).ToList() ?? [],
            Footer = verdict?.Footer,
        };
    }

    internal static string VerdictKind(RouteCheckVerdictKind kind) => kind switch
    {
        RouteCheckVerdictKind.MustReview => "must_review",
        RouteCheckVerdictKind.DataEnds => "data_ends",
        RouteCheckVerdictKind.Check => "check",
        _ => "clear",
    };

    internal static string Tone(CheckTone tone) => tone switch
    {
        CheckTone.Success => "success",
        CheckTone.Warning => "warning",
        CheckTone.Destructive => "destructive",
        _ => "neutral",
    };

    internal static string Filter(FindingsFilter filter) => filter switch
    {
        FindingsFilter.Clearance => "clearance",
        FindingsFilter.Route => "route",
        FindingsFilter.Warnings => "warnings",
        _ => "all",
    };

    private static CheckSummaryRowDto Row(RouteCheckSummaryRow row) => new(
        Filter(row.Filter),
        row.Label,
        row.Text,
        row.Chip,
        row.Chip is null ? null : Tone(row.ChipTone),
        row.AddDraft ? Resources.Strings.Check_SummaryAddDraft : null);

    private static DepartureWindowDto Window(PassageDepartureWindow window, PassageCheckService check)
    {
        var line = DepartureWindowViewModel.Describe(check, TimeFormat.Utc);
        return new DepartureWindowDto(
            window.From,
            window.To,
            window.Step.TotalMinutes,
            window.Windows.Select(w => new DepartureRangeDto(w.First, w.Last)).ToList(),
            Mcp.Tools.GetPassageResultTool.ConfidenceCodes(window.Sweep.MeetingConfidence()),
            window.ArrivalAfterDataEnds is { } hatched ? new DepartureRangeDto(hatched.First, hatched.Last) : null,
            line?.IsInside ?? false,
            line?.Nearest,
            line?.Text,
            line?.DepartText);
    }

    internal static string Verdict(PassageVerdict verdict) => verdict switch
    {
        PassageVerdict.MeetsSettings => "meets_settings",
        PassageVerdict.MeetsSettingsTight => "meets_settings_tight",
        PassageVerdict.BelowSetting => "below_setting",
        PassageVerdict.IncompleteData => "incomplete_data",
        _ => "water_level_ends_before_arrival",
    };
}

/// <summary>
/// Turns Check route on or off for the active (or a named) route, as the
/// Routes panel's Check route toggle does, and returns what it shows.
/// </summary>
internal sealed class SetCheckModeTool : PassageCheckToolBase
{
    public const string Name = "set_check_mode";

    public SetCheckModeTool(RoutesService routes, IRouteEditInvoker invoker, PassageCheckService check, RouteFindingsService? findings = null)
        : base(routes, invoker, check, findings) { }

    public async Task<ToolResult<CheckStateDto>> InvokeAsync(SetCheckModeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var error = await Invoker.InvokeAsync<ToolError?>(() =>
        {
            if (!request.On)
            {
                Routes.IsCheckModeActive = false;
                return null;
            }

            var route = Resolve(request.RouteId, out var notFound);
            if (route is null)
                return notFound;
            if (!ReferenceEquals(route, Routes.Routes.ActiveRoute))
                Routes.Routes.SetActiveRoute(route);
            if (route.Legs.Count == 0)
                return new InvalidArgument("routeId", "the route needs at least two waypoints");

            Routes.IsCheckModeActive = true;
            return null;
        }).ConfigureAwait(false);
        if (error is not null)
            return ToolResult<CheckStateDto>.Err(error);

        if (request.On && request.Wait)
            await SettleAsync(cancellationToken).ConfigureAwait(false);
        return await StateAsync().ConfigureAwait(false);
    }
}

/// <summary>Returns what Check route is showing.</summary>
internal sealed class GetCheckStateTool : PassageCheckToolBase
{
    public const string Name = "get_check_state";

    public GetCheckStateTool(RoutesService routes, IRouteEditInvoker invoker, PassageCheckService check, RouteFindingsService? findings = null)
        : base(routes, invoker, check, findings) { }

    public async Task<ToolResult<CheckStateDto>> InvokeAsync(bool wait, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (wait)
            await SettleAsync(cancellationToken).ConfigureAwait(false);
        return await StateAsync().ConfigureAwait(false);
    }
}

/// <summary>Moves the profile cursor and the ghost vessel, for agents and UI tests.</summary>
internal sealed class SetPassageCursorTool : PassageCheckToolBase
{
    public const string Name = "set_passage_cursor";

    public SetPassageCursorTool(RoutesService routes, IRouteEditInvoker invoker, PassageCheckService check, RouteFindingsService? findings = null)
        : base(routes, invoker, check, findings) { }

    public async Task<ToolResult<CheckStateDto>> InvokeAsync(SetPassageCursorRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.DistanceNm is { } nm && (!double.IsFinite(nm) || nm < 0))
            return ToolResult<CheckStateDto>.Err(new InvalidArgument("distanceNm", "must be zero or more nautical miles"));

        await SettleAsync(cancellationToken).ConfigureAwait(false);
        var error = await Invoker.InvokeAsync<ToolError?>(() =>
        {
            if (request.DistanceNm is not null && Check.Current is null)
                return new InvalidArgument("distanceNm", "Check route is off or has no result; turn it on with set_check_mode");
            Check.SetCursor(request.DistanceNm * PassageGeodesy.MetersPerNauticalMile);
            return null;
        }).ConfigureAwait(false);
        return error is not null ? ToolResult<CheckStateDto>.Err(error) : await StateAsync().ConfigureAwait(false);
    }
}

/// <summary>
/// Sets the checked route's departure (#903), as dragging the Clearance
/// tab's departure band or pressing the verdict card's Depart {HH:MM} does.
/// One undoable edit.
/// </summary>
internal sealed class SetDepartureTool : PassageCheckToolBase
{
    public const string Name = "set_departure";

    public SetDepartureTool(RoutesService routes, IRouteEditInvoker invoker, PassageCheckService check, RouteFindingsService? findings = null)
        : base(routes, invoker, check, findings) { }

    public async Task<ToolResult<CheckStateDto>> InvokeAsync(SetDepartureRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if ((request.Departure is null) == !request.NearestMeeting)
            return ToolResult<CheckStateDto>.Err(new InvalidArgument("departure", "give either departure or nearestMeeting: true"));

        if (request.NearestMeeting)
        {
            if (request.RouteId is not null)
                return ToolResult<CheckStateDto>.Err(new InvalidArgument("routeId", "nearestMeeting uses the checked route; omit routeId"));
            await SettleAsync(cancellationToken).ConfigureAwait(false);
        }

        var error = await Invoker.InvokeAsync<ToolError?>(() =>
        {
            if (request.Departure is { } departure)
            {
                var route = Resolve(request.RouteId, out var notFound);
                if (route is null)
                    return notFound;
                route.EditCheckSettings(RouteEditLabels.EditPassage, () => route.SetCheckDeparture(departure));
                return null;
            }

            if (!Check.IsActive || Check.Current is not { } current)
                return new InvalidArgument("nearestMeeting", "Check route is off or has no result; turn it on with set_check_mode");
            if (Check.Window is not { } window || window.RouteId != current.RouteId)
                return new InvalidArgument("nearestMeeting", "there is no departure window: no S-104 covers the route");
            if (window.NearestMeeting(current.Result.Departure) is not { } nearest)
                return new InvalidArgument("nearestMeeting", "no departure in the S-104 range meets your settings");
            Check.SetDeparture(nearest);
            return null;
        }).ConfigureAwait(false);
        if (error is not null)
            return ToolResult<CheckStateDto>.Err(error);

        if (request.Wait)
            await SettleAsync(cancellationToken).ConfigureAwait(false);
        return await StateAsync().ConfigureAwait(false);
    }
}

/// <summary>Request for <see cref="SetFollowPassageTool"/>.</summary>
[Description("Request for set_follow_passage: turn Follow passage on or off.")]
internal sealed record SetFollowPassageRequest(
    [property: Description("True to pin the view time to the ETA at the Clearance cursor (or at the minimum UKC without a cursor); false to stop, leaving the view time where it is.")] bool On);

/// <summary>
/// Turns Follow passage on or off (#904), as the Timeline header's toggle
/// does, and returns the Timeline state.
/// </summary>
internal sealed class SetFollowPassageTool
{
    public const string Name = "set_follow_passage";

    private readonly FollowPassageService _follow;
    private readonly PassageCheckService _check;
    private readonly IRouteEditInvoker _invoker;
    private readonly IViewerTimelineController _timeline;

    public SetFollowPassageTool(FollowPassageService follow, PassageCheckService check, IRouteEditInvoker invoker, IViewerTimelineController timeline)
    {
        ArgumentNullException.ThrowIfNull(follow);
        ArgumentNullException.ThrowIfNull(check);
        ArgumentNullException.ThrowIfNull(invoker);
        ArgumentNullException.ThrowIfNull(timeline);
        _follow = follow;
        _check = check;
        _invoker = invoker;
        _timeline = timeline;
    }

    public async Task<ToolResult<TimelineStateDto>> InvokeAsync(SetFollowPassageRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        await SettleAsync(cancellationToken).ConfigureAwait(false);
        var applied = await _invoker.InvokeAsync(() => _follow.SetOn(request.On)).ConfigureAwait(false);
        if (!applied)
        {
            return ToolResult<TimelineStateDto>.Err(new InvalidArgument(
                "on", "Follow passage needs Check route on (set_check_mode) with a speed on every leg, and time-aware data loaded"));
        }

        // Turning it on may set the departure, which recomputes the check.
        await SettleAsync(cancellationToken).ConfigureAwait(false);
        return ToolResult<TimelineStateDto>.Ok(GetTimelineStateTool.ToDto(await _timeline.GetStateAsync(cancellationToken).ConfigureAwait(false)));
    }

    private async Task SettleAsync(CancellationToken cancellationToken)
    {
        var idle = await _invoker.InvokeAsync(() => _check.WhenIdle()).ConfigureAwait(false);
        await idle.WaitAsync(PassageCheckToolBase.WaitTimeout, cancellationToken).ConfigureAwait(false);
        await _invoker.InvokeAsync(() => true).ConfigureAwait(false);
    }
}

/// <summary>Wraps the Check route tools as <see cref="McpServerTool"/>s.</summary>
internal static class PassageCheckMcpAdapters
{
    private static readonly JsonSerializerOptions JsonOptions = McpAdapterShared.Options;

    public static McpServerTool Create(SetCheckModeTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("True to turn Check route on; false to turn it off.")] bool on,
            [Description("Route to check, made active first; omit for the active route.")] string? routeId = null,
            [Description("When turning on, wait for the result before returning (default true).")] bool wait = true,
            CancellationToken ct = default) =>
            McpAdapterShared.DispatchAsync(() => inner.InvokeAsync(new SetCheckModeRequest(on, routeId, wait), ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = SetCheckModeTool.Name,
            Description =
                "Turns Check route on or off, as the Routes panel's Check route toggle does. While it's on, the " +
                "map colours the active route by UKC state at ETA, folds Object Information to its edge tab and shows " +
                "the verdict card. The route needs at least two waypoints; without a draft (set_passage) only the route " +
                "checks run and the clearance parts are hidden (hasDraft false). Returns the state get_check_state returns; " +
                "use get_passage_result for the full numbers. Viewer-injected tool — mutates live state.",
            SerializerOptions = JsonOptions,
        });
    }

    public static McpServerTool Create(GetCheckStateTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("Wait for a pending recompute first (default true).")] bool wait = true,
            CancellationToken ct = default) =>
            McpAdapterShared.DispatchAsync(() => inner.InvokeAsync(wait, ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = GetCheckStateTool.Name,
            Description =
                "Returns what Check route is showing: on or off, whether a result is computing or stale, the verdict " +
                "card (one verdict over the clearance check and every route check: verdictKind, title, subtitle, the " +
                "clearance / route / warnings summary rows and the footer), the clearance verdict and minimum UKC when " +
                "a draft is set, the cursor (ghost vessel) position and ETA, the departure " +
                "being checked, and the departure window (the runs of departures that meet your settings, the departures " +
                "that arrive after the S-104 data ends, the window line and the Depart button).",
            SerializerOptions = JsonOptions,
        });
    }

    public static McpServerTool Create(SetDepartureTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("The new departure (ISO-8601). Give this or nearestMeeting.")] DateTimeOffset? departure = null,
            [Description("True to jump to the meeting departure nearest the current one, as the Depart {HH:MM} button does. Needs Check route on.")] bool nearestMeeting = false,
            [Description("Route to set; omit for the active route. Not allowed with nearestMeeting.")] string? routeId = null,
            [Description("Wait for the recomputed check and window before returning (default true).")] bool wait = true,
            CancellationToken ct = default) =>
            McpAdapterShared.DispatchAsync(() => inner.InvokeAsync(new SetDepartureRequest(departure, nearestMeeting, routeId, wait), ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = SetDepartureTool.Name,
            Description =
                "Sets the route's departure, as dragging the Clearance tab's departure band does, or with " +
                "nearestMeeting jumps to the departure nearest the current one that meets your settings, as the " +
                "verdict card's Depart {HH:MM} button does. One undoable edit (undo_route_edit). Returns the state " +
                "get_check_state returns, including the departure window. Viewer-injected tool — mutates live state.",
            SerializerOptions = JsonOptions,
        });
    }

    public static McpServerTool Create(SetFollowPassageTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("True to pin the view time to the ETA at the Clearance cursor; false to stop, leaving the view time where it is.")] bool on,
            CancellationToken ct = default) =>
            McpAdapterShared.DispatchAsync(() => inner.InvokeAsync(new SetFollowPassageRequest(on), ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = SetFollowPassageTool.Name,
            Description =
                "Turns Follow passage on or off, as the Timeline header's toggle does. While it's on, the view time is " +
                "pinned to the ETA at the Clearance cursor (set_passage_cursor), or at the minimum UKC without a cursor, " +
                "so S-104 and S-111 show what the ship meets there. Turning it on pins the Timeline and, when the route " +
                "has no departure, sets the departure to the one being checked (undoable). Choosing another time " +
                "(set_view_time, step_time) or turning Check route off stops it. Needs Check route on with ETAs. " +
                "Returns the state get_timeline_state returns. Viewer-injected tool — mutates live state.",
            SerializerOptions = JsonOptions,
        });
    }

    public static McpServerTool Create(SetPassageCursorTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("Distance from departure in nautical miles (clamped to the route); omit or null to hide the cursor.")] double? distanceNm = null,
            CancellationToken ct = default) =>
            McpAdapterShared.DispatchAsync(() => inner.InvokeAsync(new SetPassageCursorRequest(distanceNm), ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = SetPassageCursorTool.Name,
            Description =
                "Moves the Check route cursor and the ghost vessel to a distance along the checked route, as hovering " +
                "a leg row or the clearance profile does. Returns the state, including the cursor's position and ETA. " +
                "Viewer-injected tool — mutates live state.",
            SerializerOptions = JsonOptions,
        });
    }
}

/// <summary>What the Clearance tab shows (#902).</summary>
[Description("Clearance tab state: the profile's x axis, whether Sources is open, the readout at the cursor and the Sources groups.")]
internal sealed record ClearanceStateDto(
    [property: Description("True when there is a profile (Check route is on and has a result).")] bool HasProfile,
    [property: Description("The profile's x axis: \"distance\" (NM) or \"time\" (ETA).")] string Axis,
    [property: Description("True when the Time axis is available (the passage has ETAs).")] bool CanShowTime,
    [property: Description("True when the Sources disclosure is open in place of the readout.")] bool SourcesOpen,
    [property: Description("The readout's sample index (as in get_passage_result's samples), or null.")] int? ReadoutSample,
    [property: Description("Readout position, \"{NM} · {ETA}\".")] string? ReadoutPosition,
    [property: Description("Readout leg and place name.")] string? ReadoutLeg,
    [property: Description("Charted depth, formatted.")] string? Depth,
    [property: Description("Charted depth source, e.g. \"S-102 · {cell} · ±0.3 m\" or \"S-101 DepthArea · lower confidence\".")] string? DepthSource,
    [property: Description("Water level, formatted.")] string? WaterLevel,
    [property: Description("Water level source, e.g. \"S-104 · {station} · {step}\" or \"No S-104 at this ETA · tide not applied\".")] string? WaterLevelSource,
    [property: Description("Draft + squat, formatted.")] string? DraftSquat,
    [property: Description("UKC at the readout, formatted.")] string? Ukc,
    [property: Description("The verdict words for the readout's state, with \" · lower confidence\" when the sample is, e.g. \"Meets your settings · lower confidence\".")] string? State,
    [property: Description("The readout's clearance state code: meets_settings, tight, below_setting or no_data (as get_passage_result's samples[].state); null without a readout.")] string? Band,
    [property: Description("The readout sample's lower-confidence reasons: charted_depth_from_s101 and/or datum_unconfirmed; empty at full confidence or without a readout.")] IReadOnlyList<string> Confidence,
    [property: Description("The Sources groups: title and lines.")] IReadOnlyList<ClearanceSourcesGroupDto> Sources,
    [property: Description("The departure band under the profile; null when there is no departure window.")] DepartureBandDto? DepartureBand);

/// <summary>The Clearance tab's departure band (#903, D5).</summary>
[Description("The departure band: runs of swept departures across the S-104 range, the knob at the current departure, and the axis labels.")]
internal sealed record DepartureBandDto(
    [property: Description("The current departure as shown beside the band, e.g. \"07:10\".")] string? Departure,
    [property: Description("Where the knob sits across the band, 0 (left) to 1 (right); null without a departure.")] double? KnobFraction,
    [property: Description("Start label (the first departure).")] string? StartLabel,
    [property: Description("Middle label.")] string? MiddleLabel,
    [property: Description("End label, e.g. \"12:00 · S-104 ends\".")] string? EndLabel,
    [property: Description("True while the band is being swept again (dimmed).")] bool Stale,
    [property: Description("The runs, left to right.")] IReadOnlyList<DepartureBandSegmentDto> Segments);

/// <summary>A run on the departure band.</summary>
[Description("A run of departures of one kind on the departure band.")]
internal sealed record DepartureBandSegmentDto(
    [property: Description("meets_settings (success), below_setting (destructive, 35 %), arrival_after_data_ends (hatched) or no_data (muted).")] string Kind,
    [property: Description("First departure of the run, UTC.")] DateTimeOffset First,
    [property: Description("Last departure of the run, UTC.")] DateTimeOffset Last,
    [property: Description("Left edge across the band, 0–1.")] double Start,
    [property: Description("Right edge across the band, 0–1.")] double End);

/// <summary>One Sources group.</summary>
internal sealed record ClearanceSourcesGroupDto(
    [property: Description("The group heading, e.g. \"S-102 cells\".")] string Title,
    [property: Description("The group's lines.")] IReadOnlyList<string> Items);

/// <summary>Request for <see cref="SetClearanceViewTool"/>.</summary>
[Description("Request for set_clearance_view: switch the Clearance profile's axis and open or close Sources.")]
internal sealed record SetClearanceViewRequest(
    [property: Description("\"distance\" or \"time\"; omit to leave unchanged.")] string? Axis = null,
    [property: Description("True to open Sources in place of the readout, false to close it; omit to leave unchanged.")] bool? SourcesOpen = null);

/// <summary>Reads and sets the Clearance tab's view state (#902).</summary>
internal sealed class ClearanceTools
{
    public const string GetName = "get_clearance_state";
    public const string SetName = "set_clearance_view";

    private readonly ClearanceViewModel _clearance;
    private readonly IRouteEditInvoker _invoker;

    public ClearanceTools(ClearanceViewModel clearance, IRouteEditInvoker invoker)
    {
        ArgumentNullException.ThrowIfNull(clearance);
        ArgumentNullException.ThrowIfNull(invoker);
        _clearance = clearance;
        _invoker = invoker;
    }

    public Task<ToolResult<ClearanceStateDto>> GetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _invoker.InvokeAsync(() => ToolResult<ClearanceStateDto>.Ok(State(_clearance)));
    }

    public Task<ToolResult<ClearanceStateDto>> SetAsync(SetClearanceViewRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        ClearanceAxis? axis = request.Axis?.Trim().ToLowerInvariant() switch
        {
            null or "" => null,
            "distance" => ClearanceAxis.Distance,
            "time" => ClearanceAxis.Time,
            _ => (ClearanceAxis)(-1),
        };
        if (axis is (ClearanceAxis)(-1))
            return Task.FromResult(ToolResult<ClearanceStateDto>.Err(new InvalidArgument("axis", "must be \"distance\" or \"time\"")));

        return _invoker.InvokeAsync(() =>
        {
            if (axis == ClearanceAxis.Time && !_clearance.CanShowTime)
                return ToolResult<ClearanceStateDto>.Err(new InvalidArgument("axis", "the passage has no ETAs; set a speed first"));
            if (axis is { } a)
                _clearance.Axis = a;
            if (request.SourcesOpen is { } open)
                _clearance.IsSourcesOpen = open;
            return ToolResult<ClearanceStateDto>.Ok(State(_clearance));
        });
    }

    internal static ClearanceStateDto State(ClearanceViewModel c) => new(
        c.HasProfile,
        c.Axis == ClearanceAxis.Time ? "time" : "distance",
        c.CanShowTime,
        c.IsSourcesOpen,
        c.HasReadout ? c.ReadoutSample?.Index : null,
        c.HasReadout ? c.ReadoutPosition : null,
        c.HasReadout ? c.ReadoutLeg : null,
        c.HasReadout ? c.DepthText : null,
        c.HasReadout ? c.DepthSource : null,
        c.HasReadout ? c.WaterLevelText : null,
        c.HasReadout ? c.WaterLevelSource : null,
        c.HasReadout ? c.DraftText : null,
        c.HasReadout ? c.UkcText : null,
        c.HasReadout ? c.StateText : null,
        c.HasReadout && c.ReadoutSample is { } band ? Mcp.Tools.GetPassageResultTool.State(band.State) : null,
        c.HasReadout && c.ReadoutSample is { } sample ? Mcp.Tools.GetPassageResultTool.ConfidenceCodes(sample.Confidence) : [],
        c.SourceGroups.Select(g => new ClearanceSourcesGroupDto(g.Title, g.Items)).ToList(),
        c.HasBand
            ? new DepartureBandDto(
                c.DepartureText,
                c.BandKnobFraction is { } f ? Math.Round(f, 4) : null,
                c.BandStartLabel,
                c.BandMiddleLabel,
                c.BandEndLabel,
                c.IsBandStale,
                c.BandSegments.Select(Segment).ToList())
            : null);

    internal static DepartureBandSegmentDto Segment(DepartureBandSegment s) =>
        new(BandKind(s.Kind), s.First, s.Last, Math.Round(s.Start, 4), Math.Round(s.End, 4));

    private static string BandKind(DepartureBandKind kind) => kind switch
    {
        DepartureBandKind.Meets => "meets_settings",
        DepartureBandKind.Below => "below_setting",
        DepartureBandKind.ArrivalAfterDataEnds => "arrival_after_data_ends",
        _ => "no_data",
    };

    public McpServerTool CreateGet()
    {
        var del = (CancellationToken ct = default) => McpAdapterShared.DispatchAsync(() => GetAsync(ct));
        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = GetName,
            Description =
                "Returns what the Clearance tab (bottom dock, while Check route is on) shows: the profile's axis " +
                "(distance or time), whether Sources is open, the readout at the cursor (position, leg, charted depth " +
                "and its source cell, water level and its station or grid and time step, draft + squat, UKC and state), " +
                "the Sources groups, and the departure band (runs of departures across the S-104 range, the knob and " +
                "labels). Move the cursor with set_passage_cursor and the departure with set_departure; open the tab " +
                "with set_panel Clearance.",
            SerializerOptions = McpAdapterShared.Options,
        });
    }

    public McpServerTool CreateSet()
    {
        var del = (
            [Description("\"distance\" or \"time\"; omit to leave unchanged.")] string? axis = null,
            [Description("True to open Sources, false to close it; omit to leave unchanged.")] bool? sourcesOpen = null,
            CancellationToken ct = default) =>
            McpAdapterShared.DispatchAsync(() => SetAsync(new SetClearanceViewRequest(axis, sourcesOpen), ct));
        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = SetName,
            Description =
                "Switches the Clearance profile between distance and time on its x axis, and opens or closes the " +
                "Sources disclosure, as the Clearance tab's header controls do. The cursor stays on the same sample. " +
                "Returns the state get_clearance_state returns. Viewer-injected tool — mutates live state.",
            SerializerOptions = McpAdapterShared.Options,
        });
    }
}
