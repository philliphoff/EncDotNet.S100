using System.ComponentModel;
using System.Text.Json;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Viewer.Routing;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.PassageCheck;
using EncDotNet.S100.Viewer.ViewModels;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>What Check passage is showing (#901).</summary>
[Description("Check passage state: whether it's on, whether a result is computing, and what the verdict card and cursor show.")]
internal sealed record CheckStateDto(
    [property: Description("True when Check passage is on.")] bool On,
    [property: Description("Id of the checked (active) route, or null.")] string? RouteId,
    [property: Description("True while a recompute is pending or running.")] bool Computing,
    [property: Description("True while the shown result is out of date (the route draws grey and dashed, the verdict is dimmed).")] bool Stale,
    [property: Description("Verdict code, as get_passage_result: water_level_ends_before_arrival, below_setting, incomplete_data, meets_settings_tight or meets_settings; null without a result.")] string? Verdict,
    [property: Description("The verdict card's title, e.g. \"Below your UKC setting in 1 place\"; null without a result.")] string? VerdictTitle,
    [property: Description("The minimum UKC in metres; null when none could be computed.")] double? MinUkcMeters,
    [property: Description("Zero-based leg of the minimum; null when none.")] int? MinUkcLeg,
    [property: Description("Why the last check failed, or null.")] string? Error,
    [property: Description("The cursor (and ghost vessel) distance from departure in nautical miles; null when hidden.")] double? CursorNm,
    [property: Description("The cursor's latitude; null when hidden.")] double? CursorLatitude,
    [property: Description("The cursor's longitude; null when hidden.")] double? CursorLongitude,
    [property: Description("The cursor's leg; null when hidden.")] int? CursorLeg,
    [property: Description("The ETA at the cursor (UTC); null when hidden or without ETAs.")] DateTimeOffset? CursorEta);

/// <summary>Request for <see cref="SetCheckModeTool"/>.</summary>
[Description("Request for set_check_mode: turn Check passage on or off.")]
internal sealed record SetCheckModeRequest(
    [property: Description("True to turn Check passage on; false to turn it off.")] bool On,
    [property: Description("Route to check, made active first; omit for the active route.")] string? RouteId = null,
    [property: Description("When turning on, wait for the result before returning (default true).")] bool Wait = true);

/// <summary>Request for <see cref="SetPassageCursorTool"/>.</summary>
[Description("Request for set_passage_cursor: move the profile cursor and ghost vessel along the checked route.")]
internal sealed record SetPassageCursorRequest(
    [property: Description("Distance from departure in nautical miles (clamped to the route); null hides the cursor.")] double? DistanceNm);

/// <summary>Shared plumbing for the Check passage tools.</summary>
internal abstract class PassageCheckToolBase : RouteToolBase
{
    /// <summary>How long a waiting tool waits for a result.</summary>
    internal static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(60);

    protected PassageCheckToolBase(RoutesService routes, IRouteEditInvoker invoker, PassageCheckService check)
        : base(routes, invoker)
    {
        ArgumentNullException.ThrowIfNull(check);
        Check = check;
    }

    protected PassageCheckService Check { get; }

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
                return;
        }
    }

    protected Task<ToolResult<CheckStateDto>> StateAsync() =>
        Invoker.InvokeAsync(() => ToolResult<CheckStateDto>.Ok(State(Routes, Check)));

    internal static CheckStateDto State(RoutesService routes, PassageCheckService check)
    {
        var current = check.IsActive ? check.Current : null;
        var result = current?.Result;
        var cursor = check.Cursor;
        return new CheckStateDto(
            check.IsActive,
            check.IsActive ? routes.Routes.ActiveRoute?.Id : null,
            check.IsComputing,
            check.IsStale,
            result is null ? null : Verdict(result.Verdict),
            result is null ? null : PassageCheckViewModel.VerdictTitle(result),
            result?.Minimum is { } min ? Math.Round(min.ClearanceMeters, 2) : null,
            result?.Minimum?.LegIndex,
            check.Error,
            cursor is { } c ? Math.Round(c.DistanceMeters / PassageGeodesy.MetersPerNauticalMile, 3) : null,
            cursor?.Position.Latitude,
            cursor?.Position.Longitude,
            cursor?.LegIndex,
            cursor?.Eta);
    }

    private static string Verdict(PassageVerdict verdict) => verdict switch
    {
        PassageVerdict.MeetsSettings => "meets_settings",
        PassageVerdict.MeetsSettingsTight => "meets_settings_tight",
        PassageVerdict.BelowSetting => "below_setting",
        PassageVerdict.IncompleteData => "incomplete_data",
        _ => "water_level_ends_before_arrival",
    };
}

/// <summary>
/// Turns Check passage on or off for the active (or a named) route, as the
/// Routes panel's Check passage toggle does, and returns what it shows.
/// </summary>
internal sealed class SetCheckModeTool : PassageCheckToolBase
{
    public const string Name = "set_check_mode";

    public SetCheckModeTool(RoutesService routes, IRouteEditInvoker invoker, PassageCheckService check)
        : base(routes, invoker, check) { }

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
            for (var i = 0; i < route.Legs.Count; i++)
            {
                if (route.GetEffectiveLegValue(i, RoutePassageField.Draft) is null)
                    return new InvalidArgument("routeId", "the route has no draft; set one with set_passage");
            }

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

/// <summary>Returns what Check passage is showing.</summary>
internal sealed class GetCheckStateTool : PassageCheckToolBase
{
    public const string Name = "get_check_state";

    public GetCheckStateTool(RoutesService routes, IRouteEditInvoker invoker, PassageCheckService check)
        : base(routes, invoker, check) { }

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

    public SetPassageCursorTool(RoutesService routes, IRouteEditInvoker invoker, PassageCheckService check)
        : base(routes, invoker, check) { }

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
                return new InvalidArgument("distanceNm", "Check passage is off or has no result; turn it on with set_check_mode");
            Check.SetCursor(request.DistanceNm * PassageGeodesy.MetersPerNauticalMile);
            return null;
        }).ConfigureAwait(false);
        return error is not null ? ToolResult<CheckStateDto>.Err(error) : await StateAsync().ConfigureAwait(false);
    }
}

/// <summary>Wraps the Check passage tools as <see cref="McpServerTool"/>s.</summary>
internal static class PassageCheckMcpAdapters
{
    private static readonly JsonSerializerOptions JsonOptions = McpAdapterShared.Options;

    public static McpServerTool Create(SetCheckModeTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("True to turn Check passage on; false to turn it off.")] bool on,
            [Description("Route to check, made active first; omit for the active route.")] string? routeId = null,
            [Description("When turning on, wait for the result before returning (default true).")] bool wait = true,
            CancellationToken ct = default) =>
            McpAdapterShared.DispatchAsync(() => inner.InvokeAsync(new SetCheckModeRequest(on, routeId, wait), ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = SetCheckModeTool.Name,
            Description =
                "Turns Check passage on or off, as the Routes panel's Check passage toggle does. While it's on, the " +
                "map colours the active route by UKC state at ETA, folds Object Information to its edge tab and shows " +
                "the verdict card. The route needs a draft (set_passage). Returns the state get_check_state returns; " +
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
                "Returns what Check passage is showing: on or off, whether a result is computing or stale, the verdict " +
                "card's verdict and title, the minimum UKC, and the cursor (ghost vessel) position and ETA.",
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
                "Moves the Check passage cursor and the ghost vessel to a distance along the checked route, as hovering " +
                "a leg row or the clearance profile does. Returns the state, including the cursor's position and ETA. " +
                "Viewer-injected tool — mutates live state.",
            SerializerOptions = JsonOptions,
        });
    }
}
