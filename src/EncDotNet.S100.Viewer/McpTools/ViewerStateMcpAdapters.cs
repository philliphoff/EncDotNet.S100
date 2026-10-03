using System.ComponentModel;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>
/// Wraps the viewer-state tools (<see cref="GetTimelineStateTool"/>,
/// <see cref="SetViewTimeTool"/>, <see cref="SetDatasetStateTool"/>,
/// <see cref="SelectDatasetTool"/>,
/// <see cref="ListNotificationsTool"/>, <see cref="DismissNotificationTool"/>,
/// <see cref="SetTestClockTool"/>) as MCP server tools (#715).
/// </summary>
internal static class ViewerStateMcpAdapters
{
    /// <summary>Creates <c>get_timeline_state</c>.</summary>
    public static McpServerTool Create(GetTimelineStateTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (CancellationToken ct = default) =>
            McpAdapterShared.DispatchAsync(() => inner.InvokeAsync(ct));
        return Tool(del, GetTimelineStateTool.Name,
            "Reads the live viewer's Timeline as the user sees it: mode ('live' while the view time follows now, else "
            + "'pinned'), now, the view time, the loaded range and its coverage windows (gaps lie between them), the "
            + "forecast runs, the readout, offset ('in 11 h 30') and summary text, the status message and its action, and for each time-aware layer the sample it draws (null when "
            + "it has no data near the view time and hides) with its previous and next samples. Use it to explain why a "
            + "layer isn't drawn, or to check Timeline behaviour. Layer times settle after the map's time refresh: call "
            + "await_render_idle after set_view_time before reading them. Read-only; viewer-injected tool.");
    }

    /// <summary>Creates <c>set_view_time</c>.</summary>
    public static McpServerTool Create(SetViewTimeTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("'now' (as the Timeline's Now button: the view then follows now), an ISO-8601 time (UTC assumed when no offset is given), or an offset from the current view time such as '+6h', '-30m', '+1d'.")] string time,
            [Description("'exact' (default) shows exactly that time, so each layer applies its own time limits; 'nearest' moves to the loaded sample nearest it.")] string? snap = null,
            CancellationToken ct = default) =>
            McpAdapterShared.DispatchAsync(() => inner.InvokeAsync(new SetViewTimeRequest(time, snap), ct));
        return Tool(del, SetViewTimeTool.Name,
            "Moves the live viewer's Timeline to a time, as the user does by scrubbing or pressing Now. Unlike "
            + "set_time_step it can show an exact time between samples. Choosing a time leaves Live mode; 'now' enters "
            + "it. The view time is clamped to the loaded range. Returns the Timeline state (see get_timeline_state); "
            + "call await_render_idle before relying on layer times. Fails with view_time_not_applied when no "
            + "time-aware dataset is loaded. 'now' works past every loaded window too: Live follows the clock and "
            + "layers without data hide (get_timeline_state's message says so). Mutating; "
            + "viewer-injected tool.");
    }

    /// <summary>Creates <c>step_time</c>.</summary>
    public static McpServerTool Create(StepTimeTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("'next' or 'previous'.")] string direction,
            [Description("What to step by: 10min, 1h, 6h, 1d, sample (of the step driver layer), boundary (dataset or run start/end), data (next cluster, skipping gaps), or current (the Timeline's chosen step, the default).")] string? unit = null,
            [Description("How many steps, 1–1000 (default 1); stops early at the end of the data.")] int? count = null,
            CancellationToken ct = default) =>
            McpAdapterShared.DispatchAsync(() => inner.InvokeAsync(new StepTimeRequest(direction, unit, count), ct));
        return Tool(del, StepTimeTool.Name,
            "Steps the live viewer's Timeline as its ‹ › buttons and arrow keys do: by a fixed interval (landing on "
            + "whole units), by the samples of the step driver layer, by dataset/run boundaries, or to the next / "
            + "previous cluster of data skipping gaps. Stepping pins the time (leaves Live). Returns the Timeline state; "
            + "view_time_not_applied when nothing is loaded or there is nothing further in that direction. Mutating; "
            + "viewer-injected tool.");
    }

    /// <summary>Creates <c>set_timeline_view</c>.</summary>
    public static McpServerTool Create(SetTimelineViewTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("A window preset: now_6h, today, next_48h, this_run (the run holding the view time), in_view (the data of the layers in the map view) or all_loaded.")] string? preset = null,
            [Description("'in' halves the window around the view time; 'out' doubles it, up to all loaded.")] string? zoom = null,
            [Description("Start of a custom window, ISO-8601 (with end).")] string? start = null,
            [Description("End of a custom window, ISO-8601 (with start).")] string? end = null,
            [Description("True lists only the layers whose footprint intersects the map view (they also set the axis; the rest fold into one row); false lists every layer. Omit to leave it.")] bool? inMapView = null,
            [Description("'lanes' (one lane per layer, grouped by product) or 'strip' (the single strip), as the dock header's Collapse to strip does. Omit to leave it.")] string? layout = null,
            [Description("True also shows what the Library knows but has not loaded (online dashed, on disk outlined) on the lanes; false shows loaded data only. Omit to leave it.")] bool? showOnline = null,
            CancellationToken ct = default) =>
            McpAdapterShared.DispatchAsync(() => inner.InvokeAsync(new SetTimelineViewRequest(preset, zoom, start, end, inMapView, layout, showOnline), ct));
        return Tool(del, SetTimelineViewTool.Name,
            "Changes what the live viewer's Timeline shows, as its preset menu, mouse wheel, drag, In map view checkbox "
            + "and Collapse to strip do: at most one of a preset, a zoom step or a custom start/end, and/or the In map "
            + "view filter, Show online and the lanes/strip layout (applied first, so a preset follows them). The view "
            + "time is not changed. Returns the Timeline state, whose 'window', 'preset', 'gaps', 'inMapView', "
            + "'showOnline', 'layout' and 'lanes' describe it. A lane's 'windows' carry Library item ids for "
            + "library_action (Get = download, Load = load) and describe_library_item. Mutating; viewer-injected tool.");
    }

    /// <summary>Creates <c>set_dataset_state</c>.</summary>
    public static McpServerTool Create(SetDatasetStateTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("Dataset id, as list_datasets reports it.")] string datasetId,
            [Description("True shows the dataset on the map, false hides it; omit to leave it.")] bool? visible = null,
            [Description("Opacity in 0..1; omit to leave it.")] double? opacity = null,
            CancellationToken ct = default) =>
            McpAdapterShared.DispatchAsync(() => inner.InvokeAsync(new SetDatasetStateRequest(datasetId, visible, opacity), ct));
        return Tool(del, SetDatasetStateTool.Name,
            "Shows or hides a loaded dataset and sets its opacity, as the eye icon and opacity control in the Datasets "
            + "list do. Some datasets load hidden (e.g. gridded S-104 water-level surfaces, or duplicate variants in an "
            + "exchange set); use this to switch them on. With neither 'visible' nor 'opacity' it only reports the "
            + "current state. Returns the state before and after. Mutating; viewer-injected tool.");
    }

    /// <summary>Creates <c>select_dataset</c>.</summary>
    public static McpServerTool Create(SelectDatasetTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("Dataset id, as list_datasets / open_dataset report it.")] string datasetId,
            [Description("The inspector tab to show: 'dataset', 'layers' or 'validation'; omit to leave it.")] string? tab = null,
            [Description("How long to wait for a dataset that is still loading to finish validation, in ms (default 10000, 0–120000).")] int? timeoutMs = null,
            CancellationToken ct = default) =>
            McpAdapterShared.DispatchAsync(() => inner.InvokeAsync(new SelectDatasetRequest(datasetId, tab, timeoutMs), ct));
        return Tool(del, SelectDatasetTool.Name,
            "Selects a loaded dataset in the Datasets panel, as the user does by clicking its row, and optionally "
            + "switches the inspector to its Dataset, Layers or Validation tab. The panel switches to its Datasets tab "
            + "when needed. The selection drives the inspector and the map's validation overlay, which draws the "
            + "selected dataset's located findings. It does not open the panel: call set_panel Datasets first to see "
            + "it. Returns the selection and a validation summary (finding counts by severity, or the empty-state "
            + "message). Validation runs when a dataset loads (open_dataset returns after it); for a dataset still "
            + "loading this waits up to timeoutMs, and an exchange-set cell deferred until it is in view reports "
            + "'not_loaded'. Call await_render_idle before capture_app_screenshot so the overlay has painted. "
            + "dataset_not_found for an unknown id. Mutating; viewer-injected tool.");
    }

    /// <summary>Creates <c>list_notifications</c>.</summary>
    public static McpServerTool Create(ListNotificationsTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (CancellationToken ct = default) =>
            McpAdapterShared.DispatchAsync(() => inner.InvokeAsync(ct));
        return Tool(del, ListNotificationsTool.Name,
            "Lists the notifications on screen in the live viewer, oldest first: id, severity, title, message, when "
            + "raised, whether it stays until dismissed, and its action labels. Use it to tell the user about warnings "
            + "or to clear the screen before a screenshot (dismiss_notification). Read-only; viewer-injected tool.");
    }

    /// <summary>Creates <c>dismiss_notification</c>.</summary>
    public static McpServerTool Create(DismissNotificationTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("Notification id from list_notifications, or 'all' (the default) to dismiss every notification.")] string? id = null,
            CancellationToken ct = default) =>
            McpAdapterShared.DispatchAsync(() => inner.InvokeAsync(new DismissNotificationRequest(id), ct));
        return Tool(del, DismissNotificationTool.Name,
            "Dismisses one notification, or all of them, as the user's close button does. Returns the ids dismissed. "
            + "Fails with notification_not_found for an id that is not on screen. Mutating; viewer-injected tool.");
    }

    /// <summary>Creates <c>set_test_clock</c>.</summary>
    public static McpServerTool Create(SetTestClockTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("Set now to this ISO-8601 time (UTC assumed when no offset is given).")] string? now = null,
            [Description("Move now by an offset such as '+1h', '-30m' or '+2d'.")] string? advance = null,
            [Description("True stops the clock (at 'now' when given, else at the current now); false lets it run again.")] bool? freeze = null,
            [Description("True returns to the real clock.")] bool? reset = null,
            CancellationToken ct = default) =>
            McpAdapterShared.DispatchAsync(() => Task.FromResult(inner.Invoke(new SetTestClockRequest(now, advance, freeze, reset))));
        return Tool(del, SetTestClockTool.Name,
            "TEST ONLY (registered with --mcp-test-hooks). Moves, freezes or resets the viewer's notion of now, so "
            + "forecasts age, runs expire and a Live Timeline advances without waiting. Supply at most one of 'now', "
            + "'advance' and 'reset'; 'freeze' alone freezes or releases the clock at the current now. Minute-tick "
            + "consumers (the Timeline, the Library's expiry check) react immediately. Returns the resulting now. "
            + "Mutating; viewer-injected tool.");
    }

    private static McpServerTool Tool(Delegate del, string name, string description) =>
        McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = name,
            Description = description,
            SerializerOptions = McpAdapterShared.Options,
        });
}
