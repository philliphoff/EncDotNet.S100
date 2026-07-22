using System.ComponentModel;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>
/// Request payload for <see cref="SetOwnShipTool"/>.
/// </summary>
/// <remarks>
/// Every field is optional; the tool applies whichever components are
/// supplied. A position change requires <see cref="Latitude"/> and
/// <see cref="Longitude"/> together. <see cref="HeadingDeg"/> (a gyro
/// heading distinct from course) is only honoured alongside a position
/// because the helm sets heading as part of an absolute state
/// correction. At least one actionable field must be supplied.
/// </remarks>
[Description("Request for set_own_ship: drive the simulated own-ship helm. All fields optional. Provide lat AND lon together to reposition; cog/sog adjust course/speed; heading (gyro heading) is applied only with a position; hold=true stops the vessel, hold=false resumes its previous speed. Angles are degrees true; speed is metres per second.")]
internal sealed record SetOwnShipRequest(
    [property: Description("WGS-84 latitude in decimal degrees [-90, 90]. Must be paired with lon.")] double? Lat = null,
    [property: Description("WGS-84 longitude in decimal degrees [-180, 180]. Must be paired with lat.")] double? Lon = null,
    [property: Description("Course over ground in degrees true; normalised to [0, 360).")] double? Cog = null,
    [property: Description("Speed over ground in metres per second; clamped to be non-negative.")] double? Sog = null,
    [property: Description("Gyro heading in degrees true; normalised to [0, 360). Only applied together with lat/lon.")] double? Heading = null,
    [property: Description("true stops the vessel (remembering speed); false resumes the remembered speed.")] bool? Hold = null);

/// <summary>Result of <see cref="SetOwnShipTool"/>: an echo of the
/// components that were applied.</summary>
[Description("Result of set_own_ship: echoes the components that were applied to the helm.")]
internal sealed record SetOwnShipResult(
    [property: Description("Applied latitude in decimal degrees, or null when position was not changed.")] double? Lat,
    [property: Description("Applied longitude in decimal degrees, or null when position was not changed.")] double? Lon,
    [property: Description("Applied course over ground in degrees true, or null when unchanged.")] double? Cog,
    [property: Description("Applied speed over ground in metres per second, or null when unchanged.")] double? Sog,
    [property: Description("Applied gyro heading in degrees true, or null when unchanged.")] double? Heading,
    [property: Description("\"hold\" when the vessel was stopped, \"resume\" when restarted, or null when neither was requested.")] string? HoldAction);

/// <summary>
/// Mutates the live viewer's simulated own-ship position by driving the
/// <see cref="IOwnShipHelm"/>. Lets scripted / agent runs put own-ship
/// in a known kinematic state — pre-position it for a screenshot, steer
/// it onto a course, or stop it — without touching the GUI. Distinguished
/// from the read-only capture tools: this intentionally mutates live
/// state.
/// </summary>
/// <remarks>
/// <para>
/// The helm is a DI singleton that exists whether or not the own-ship
/// overlay is currently shown, so this tool works even when the overlay
/// is hidden: the corrected state is cached and surfaces as soon as the
/// overlay is enabled. The tool therefore never reports a "not ready"
/// condition based on overlay visibility.
/// </para>
/// <para>
/// Validation:
/// <list type="bullet">
/// <item><description>Latitude in [-90, 90]; longitude in [-180, 180]; both required together for a reposition.</description></item>
/// <item><description>Course / heading must be finite; speed must be finite and non-negative.</description></item>
/// <item><description>Heading without a position is rejected (the helm sets heading only as part of an absolute correction).</description></item>
/// <item><description>At least one of {lat+lon, cog, sog, heading, hold} must be supplied.</description></item>
/// </list>
/// </para>
/// </remarks>
internal sealed class SetOwnShipTool
{
    /// <summary>Public tool name as exposed over MCP.</summary>
    public const string Name = "set_own_ship";

    internal const double MinLat = -90.0;
    internal const double MaxLat = 90.0;
    internal const double MinLon = -180.0;
    internal const double MaxLon = 180.0;

    private readonly IOwnShipHelm _helm;

    /// <summary>Creates a new <see cref="SetOwnShipTool"/>.</summary>
    public SetOwnShipTool(IOwnShipHelm helm)
    {
        ArgumentNullException.ThrowIfNull(helm);
        _helm = helm;
    }

    /// <summary>Executes the tool.</summary>
    public Task<ToolResult<SetOwnShipResult>> InvokeAsync(
        SetOwnShipRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var hasLat = request.Lat.HasValue;
        var hasLon = request.Lon.HasValue;
        if (hasLat ^ hasLon)
        {
            return Err(new InvalidArgument(
                hasLat ? "lon" : "lat",
                "lat and lon must be supplied together to reposition own-ship"));
        }

        var hasPosition = hasLat && hasLon;
        var anyAction = hasPosition
            || request.Cog.HasValue || request.Sog.HasValue
            || request.Heading.HasValue || request.Hold.HasValue;
        if (!anyAction)
        {
            return Err(new InvalidArgument(
                "request",
                "supply at least one of lat+lon, cog, sog, heading, or hold"));
        }

        if (request.Heading.HasValue && !hasPosition)
        {
            return Err(new InvalidArgument(
                "heading",
                "heading can only be set together with a lat/lon position"));
        }

        if (hasPosition)
        {
            if (Validate(request.Lat!.Value, "lat", MinLat, MaxLat) is { } e1) return Err(e1);
            if (Validate(request.Lon!.Value, "lon", MinLon, MaxLon) is { } e2) return Err(e2);
        }
        if (request.Cog is { } cog && NotFinite(cog))
            return Err(new InvalidArgument("cog", $"value {cog} is not a finite number"));
        if (request.Heading is { } hdg && NotFinite(hdg))
            return Err(new InvalidArgument("heading", $"value {hdg} is not a finite number"));
        if (request.Sog is { } sog)
        {
            if (NotFinite(sog))
                return Err(new InvalidArgument("sog", $"value {sog} is not a finite number"));
            if (sog < 0.0)
                return Err(new InvalidArgument("sog", $"value {sog} must be non-negative"));
        }

        // Apply hold/resume first so a combined call (e.g. resume + new
        // course) leaves the requested course/speed authoritative.
        string? holdAction = null;
        if (request.Hold is { } hold)
        {
            if (hold) { _helm.Hold(); holdAction = "hold"; }
            else { _helm.Resume(); holdAction = "resume"; }
        }

        if (hasPosition)
        {
            _helm.SetState(
                request.Lat!.Value, request.Lon!.Value,
                request.Cog, request.Sog, request.Heading);
        }
        else
        {
            if (request.Cog is { } c) _helm.SetCourse(c);
            if (request.Sog is { } s) _helm.SetSpeed(s);
        }

        return Ok(new SetOwnShipResult(
            request.Lat, request.Lon, request.Cog, request.Sog,
            hasPosition ? request.Heading : null,
            holdAction));
    }

    private static bool NotFinite(double v) => double.IsNaN(v) || double.IsInfinity(v);

    private static ToolError? Validate(double value, string name, double min, double max)
    {
        if (NotFinite(value))
            return new InvalidArgument(name, $"value {value} is not a finite number");
        if (value < min || value > max)
            return new InvalidArgument(name, $"value {value} is outside the WGS-84 range [{min}, {max}]");
        return null;
    }

    private static Task<ToolResult<SetOwnShipResult>> Ok(SetOwnShipResult value)
        => Task.FromResult(ToolResult<SetOwnShipResult>.Ok(value));

    private static Task<ToolResult<SetOwnShipResult>> Err(ToolError error)
        => Task.FromResult(ToolResult<SetOwnShipResult>.Err(error));
}
