using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.McpTools;

// Viewer-only tools that let an agent read and drive the Timeline, a loaded
// dataset's display state, the notifications and (with --mcp-test-hooks) the
// clock, through the same view models the UI uses (#715 slice 1).

// ---------------------------------------------------------------------------
// Wire types
// ---------------------------------------------------------------------------

/// <summary>A window of the Timeline in which some layer draws.</summary>
[Description("A window of the Timeline in which at least one layer draws.")]
internal sealed record TimeWindowDto(
    [property: Description("Start of the window, UTC ISO-8601.")] DateTime Start,
    [property: Description("End of the window, UTC ISO-8601.")] DateTime End);

/// <summary>A gap collapsed on the Timeline's axis.</summary>
[Description("A stretch with no data, collapsed on the Timeline's axis.")]
internal sealed record TimelineGapDto(
    [property: Description("Start of the gap, UTC ISO-8601.")] DateTime From,
    [property: Description("End of the gap, UTC ISO-8601.")] DateTime To,
    [property: Description("Its length as the axis labels it, e.g. '6 wk'.")] string Length);

/// <summary>What one time-aware layer draws at the view time.</summary>
[Description("What one time-aware layer draws at the view time.")]
internal sealed record TimelineLayerDto(
    [property: Description("Dataset id, as list_datasets reports it.")] string Id,
    [property: Description("Product specification, e.g. 'S-111'.")] string Spec,
    [property: Description("True when the user has the layer switched on (set_dataset_state changes it).")] bool Visible,
    [property: Description("The sample the layer draws, UTC ISO-8601; null when the layer has no data within its tolerance of the view time and hides.")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] DateTime? DrawnTime,
    [property: Description("The latest sample at or before the view time, UTC ISO-8601, or null.")] DateTime? PreviousSample,
    [property: Description("The earliest sample after the view time, UTC ISO-8601, or null.")] DateTime? NextSample,
    [property: Description("Number of samples in the layer.")] int SampleCount,
    [property: Description("The layer time as its row in the Datasets list shows it: '08:00Z · T+20 h', '20:06Z (−24 min)', 'no data · last 18:00Z, 6 h earlier', 'drawing…'; null for none.")] string? Time,
    [property: Description("True when the layer has no data within its tolerance of the view time and hides (its row's Hidden tag).")] bool Hidden,
    [property: Description("True while the layer is drawing the view time.")] bool Drawing);

/// <summary>The Timeline as the user sees it.</summary>
[Description("The viewer's Timeline: mode, now, view time, loaded range and what each time-aware layer draws.")]
internal sealed record TimelineStateDto(
    [property: Description("True when at least one time-aware dataset is loaded.")] bool Active,
    [property: Description("'live' while the view time follows now; 'pinned' when the user (or an agent) chose a time.")] string Mode,
    [property: Description("The viewer's current time, UTC ISO-8601 (moved by set_test_clock when test hooks are on).")] DateTime Now,
    [property: Description("The time the map shows, UTC ISO-8601, or null when inactive.")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] DateTime? ViewTime,
    [property: Description("Earliest loaded sample, UTC ISO-8601, or null.")] DateTime? Minimum,
    [property: Description("Latest loaded sample, UTC ISO-8601, or null.")] DateTime? Maximum,
    [property: Description("Number of distinct loaded samples across all layers.")] int SampleCount,
    [property: Description("Merged windows in which some layer draws; the spaces between them are gaps.")] IReadOnlyList<TimeWindowDto> Coverage,
    [property: Description("Forecast runs loaded, e.g. 'cbofs 12:00Z'.")] IReadOnlyList<string> Runs,
    [property: Description("True when now lies inside a loaded window (some layer has data now).")] bool NowInCoverage,
    [property: Description("True when every loaded forecast has ended.")] bool ForecastEnded,
    [property: Description("The Timeline's time readout as displayed (user's Local/UTC setting).")] string Readout,
    [property: Description("The Timeline's range summary as displayed.")] string Summary,
    [property: Description("The view time's offset from now as displayed: 'now', 'in 11 h 30', '5 h ago'.")] string Offset,
    [property: Description("The status line's message, e.g. 'Every forecast ended 10 h ago' or 'No data at this time for 1 of 2 layers'; null when all is well.")] string? Message,
    [property: Description("The message's action as displayed: 'Check for new runs', 'Next data ›' or '‹ Previous data'; null for none.")] string? MessageAction,
    [property: Description("The window the axis shows (set_timeline_view changes it).")] TimeWindowDto? Window,
    [property: Description("The window's preset as displayed: 'All loaded', 'Now ± 6 h', 'Today', 'Next 48 h', 'This run' or 'Custom'.")] string Preset,
    [property: Description("What the Timeline's ‹ › step by: 'ten_minutes', 'hour', 'six_hours', 'day', 'sample', 'boundary' or 'data'.")] string Step,
    [property: Description("The layer whose samples 'sample' steps follow and whose ticks show, or null.")] string? StepDriver,
    [property: Description("The gaps collapsed on the axis, with their length as labelled ('6 wk').")] IReadOnlyList<TimelineGapDto> Gaps,
    [property: Description("The time-aware layers in Datasets-list order. Layer times settle after the map's time refresh; call await_render_idle after set_view_time before reading them.")] IReadOnlyList<TimelineLayerDto> Layers);

/// <summary>A loaded dataset's display state before and after set_dataset_state.</summary>
[Description("A loaded dataset's display state before and after a change.")]
internal sealed record DatasetStateDto(
    [property: Description("Dataset id, as list_datasets reports it.")] string Id,
    [property: Description("Product specification, e.g. 'S-104'.")] string Spec,
    [property: Description("Whether the dataset draws, after the call.")] bool Visible,
    [property: Description("Dataset opacity in 0..1, after the call.")] double Opacity,
    [property: Description("Whether the dataset drew, before the call.")] bool PreviousVisible,
    [property: Description("Dataset opacity in 0..1, before the call.")] double PreviousOpacity,
    [property: Description("True when the call changed anything.")] bool Changed);

/// <summary>A notification as the user sees it.</summary>
[Description("A notification shown in the viewer.")]
internal sealed record NotificationDto(
    [property: Description("Notification id; pass it to dismiss_notification.")] Guid Id,
    [property: Description("'info', 'success', 'warning' or 'error'.")] string Severity,
    [property: Description("Title text.")] string Title,
    [property: Description("Body text, or null.")] string? Message,
    [property: Description("When it was raised, UTC ISO-8601.")] DateTimeOffset CreatedUtc,
    [property: Description("True when it stays until dismissed.")] bool Persistent,
    [property: Description("Labels of its action buttons.")] IReadOnlyList<string> Actions);

/// <summary>The notifications on screen.</summary>
[Description("The notifications on screen, oldest first.")]
internal sealed record NotificationListDto(
    [property: Description("The active notifications, oldest first.")] IReadOnlyList<NotificationDto> Notifications);

/// <summary>Result of dismiss_notification.</summary>
[Description("The notifications dismissed.")]
internal sealed record DismissNotificationsDto(
    [property: Description("Ids of the notifications dismissed.")] IReadOnlyList<Guid> Dismissed);

/// <summary>Result of set_test_clock.</summary>
[Description("The viewer's test clock after the call.")]
internal sealed record TestClockDto(
    [property: Description("The viewer's now, UTC ISO-8601.")] DateTimeOffset Now,
    [property: Description("True when the clock is frozen.")] bool Frozen,
    [property: Description("Offset from the real clock in seconds while running (0 when frozen or reset).")] double OffsetSeconds);

/// <summary>set_view_time could not apply the requested time.</summary>
[Description("Raised when set_view_time cannot apply the requested time, e.g. no time-aware dataset is loaded.")]
internal sealed record ViewTimeNotApplied(
    [property: Description("Why the time was not applied.")] string Reason)
    : ToolError("view_time_not_applied", $"The view time was not changed: {Reason}.");

/// <summary>No notification has the given id.</summary>
[Description("Raised when no notification on screen has the requested id (call list_notifications).")]
internal sealed record NotificationNotFound(
    [property: Description("The notification id that could not be resolved.")] Guid Id)
    : ToolError("notification_not_found", $"No notification with id '{Id}' is on screen.");

// ---------------------------------------------------------------------------
// get_timeline_state / set_view_time
// ---------------------------------------------------------------------------

/// <summary>Reads the Timeline (MCP <c>get_timeline_state</c>).</summary>
internal sealed class GetTimelineStateTool(IViewerTimelineController timeline)
{
    /// <summary>The MCP tool name.</summary>
    public const string Name = "get_timeline_state";

    private readonly IViewerTimelineController _timeline = timeline ?? throw new ArgumentNullException(nameof(timeline));

    /// <summary>Snapshots the Timeline.</summary>
    public async Task<ToolResult<TimelineStateDto>> InvokeAsync(CancellationToken ct = default) =>
        ToolResult<TimelineStateDto>.Ok(ToDto(await _timeline.GetStateAsync(ct).ConfigureAwait(false)));

    /// <summary>The wire name of a step kind: "hour", "ten_minutes", "sample", ….</summary>
    internal static string StepName(TimelineStepKind kind) => kind switch
    {
        TimelineStepKind.TenMinutes => "ten_minutes",
        TimelineStepKind.SixHours => "six_hours",
        _ => kind.ToString().ToLowerInvariant(),
    };

    internal static TimelineStateDto ToDto(ViewerTimelineState state) => new(
        state.Active,
        state.FollowingNow ? "live" : "pinned",
        state.Now,
        state.ViewTime,
        state.Minimum,
        state.Maximum,
        state.SampleCount,
        [.. state.Coverage.Select(segment => new TimeWindowDto(segment.Start, segment.End))],
        state.Runs,
        state.NowInCoverage,
        state.ForecastEnded,
        state.Readout,
        state.Summary,
        state.Offset,
        state.Message,
        state.MessageAction,
        state.WindowStart is { } ws && state.WindowEnd is { } we ? new TimeWindowDto(ws, we) : null,
        state.Preset,
        StepName(state.Step),
        state.StepDriver,
        [.. state.Gaps.Select(g => new TimelineGapDto(g.From, g.To, TimelineAxisLabels.GapLength(g.Length, CultureInfo.InvariantCulture)))],
        [.. state.Layers.Select(layer => new TimelineLayerDto(
            layer.Id, layer.Spec, layer.Visible, layer.DrawnTime, layer.PreviousSample, layer.NextSample, layer.SampleCount, layer.Time, layer.Hidden, layer.Drawing))]);
}

/// <summary>Request for <see cref="SetViewTimeTool"/>.</summary>
internal sealed record SetViewTimeRequest(string Time, string? Snap);

/// <summary>
/// Moves the Timeline's view time (MCP <c>set_view_time</c>): to now (as the
/// Now button does, which then follows now), to an absolute time, or by an
/// offset from the current view time.
/// </summary>
internal sealed partial class SetViewTimeTool(IViewerTimelineController timeline)
{
    /// <summary>The MCP tool name.</summary>
    public const string Name = "set_view_time";

    private readonly IViewerTimelineController _timeline = timeline ?? throw new ArgumentNullException(nameof(timeline));

    /// <summary>Applies the request.</summary>
    public async Task<ToolResult<TimelineStateDto>> InvokeAsync(SetViewTimeRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var nearest = request.Snap?.Trim().ToLowerInvariant() switch
        {
            null or "" or "exact" => false,
            "nearest" => true,
            _ => (bool?)null,
        };
        if (nearest is null)
            return ToolResult<TimelineStateDto>.Err(new InvalidArgument("snap", "expected 'exact' or 'nearest'"));

        var text = request.Time?.Trim() ?? string.Empty;
        DateTime? target;
        if (string.Equals(text, "now", StringComparison.OrdinalIgnoreCase))
        {
            target = null;
        }
        else if (TryParseOffset(text, out var offset))
        {
            var current = await _timeline.GetStateAsync(ct).ConfigureAwait(false);
            if (current.ViewTime is not { } viewTime)
                return ToolResult<TimelineStateDto>.Err(new ViewTimeNotApplied("no time-aware dataset is loaded"));
            target = viewTime + offset;
        }
        else if (DateTime.TryParse(text, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var absolute))
        {
            target = absolute;
        }
        else
        {
            return ToolResult<TimelineStateDto>.Err(new InvalidArgument(
                "time", "expected 'now', an ISO-8601 time, or an offset such as '+6h', '-30m' or '+1d'"));
        }

        var outcome = await _timeline.SetViewTimeAsync(target, nearest.Value, ct).ConfigureAwait(false);
        return outcome.Applied
            ? ToolResult<TimelineStateDto>.Ok(GetTimelineStateTool.ToDto(outcome.State))
            : ToolResult<TimelineStateDto>.Err(new ViewTimeNotApplied(outcome.Reason ?? "unknown"));
    }

    /// <summary>Parses '+6h', '-30m', '+1d', '+90s', '+1.5h'.</summary>
    internal static bool TryParseOffset(string text, out TimeSpan offset)
    {
        offset = TimeSpan.Zero;
        var match = OffsetPattern().Match(text);
        if (!match.Success)
            return false;
        var amount = double.Parse(match.Groups["n"].Value, CultureInfo.InvariantCulture);
        if (match.Groups["sign"].Value == "-")
            amount = -amount;
        offset = match.Groups["unit"].Value switch
        {
            "s" => TimeSpan.FromSeconds(amount),
            "m" => TimeSpan.FromMinutes(amount),
            "h" => TimeSpan.FromHours(amount),
            _ => TimeSpan.FromDays(amount),
        };
        return true;
    }

    [GeneratedRegex(@"^(?<sign>[+-])(?<n>\d+(\.\d+)?)(?<unit>[smhd])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OffsetPattern();
}

// ---------------------------------------------------------------------------
// set_dataset_state
// ---------------------------------------------------------------------------

/// <summary>Request for <see cref="SetDatasetStateTool"/>.</summary>
internal sealed record SetDatasetStateRequest(string DatasetId, bool? Visible, double? Opacity);

/// <summary>
/// Shows or hides a loaded dataset and sets its opacity (MCP
/// <c>set_dataset_state</c>), as the Datasets list's eye icon does. With
/// neither value supplied it only reports the current state.
/// </summary>
internal sealed class SetDatasetStateTool(IViewerDatasetStateController datasets)
{
    /// <summary>The MCP tool name.</summary>
    public const string Name = "set_dataset_state";

    private readonly IViewerDatasetStateController _datasets = datasets ?? throw new ArgumentNullException(nameof(datasets));

    /// <summary>Applies the request.</summary>
    public async Task<ToolResult<DatasetStateDto>> InvokeAsync(SetDatasetStateRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.DatasetId))
            return ToolResult<DatasetStateDto>.Err(new InvalidArgument("datasetId", "value is required; call list_datasets for the ids"));
        if (request.Opacity is { } opacity && (double.IsNaN(opacity) || opacity < 0 || opacity > 1))
            return ToolResult<DatasetStateDto>.Err(new InvalidArgument("opacity", "must be between 0 and 1"));

        var id = request.DatasetId.Trim();
        var outcome = await _datasets.SetStateAsync(id, request.Visible, request.Opacity, ct).ConfigureAwait(false);
        if (outcome is null)
            return ToolResult<DatasetStateDto>.Err(new DatasetNotFound(new DatasetId(id)));
        return ToolResult<DatasetStateDto>.Ok(new DatasetStateDto(
            outcome.Id,
            outcome.Spec,
            outcome.Visible,
            outcome.Opacity,
            outcome.PreviousVisible,
            outcome.PreviousOpacity,
            outcome.Visible != outcome.PreviousVisible || outcome.Opacity != outcome.PreviousOpacity));
    }
}

// ---------------------------------------------------------------------------
// list_notifications / dismiss_notification
// ---------------------------------------------------------------------------

/// <summary>Lists the notifications on screen (MCP <c>list_notifications</c>).</summary>
internal sealed class ListNotificationsTool(IViewerNotificationController notifications)
{
    /// <summary>The MCP tool name.</summary>
    public const string Name = "list_notifications";

    private readonly IViewerNotificationController _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));

    /// <summary>Lists them.</summary>
    public async Task<ToolResult<NotificationListDto>> InvokeAsync(CancellationToken ct = default)
    {
        var list = await _notifications.ListAsync(ct).ConfigureAwait(false);
        return ToolResult<NotificationListDto>.Ok(new NotificationListDto(
            [.. list.Select(n => new NotificationDto(n.Id, n.Severity, n.Title, n.Message, n.CreatedUtc, n.Persistent, n.Actions))]));
    }
}

/// <summary>Request for <see cref="DismissNotificationTool"/>.</summary>
internal sealed record DismissNotificationRequest(string? Id);

/// <summary>
/// Dismisses one notification, or all of them (MCP
/// <c>dismiss_notification</c>), as the user's × does.
/// </summary>
internal sealed class DismissNotificationTool(IViewerNotificationController notifications)
{
    /// <summary>The MCP tool name.</summary>
    public const string Name = "dismiss_notification";

    private readonly IViewerNotificationController _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));

    /// <summary>Applies the request.</summary>
    public async Task<ToolResult<DismissNotificationsDto>> InvokeAsync(DismissNotificationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Guid? id = null;
        if (!string.IsNullOrWhiteSpace(request.Id) && !string.Equals(request.Id.Trim(), "all", StringComparison.OrdinalIgnoreCase))
        {
            if (!Guid.TryParse(request.Id.Trim(), out var parsed))
                return ToolResult<DismissNotificationsDto>.Err(new InvalidArgument("id", "expected a notification id from list_notifications, or 'all'"));
            id = parsed;
        }

        var dismissed = await _notifications.DismissAsync(id, ct).ConfigureAwait(false);
        if (id is { } wanted && dismissed.Count == 0)
            return ToolResult<DismissNotificationsDto>.Err(new NotificationNotFound(wanted));
        return ToolResult<DismissNotificationsDto>.Ok(new DismissNotificationsDto(dismissed));
    }
}

// ---------------------------------------------------------------------------
// set_test_clock (only with --mcp-test-hooks)
// ---------------------------------------------------------------------------

/// <summary>Request for <see cref="SetTestClockTool"/>.</summary>
internal sealed record SetTestClockRequest(string? Now, string? Advance, bool? Freeze, bool? Reset);

/// <summary>
/// Moves, freezes or resets the viewer's notion of now (MCP
/// <c>set_test_clock</c>). Registered only with <c>--mcp-test-hooks</c>.
/// </summary>
internal sealed class SetTestClockTool(AdjustableTimeProvider clock)
{
    /// <summary>The MCP tool name.</summary>
    public const string Name = "set_test_clock";

    private readonly AdjustableTimeProvider _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    /// <summary>Applies the request.</summary>
    public ToolResult<TestClockDto> Invoke(SetTestClockRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var hasNow = !string.IsNullOrWhiteSpace(request.Now);
        var hasAdvance = !string.IsNullOrWhiteSpace(request.Advance);
        var reset = request.Reset == true;
        if ((hasNow ? 1 : 0) + (hasAdvance ? 1 : 0) + (reset ? 1 : 0) > 1)
            return ToolResult<TestClockDto>.Err(new InvalidArgument("now", "supply at most one of 'now', 'advance' and 'reset'"));

        if (reset)
        {
            _clock.Reset();
        }
        else if (hasNow)
        {
            if (!DateTimeOffset.TryParse(request.Now, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var now))
                return ToolResult<TestClockDto>.Err(new InvalidArgument("now", "expected an ISO-8601 time"));
            _clock.SetNow(now.ToUniversalTime(), request.Freeze == true);
        }
        else if (hasAdvance)
        {
            if (!SetViewTimeTool.TryParseOffset(request.Advance!.Trim(), out var delta))
                return ToolResult<TestClockDto>.Err(new InvalidArgument("advance", "expected an offset such as '+1h', '-30m' or '+2d'"));
            _clock.Advance(delta);
        }
        else if (request.Freeze is { } freeze)
        {
            // Freeze (or release) at the current now.
            _clock.SetNow(_clock.GetUtcNow(), freeze);
        }

        return ToolResult<TestClockDto>.Ok(new TestClockDto(
            _clock.GetUtcNow(), _clock.FrozenAt is not null, _clock.Offset.TotalSeconds));
    }
}

// ---------------------------------------------------------------------------
// step_time / set_timeline_view (#708, #715)
// ---------------------------------------------------------------------------

/// <summary>Request for <see cref="StepTimeTool"/>.</summary>
internal sealed record StepTimeRequest(string Direction, string? Unit, int? Count);

/// <summary>Steps the Timeline as ‹ › and the arrow keys do (MCP <c>step_time</c>).</summary>
internal sealed class StepTimeTool(IViewerTimelineController timeline)
{
    /// <summary>The MCP tool name.</summary>
    public const string Name = "step_time";

    private readonly IViewerTimelineController _timeline = timeline ?? throw new ArgumentNullException(nameof(timeline));

    /// <summary>Applies the request.</summary>
    public async Task<ToolResult<TimelineStateDto>> InvokeAsync(StepTimeRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var direction = request.Direction?.Trim().ToLowerInvariant() switch
        {
            "next" or "forward" or "+1" => +1,
            "previous" or "prev" or "back" or "-1" => -1,
            _ => 0,
        };
        if (direction == 0)
            return ToolResult<TimelineStateDto>.Err(new InvalidArgument("direction", "expected 'next' or 'previous'"));

        TimelineStepKind? kind = request.Unit?.Trim().ToLowerInvariant() switch
        {
            null or "" or "current" => null,
            "10min" or "ten_minutes" => TimelineStepKind.TenMinutes,
            "1h" or "hour" => TimelineStepKind.Hour,
            "6h" or "six_hours" => TimelineStepKind.SixHours,
            "1d" or "day" => TimelineStepKind.Day,
            "sample" => TimelineStepKind.Sample,
            "boundary" => TimelineStepKind.Boundary,
            "data" => TimelineStepKind.Data,
            _ => (TimelineStepKind)(-1),
        };
        if (kind is (TimelineStepKind)(-1))
            return ToolResult<TimelineStateDto>.Err(new InvalidArgument("unit", "expected 10min, 1h, 6h, 1d, sample, boundary, data or current"));

        var count = request.Count ?? 1;
        if (count is < 1 or > 1000)
            return ToolResult<TimelineStateDto>.Err(new InvalidArgument("count", "must be between 1 and 1000"));

        var outcome = await _timeline.StepAsync(kind, direction, count, ct).ConfigureAwait(false);
        return outcome.Applied
            ? ToolResult<TimelineStateDto>.Ok(GetTimelineStateTool.ToDto(outcome.State))
            : ToolResult<TimelineStateDto>.Err(new ViewTimeNotApplied(outcome.Reason ?? "unknown"));
    }
}

/// <summary>Request for <see cref="SetTimelineViewTool"/>.</summary>
internal sealed record SetTimelineViewRequest(string? Preset, string? Zoom, string? Start, string? End);

/// <summary>Changes the window the Timeline's axis shows (MCP <c>set_timeline_view</c>).</summary>
internal sealed class SetTimelineViewTool(IViewerTimelineController timeline)
{
    /// <summary>The MCP tool name.</summary>
    public const string Name = "set_timeline_view";

    private readonly IViewerTimelineController _timeline = timeline ?? throw new ArgumentNullException(nameof(timeline));

    /// <summary>Applies the request.</summary>
    public async Task<ToolResult<TimelineStateDto>> InvokeAsync(SetTimelineViewRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var hasWindow = !string.IsNullOrWhiteSpace(request.Start) || !string.IsNullOrWhiteSpace(request.End);
        var given = (string.IsNullOrWhiteSpace(request.Preset) ? 0 : 1) + (string.IsNullOrWhiteSpace(request.Zoom) ? 0 : 1) + (hasWindow ? 1 : 0);
        if (given != 1)
            return ToolResult<TimelineStateDto>.Err(new InvalidArgument("preset", "supply exactly one of preset, zoom, or start and end"));

        TimelineViewChange change;
        if (!string.IsNullOrWhiteSpace(request.Preset))
        {
            EncDotNet.S100.Viewer.ViewModels.TimelinePreset? preset = request.Preset.Trim().ToLowerInvariant() switch
            {
                "now_6h" or "now" => EncDotNet.S100.Viewer.ViewModels.TimelinePreset.NowSixHours,
                "today" => EncDotNet.S100.Viewer.ViewModels.TimelinePreset.Today,
                "next_48h" => EncDotNet.S100.Viewer.ViewModels.TimelinePreset.Next48Hours,
                "this_run" => EncDotNet.S100.Viewer.ViewModels.TimelinePreset.ThisRun,
                "all_loaded" or "all" => EncDotNet.S100.Viewer.ViewModels.TimelinePreset.AllLoaded,
                _ => null,
            };
            if (preset is null)
                return ToolResult<TimelineStateDto>.Err(new InvalidArgument("preset", "expected now_6h, today, next_48h, this_run or all_loaded"));
            change = new TimelineViewChange(preset, null, null);
        }
        else if (!string.IsNullOrWhiteSpace(request.Zoom))
        {
            var zoom = request.Zoom.Trim().ToLowerInvariant() switch { "in" => +1, "out" => -1, _ => 0 };
            if (zoom == 0)
                return ToolResult<TimelineStateDto>.Err(new InvalidArgument("zoom", "expected 'in' or 'out'"));
            change = new TimelineViewChange(null, zoom, null);
        }
        else
        {
            if (!TryParseUtc(request.Start, out var start) || !TryParseUtc(request.End, out var end) || end <= start)
                return ToolResult<TimelineStateDto>.Err(new InvalidArgument("start", "supply ISO-8601 start and end, end after start"));
            change = new TimelineViewChange(null, null, (start, end));
        }

        var outcome = await _timeline.SetViewAsync(change, ct).ConfigureAwait(false);
        return outcome.Applied
            ? ToolResult<TimelineStateDto>.Ok(GetTimelineStateTool.ToDto(outcome.State))
            : ToolResult<TimelineStateDto>.Err(new ViewTimeNotApplied(outcome.Reason ?? "unknown"));
    }

    private static bool TryParseUtc(string? text, out DateTime value) =>
        DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out value);
}
