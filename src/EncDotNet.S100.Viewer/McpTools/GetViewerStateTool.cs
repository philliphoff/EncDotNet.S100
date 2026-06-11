using System;
using System.ComponentModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using EncDotNet.S100.Mcp.Tools;
using EncDotNet.S100.Mcp.Tools.Catalog;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>The live viewport as a WGS-84 frame plus web-mercator zoom.</summary>
[Description("The live viewport as a WGS-84 bounding box plus centre and zoom; null when the map is not yet laid out.")]
internal sealed record ViewportState(
    [property: Description("South edge in decimal degrees, WGS-84.")] double South,
    [property: Description("West edge in decimal degrees, WGS-84.")] double West,
    [property: Description("North edge in decimal degrees, WGS-84.")] double North,
    [property: Description("East edge in decimal degrees, WGS-84.")] double East,
    [property: Description("Viewport-centre latitude in decimal degrees, WGS-84.")] double CenterLat,
    [property: Description("Viewport-centre longitude in decimal degrees, WGS-84.")] double CenterLon,
    [property: Description("Equivalent standard web-mercator zoom level.")] double Zoom);

/// <summary>The aggregate global-time clock state.</summary>
[Description("The aggregate global-time clock state across loaded time-aware datasets; null when no time-aware dataset is loaded.")]
internal sealed record TimeState(
    [property: Description("True when at least one loaded dataset contributes time samples.")] bool Active,
    [property: Description("Current global clock value (ISO-8601 UTC), or null when no clock has been established.")] string? CurrentTime,
    [property: Description("Zero-based index of the current time within the aggregated samples, or -1 when not aligned to a sample.")] int CurrentIndex,
    [property: Description("Total number of distinct aggregated time samples.")] int SampleCount,
    [property: Description("Earliest aggregated sample (ISO-8601 UTC), or null when none.")] string? MinTime,
    [property: Description("Latest aggregated sample (ISO-8601 UTC), or null when none.")] string? MaxTime);

/// <summary>The simulated own-ship kinematic state.</summary>
[Description("The simulated own-ship kinematic state; null when no own-ship fix has been produced.")]
internal sealed record OwnShipState(
    [property: Description("Latitude in decimal degrees, WGS-84.")] double Lat,
    [property: Description("Longitude in decimal degrees, WGS-84.")] double Lon,
    [property: Description("Course over ground in degrees true, or null when unknown.")] double? Cog,
    [property: Description("Speed over ground in metres per second, or null when unknown.")] double? Sog,
    [property: Description("Gyro heading in degrees true, or null when mirroring course.")] double? Heading,
    [property: Description("True when the vessel is stopped via a hold with a remembered speed.")] bool IsHeld,
    [property: Description("Commanded (ordered) speed over ground in metres per second — the speed a resume would restore while held.")] double CommandedSpeedMs);

/// <summary>Result of <see cref="GetViewerStateTool"/>.</summary>
[Description("A read-only snapshot of the live viewer's render and navigation state. Each section is null when its subsystem is unavailable, so the tool degrades gracefully before the map finishes initialising.")]
internal sealed record GetViewerStateResult(
    [property: Description("Live viewport (WGS-84 bbox, centre, zoom), or null when the map is not yet laid out.")] ViewportState? Viewport,
    [property: Description("Active map palette (\"Day\", \"Dusk\", \"Night\"), or null when the render-state controller is unavailable.")] string? Palette,
    [property: Description("Active ECDIS display category (\"DisplayBase\", \"Standard\", \"OtherInformation\", \"All\"), or null when unavailable.")] string? DisplayCategory,
    [property: Description("Global-time clock state, or null when no time-aware dataset is loaded.")] TimeState? Time,
    [property: Description("Number of datasets currently loaded in the viewer.")] int DatasetCount,
    [property: Description("Simulated own-ship state, or null when no own-ship fix has been produced.")] OwnShipState? OwnShip);

/// <summary>
/// Reports a read-only snapshot of the live viewer's render and
/// navigation state: viewport, palette, ECDIS display category, global
/// time clock, loaded-dataset count, and simulated own-ship. The
/// read-side counterpart to the mutating <c>set_viewport</c> /
/// <c>set_palette</c> / <c>set_display_category</c> / <c>set_time_step</c>
/// / <c>set_own_ship</c> tools, so scripted runs can assert
/// preconditions and verify postconditions without issuing a
/// side-effecting write.
/// </summary>
/// <remarks>
/// Every dependency is optional; a section of the result is
/// <see langword="null"/> when its subsystem has not been wired or is
/// not yet ready, so the tool never fails just because (say) the map
/// control has not finished initialising.
/// </remarks>
internal sealed class GetViewerStateTool
{
    /// <summary>Public tool name as exposed over MCP.</summary>
    public const string Name = "get_viewer_state";

    private readonly IMapHostAccessor? _mapHostAccessor;
    private readonly IRenderStateControllerAccessor? _renderStateAccessor;
    private readonly GlobalTimeService? _globalTime;
    private readonly IDatasetCatalog? _catalog;
    private readonly IOwnShipPositionProvider? _ownShipPosition;
    private readonly IOwnShipHelmState? _ownShipHelmState;

    /// <summary>Creates a new <see cref="GetViewerStateTool"/>.</summary>
    public GetViewerStateTool(
        IMapHostAccessor? mapHostAccessor = null,
        IRenderStateControllerAccessor? renderStateAccessor = null,
        GlobalTimeService? globalTime = null,
        IDatasetCatalog? catalog = null,
        IOwnShipPositionProvider? ownShipPosition = null,
        IOwnShipHelmState? ownShipHelmState = null)
    {
        _mapHostAccessor = mapHostAccessor;
        _renderStateAccessor = renderStateAccessor;
        _globalTime = globalTime;
        _catalog = catalog;
        _ownShipPosition = ownShipPosition;
        _ownShipHelmState = ownShipHelmState;
    }

    /// <summary>Executes the tool.</summary>
    public Task<ToolResult<GetViewerStateResult>> InvokeAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var viewport = ReadViewport();
        var (palette, displayCategory) = ReadRenderState();
        var time = ReadTime();
        var datasetCount = _catalog?.Datasets.Length ?? 0;
        var ownShip = ReadOwnShip();

        return Task.FromResult(ToolResult<GetViewerStateResult>.Ok(new GetViewerStateResult(
            viewport,
            palette,
            displayCategory,
            time,
            datasetCount,
            ownShip)));
    }

    private ViewportState? ReadViewport()
    {
        if (_mapHostAccessor?.Current is not { } host)
            return null;

        if (host.TryGetViewportWgs84() is not { } v)
            return null;

        return new ViewportState(
            v.South, v.West, v.North, v.East,
            v.CenterLatitude, v.CenterLongitude, v.Zoom);
    }

    private (string? Palette, string? DisplayCategory) ReadRenderState()
    {
        if (_renderStateAccessor?.Current is not { } controller)
            return (null, null);

        return (controller.CurrentPalette.ToString(), controller.CurrentDisplayCategory.ToString());
    }

    private TimeState? ReadTime()
    {
        if (_globalTime is not { } gt)
            return null;

        var samples = gt.AllSamples;
        if (samples.Count == 0 && !gt.IsActive && gt.CurrentTime is null)
            return null;

        var currentIndex = -1;
        if (gt.CurrentTime is { } current)
        {
            for (var i = 0; i < samples.Count; i++)
            {
                if (samples[i] == current) { currentIndex = i; break; }
            }
        }

        return new TimeState(
            gt.IsActive,
            Iso(gt.CurrentTime),
            currentIndex,
            samples.Count,
            Iso(gt.MinTime),
            Iso(gt.MaxTime));
    }

    private OwnShipState? ReadOwnShip()
    {
        if (_ownShipPosition?.Current is not { } fix)
            return null;

        var isHeld = _ownShipHelmState?.IsHeld ?? false;
        var commanded = _ownShipHelmState?.CommandedSpeedMs ?? (fix.SpeedOverGroundMs ?? 0.0);

        return new OwnShipState(
            fix.Latitude,
            fix.Longitude,
            fix.CourseOverGroundDeg,
            fix.SpeedOverGroundMs,
            fix.HeadingDeg,
            isHeld,
            commanded);
    }

    private static string? Iso(DateTime? value)
        => value?.ToString("o", CultureInfo.InvariantCulture);
}
