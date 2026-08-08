using System.ComponentModel;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.ExchangeSets;

namespace EncDotNet.S100.Mcp.Tools.Mutable;

/// <summary>
/// Request payload for <see cref="SetViewportTool"/>. Two mutually-exclusive
/// forms are accepted:
/// <list type="bullet">
/// <item><description>
/// <b>centre + scale</b> — <see cref="CenterLongitude"/>,
/// <see cref="CenterLatitude"/>, and <see cref="ScaleDenominator"/> (with an
/// optional <see cref="RotationDegrees"/>), which pins an explicit geographic
/// viewport via <see cref="IViewportController.Set"/>.
/// </description></item>
/// <item><description>
/// <b>bounding box</b> — <see cref="MinLongitude"/>, <see cref="MinLatitude"/>,
/// <see cref="MaxLongitude"/>, <see cref="MaxLatitude"/>, which frames a WGS-84
/// box via <see cref="IViewportController.SetToBounds"/> (the host resolves the
/// scale for its own render surface).
/// </description></item>
/// </list>
/// Supplying values from both forms, or an incomplete form, is rejected.
/// </summary>
public sealed record SetViewportRequest(
    [property: Description("Centre longitude in decimal degrees, WGS-84. Pair with centerLatitude and scaleDenominator; mutually exclusive with the bounding-box form.")] double? CenterLongitude = null,
    [property: Description("Centre latitude in decimal degrees, WGS-84. Pair with centerLongitude and scaleDenominator; mutually exclusive with the bounding-box form.")] double? CenterLatitude = null,
    [property: Description("Map scale denominator (e.g. 50000 for 1:50000); must be positive. Pair with centerLongitude/centerLatitude; mutually exclusive with the bounding-box form.")] double? ScaleDenominator = null,
    [property: Description("Optional clockwise rotation in degrees; only 0 (north-up) is supported by the composite renderer, so any non-zero value is rejected. Applies to the centre+scale form.")] double? RotationDegrees = null,
    [property: Description("Bounding-box west edge (min longitude) in decimal degrees, WGS-84. Pair with the other three edges; mutually exclusive with the centre+scale form.")] double? MinLongitude = null,
    [property: Description("Bounding-box south edge (min latitude) in decimal degrees, WGS-84. Pair with the other three edges; mutually exclusive with the centre+scale form.")] double? MinLatitude = null,
    [property: Description("Bounding-box east edge (max longitude) in decimal degrees, WGS-84. Pair with the other three edges; mutually exclusive with the centre+scale form.")] double? MaxLongitude = null,
    [property: Description("Bounding-box north edge (max latitude) in decimal degrees, WGS-84. Pair with the other three edges; mutually exclusive with the centre+scale form.")] double? MaxLatitude = null);

/// <summary>Result payload for <see cref="SetViewportTool"/>.</summary>
public sealed record SetViewportResult(
    [property: Description("How the viewport was set: 'center' (centre+scale) or 'bounds' (bounding box).")] string Mode,
    [property: Description("Centre longitude of the applied viewport, decimal degrees WGS-84.")] double CenterLongitude,
    [property: Description("Centre latitude of the applied viewport, decimal degrees WGS-84.")] double CenterLatitude,
    [property: Description("Scale denominator of the applied viewport. For the bounds form this is resolved against a reference render surface and is re-fit to the actual size on each render.")] double ScaleDenominator,
    [property: Description("Clockwise rotation in degrees of the applied viewport; always 0 today.")] double RotationDegrees,
    [property: Description("The viewport applied before this call as 'lon,lat,scale,rotation', or null when the host was auto-fitting the loaded datasets.")] string? Previous);

/// <summary>
/// Mutating tool that pins the session's geographic viewport — either an explicit
/// centre + scale (<see cref="IViewportController.Set"/>) or a framed WGS-84
/// bounding box (<see cref="IViewportController.SetToBounds"/>). Renderer-neutral:
/// it drives the shared <see cref="IViewportController"/>, which stores the
/// viewport geographically and resolves it to a pixel viewport per render, so a
/// <see langword="null"/> viewport keeps the host's auto-fit behaviour.
/// </summary>
/// <remarks>
/// <para>
/// The composite renderer has no rotation analog (the shared
/// <c>Viewport</c> carries no rotation), so a non-zero
/// <see cref="SetViewportRequest.RotationDegrees"/> is rejected rather than
/// silently dropped. North-up (0) is the only supported value.
/// </para>
/// </remarks>
public sealed class SetViewportTool
{
    /// <summary>The MCP tool name as exposed to clients.</summary>
    public const string Name = "set_viewport";

    internal const double MinLat = -90.0;
    internal const double MaxLat = 90.0;
    internal const double MinLon = -180.0;
    internal const double MaxLon = 180.0;

    private readonly ICapabilityAccessor<IViewportController> _viewport;

    /// <summary>Creates the tool bound to a viewport-controller accessor.</summary>
    public SetViewportTool(ICapabilityAccessor<IViewportController> viewport)
    {
        ArgumentNullException.ThrowIfNull(viewport);
        _viewport = viewport;
    }

    /// <summary>
    /// Pins the viewport. Returns the applied centre/scale/rotation plus the
    /// previous viewport so callers can stitch repeated runs.
    /// </summary>
    public Task<ToolResult<SetViewportResult>> InvokeAsync(
        SetViewportRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var hasCenterAny = request.CenterLongitude.HasValue || request.CenterLatitude.HasValue
            || request.ScaleDenominator.HasValue || request.RotationDegrees.HasValue;
        var hasBoundsAny = request.MinLongitude.HasValue || request.MinLatitude.HasValue
            || request.MaxLongitude.HasValue || request.MaxLatitude.HasValue;

        // Cross-field constraints are attributed to a single real request
        // property ("centerLongitude") per InvalidArgument's contract, with the
        // relationship spelled out in the message.
        if (hasCenterAny && hasBoundsAny)
        {
            return Err(new InvalidArgument(
                "centerLongitude",
                "supply EITHER centre+scale (centerLongitude/centerLatitude/scaleDenominator) OR a bounding box (minLongitude/minLatitude/maxLongitude/maxLatitude), not both"));
        }
        if (!hasCenterAny && !hasBoundsAny)
        {
            return Err(new InvalidArgument(
                "centerLongitude",
                "must supply either centre+scale (centerLongitude/centerLatitude/scaleDenominator) or a bounding box (minLongitude/minLatitude/maxLongitude/maxLatitude)"));
        }

        var controller = _viewport.Current;
        if (controller is null)
        {
            return Err(new HostNotReady("the viewport controller is not attached yet"));
        }

        return hasBoundsAny
            ? ApplyBounds(request, controller)
            : ApplyCenterScale(request, controller);
    }

    private static Task<ToolResult<SetViewportResult>> ApplyCenterScale(
        SetViewportRequest request, IViewportController controller)
    {
        if (request.CenterLongitude is not { } lon
            || request.CenterLatitude is not { } lat
            || request.ScaleDenominator is not { } scale)
        {
            return Err(new InvalidArgument(
                "centerLongitude",
                "centre+scale form requires all three of centerLongitude, centerLatitude, scaleDenominator"));
        }

        if (Validate(lon, "centerLongitude", MinLon, MaxLon) is { } e1) return Err(e1);
        if (Validate(lat, "centerLatitude", MinLat, MaxLat) is { } e2) return Err(e2);
        if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0)
        {
            return Err(new InvalidArgument(
                "scaleDenominator", $"value {scale} must be a positive, finite number"));
        }

        // The composite Viewport has no rotation; reject a non-zero request
        // rather than silently dropping it. Only north-up is supported.
        var rotation = request.RotationDegrees ?? 0.0;
        if (double.IsNaN(rotation) || double.IsInfinity(rotation))
        {
            return Err(new InvalidArgument("rotationDegrees", $"value {rotation} is not a finite number"));
        }
        if (rotation != 0.0)
        {
            return Err(new InvalidArgument(
                "rotationDegrees",
                $"value {rotation} is not supported; the composite renderer is north-up only, so rotationDegrees must be 0"));
        }

        var previous = controller.Current;
        controller.Set(new MapViewport(lon, lat, scale, rotation));

        return Ok(new SetViewportResult(
            Mode: "center",
            CenterLongitude: lon,
            CenterLatitude: lat,
            ScaleDenominator: scale,
            RotationDegrees: rotation,
            Previous: Format(previous)));
    }

    private static Task<ToolResult<SetViewportResult>> ApplyBounds(
        SetViewportRequest request, IViewportController controller)
    {
        if (request.MinLongitude is not { } minLon
            || request.MinLatitude is not { } minLat
            || request.MaxLongitude is not { } maxLon
            || request.MaxLatitude is not { } maxLat)
        {
            return Err(new InvalidArgument(
                "minLongitude",
                "bounding-box form requires all four of minLongitude, minLatitude, maxLongitude, maxLatitude"));
        }

        if (Validate(minLon, "minLongitude", MinLon, MaxLon) is { } e1) return Err(e1);
        if (Validate(minLat, "minLatitude", MinLat, MaxLat) is { } e2) return Err(e2);
        if (Validate(maxLon, "maxLongitude", MinLon, MaxLon) is { } e3) return Err(e3);
        if (Validate(maxLat, "maxLatitude", MinLat, MaxLat) is { } e4) return Err(e4);

        if (minLat >= maxLat)
        {
            return Err(new GeometryInvalid(
                "minLatitude", $"minLatitude ({minLat}) must be less than maxLatitude ({maxLat})"));
        }
        if (minLon >= maxLon)
        {
            return Err(new GeometryInvalid(
                "minLongitude",
                $"minLongitude ({minLon}) must be less than maxLongitude ({maxLon}); antimeridian crossing is not supported"));
        }

        var previous = controller.Current;
        controller.SetToBounds(new BoundingBox
        {
            WestBoundLongitude = minLon,
            EastBoundLongitude = maxLon,
            SouthBoundLatitude = minLat,
            NorthBoundLatitude = maxLat,
        });

        // Echo the resolved viewport the controller settled on (its own
        // reference-surface framing of the box) so callers can verify.
        var applied = controller.Current;
        return Ok(new SetViewportResult(
            Mode: "bounds",
            CenterLongitude: applied?.CenterLongitude ?? (minLon + maxLon) / 2.0,
            CenterLatitude: applied?.CenterLatitude ?? (minLat + maxLat) / 2.0,
            ScaleDenominator: applied?.ScaleDenominator ?? 0.0,
            RotationDegrees: applied?.RotationDegrees ?? 0.0,
            Previous: Format(previous)));
    }

    private static InvalidArgument? Validate(double value, string name, double min, double max)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return new InvalidArgument(name, $"value {value} is not a finite number");
        if (value < min || value > max)
            return new InvalidArgument(name, $"value {value} is outside the WGS-84 range [{min}, {max}]");
        return null;
    }

    private static string? Format(MapViewport? viewport) =>
        viewport is null
            ? null
            : string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{viewport.CenterLongitude},{viewport.CenterLatitude},{viewport.ScaleDenominator},{viewport.RotationDegrees}");

    private static Task<ToolResult<SetViewportResult>> Ok(SetViewportResult value)
        => Task.FromResult(ToolResult<SetViewportResult>.Ok(value));

    private static Task<ToolResult<SetViewportResult>> Err(ToolError error)
        => Task.FromResult(ToolResult<SetViewportResult>.Err(error));
}
