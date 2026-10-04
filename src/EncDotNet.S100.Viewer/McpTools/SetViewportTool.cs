using System.ComponentModel;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Viewer.Services;
using Mapsui;
using Mapsui.Projections;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>
/// Request payload for <see cref="SetViewportTool"/>.
/// </summary>
/// <remarks>
/// Three mutually-exclusive forms are accepted: a WGS-84 bounding box
/// (<paramref name="South"/>/<paramref name="West"/>/<paramref name="North"/>/<paramref name="East"/>),
/// a centre plus web-mercator zoom
/// (<paramref name="CenterLat"/>/<paramref name="CenterLon"/>/<paramref name="Zoom"/>),
/// or a centre plus map scale
/// (<paramref name="CenterLat"/>/<paramref name="CenterLon"/>/<paramref name="ScaleDenominator"/>).
/// Every value of the chosen form must be supplied; mixing forms (including
/// <paramref name="Zoom"/> with <paramref name="ScaleDenominator"/>) is
/// rejected with <see cref="InvalidArgument"/>. Antimeridian-crossing bboxes are not
/// supported in v1 (would need <c>west &gt; east</c>).
/// </remarks>
[Description("Request for set_viewport: supply EXACTLY ONE of a WGS-84 bbox (south/west/north/east), centre+zoom (centerLat/centerLon/zoom), or centre+scale (centerLat/centerLon/scaleDenominator). Coordinates are decimal degrees. Zoom is the standard web-mercator level (0–24); scaleDenominator is the 1:N map scale the status bar shows.")]
internal sealed record SetViewportRequest(
    [property: Description("Bounding-box south edge in decimal degrees (WGS-84). Must be paired with west/north/east; mutually exclusive with centre+zoom.")] double? South = null,
    [property: Description("Bounding-box west edge in decimal degrees (WGS-84). Must be paired with south/north/east; mutually exclusive with centre+zoom.")] double? West = null,
    [property: Description("Bounding-box north edge in decimal degrees (WGS-84). Must be paired with south/west/east; mutually exclusive with centre+zoom.")] double? North = null,
    [property: Description("Bounding-box east edge in decimal degrees (WGS-84). Must be paired with south/west/north; mutually exclusive with centre+zoom.")] double? East = null,
    [property: Description("Centre latitude in decimal degrees (WGS-84). Must be paired with centerLon and one of zoom or scaleDenominator; mutually exclusive with the bbox form.")] double? CenterLat = null,
    [property: Description("Centre longitude in decimal degrees (WGS-84). Must be paired with centerLat and one of zoom or scaleDenominator; mutually exclusive with the bbox form.")] double? CenterLon = null,
    [property: Description("Web-mercator zoom level in [0, 24]. Must be paired with centerLat/centerLon; mutually exclusive with scaleDenominator and the bbox form.")] double? Zoom = null,
    [property: Description("Optional clockwise viewport rotation in degrees (0 = north-up). Applied on top of the bbox or centre+zoom frame, so it exercises the rotated-viewport render path (e.g. upright-label verification). Any finite value is accepted and normalised to [0, 360); must accompany a frame form.")] double? Rotation = null,
    [property: Description("Map scale denominator (e.g. 50000 for 1:50 000); positive and finite. Must be paired with centerLat/centerLon; mutually exclusive with zoom and the bbox form. Converted at the centre latitude with the 0.28 mm pixel the status bar uses, so the status bar reads back this scale.")] double? ScaleDenominator = null);

/// <summary>Result of <see cref="SetViewportTool"/>.</summary>
[Description("Result of set_viewport: the request mode that was applied (bbox or center) plus an echo of the resolved WGS-84 viewport and its map scale. The echo is the precise frame the navigator was set to and is suitable for verification in scripted runs.")]
internal sealed record SetViewportResult(
    [property: Description("\"bbox\" when the call resolved through the south/west/north/east form; \"center\" when it resolved through a centre form (centerLat/centerLon with zoom or scaleDenominator).")] string Mode,
    [property: Description("Echoed south edge of the resolved viewport in decimal degrees, WGS-84.")] double South,
    [property: Description("Echoed west edge of the resolved viewport in decimal degrees, WGS-84.")] double West,
    [property: Description("Echoed north edge of the resolved viewport in decimal degrees, WGS-84.")] double North,
    [property: Description("Echoed east edge of the resolved viewport in decimal degrees, WGS-84.")] double East,
    [property: Description("Clockwise viewport rotation in degrees that was applied (0 = north-up), normalised to [0, 360).")] double Rotation,
    [property: Description("Unrounded 1:N scale denominator of the applied viewport, as the status bar computes it. Read from the live map after the change (so it reflects zoom limits and, for a bbox, the fit to the control); for the centre forms it falls back to the requested scale when the map is not laid out yet. Null for a bbox when the map is not laid out.")] double? ScaleDenominator = null);

/// <summary>
/// Mutates the live viewer's navigator to a specific WGS-84 viewport
/// so that scripted / agent-driven measurement runs can drive pan,
/// zoom, and viewport-change scenarios from outside the GUI.
/// Distinguished from the read-only <c>render_to_image</c> tool, which
/// clones the Map: this tool intentionally
/// mutates the live <see cref="IMapViewportController"/>.
/// </summary>
/// <remarks>
/// <para>
/// Closes part of the tooling gap identified in the perf-report: MCP
/// could not previously drive viewport changes from outside, so warm
/// "pan/zoom" measurements were impossible without the GUI in the
/// loop.
/// </para>
/// <para>
/// Validation:
/// <list type="bullet">
/// <item><description>Latitudes must be in [-90, 90]; longitudes in [-180, 180].</description></item>
/// <item><description>Bbox: <c>south &lt; north</c> and <c>west &lt; east</c> (no antimeridian wrap).</description></item>
/// <item><description>Zoom: in [0, 24] and finite.</description></item>
/// <item><description>Scale denominator: positive and finite.</description></item>
/// <item><description>Exactly one of {bbox, centre+zoom, centre+scale} must be fully supplied; partial / mixed forms are rejected.</description></item>
/// </list>
/// </para>
/// </remarks>
internal sealed class SetViewportTool
{
    /// <summary>Public tool name as exposed over MCP.</summary>
    public const string Name = "set_viewport";

    internal const double MinLat = -90.0;
    internal const double MaxLat = 90.0;
    internal const double MinLon = -180.0;
    internal const double MaxLon = 180.0;
    internal const double MinZoom = 0.0;
    internal const double MaxZoom = 24.0;
    internal const double FullCircleDegrees = 360.0;

    // Standard web-mercator resolution (metres / pixel) at zoom 0 with a
    // 256-pixel tile, used to convert a zoom level to the resolution the
    // Mapsui Navigator wants. 156543.0339... = 2 * pi * 6378137 / 256.
    internal const double ResolutionAtZoomZero = 156543.03392804097;

    private readonly ICapabilityAccessor<IMapViewportController> _accessor;

    /// <summary>Creates a new <see cref="SetViewportTool"/>.</summary>
    public SetViewportTool(ICapabilityAccessor<IMapViewportController> accessor)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        _accessor = accessor;
    }

    /// <summary>Executes the tool.</summary>
    public Task<ToolResult<SetViewportResult>> InvokeAsync(
        SetViewportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var hasBboxAny = request.South.HasValue || request.West.HasValue
            || request.North.HasValue || request.East.HasValue;
        var hasCenterAny = request.CenterLat.HasValue || request.CenterLon.HasValue
            || request.Zoom.HasValue || request.ScaleDenominator.HasValue;

        if (hasBboxAny && hasCenterAny)
        {
            return Err(new InvalidArgument(
                "request",
                "supply EITHER a bbox (south/west/north/east) OR a centre form (centerLat/centerLon with zoom or scaleDenominator), not both"));
        }
        if (!hasBboxAny && !hasCenterAny)
        {
            return Err(new InvalidArgument(
                "request",
                "must supply a bbox (south/west/north/east), centre+zoom (centerLat/centerLon/zoom), or centre+scale (centerLat/centerLon/scaleDenominator)"));
        }
        if (request.Zoom.HasValue && request.ScaleDenominator.HasValue)
        {
            return Err(new InvalidArgument(
                "request",
                "supply EITHER zoom OR scaleDenominator with centerLat/centerLon, not both"));
        }

        var host = _accessor.Current;
        if (host is null)
        {
            return Err(new MapNotReady("the viewer's map control has not been initialised yet"));
        }

        // Rotation is an optional modifier on whichever frame form is supplied;
        // normalise any finite value into [0, 360). It must accompany a frame so
        // the echoed bbox stays meaningful — to rotate in place, re-issue the
        // same centre+zoom (cheap; duration 0) with the new rotation.
        if (request.Rotation is { } rot
            && (double.IsNaN(rot) || double.IsInfinity(rot)))
        {
            return Err(new InvalidArgument("rotation", $"value {rot} is not a finite number"));
        }
        double rotation = request.Rotation is { } r
            ? ((r % FullCircleDegrees) + FullCircleDegrees) % FullCircleDegrees
            : 0.0;

        return hasBboxAny
            ? ApplyBbox(request, host, rotation)
            : ApplyCenter(request, host, rotation);
    }

    private static Task<ToolResult<SetViewportResult>> ApplyBbox(
        SetViewportRequest request,
        IMapViewportController host,
        double rotation)
    {
        if (request.South is not { } south
            || request.West is not { } west
            || request.North is not { } north
            || request.East is not { } east)
        {
            return Err(new InvalidArgument(
                "request",
                "bbox form requires all four of south, west, north, east"));
        }

        if (Validate(south, "south", MinLat, MaxLat) is { } e1) return Err(e1);
        if (Validate(west, "west", MinLon, MaxLon) is { } e2) return Err(e2);
        if (Validate(north, "north", MinLat, MaxLat) is { } e3) return Err(e3);
        if (Validate(east, "east", MinLon, MaxLon) is { } e4) return Err(e4);

        if (south >= north)
        {
            return Err(new GeometryInvalid(
                "request",
                $"south ({south}) must be less than north ({north})"));
        }
        if (west >= east)
        {
            return Err(new GeometryInvalid(
                "request",
                $"west ({west}) must be less than east ({east}); antimeridian crossing is not supported in v1"));
        }

        var (minX, minY) = SphericalMercator.FromLonLat(west, south);
        var (maxX, maxY) = SphericalMercator.FromLonLat(east, north);
        host.SetViewportToExtent(new MRect(minX, minY, maxX, maxY));
        host.SetRotation(rotation);

        var scale = ReadAppliedScale(host, fallbackLatitude: (south + north) / 2.0, fallbackResolution: null);
        return Ok(new SetViewportResult("bbox", south, west, north, east, rotation, scale));
    }

    private static Task<ToolResult<SetViewportResult>> ApplyCenter(
        SetViewportRequest request,
        IMapViewportController host,
        double rotation)
    {
        if (request.CenterLat is not { } lat
            || request.CenterLon is not { } lon
            || (request.Zoom is null && request.ScaleDenominator is null))
        {
            return Err(new InvalidArgument(
                "request",
                request.ScaleDenominator.HasValue
                    ? "centre+scale form requires all three of centerLat, centerLon, scaleDenominator"
                    : "centre+zoom form requires all three of centerLat, centerLon, zoom (or use scaleDenominator in place of zoom)"));
        }

        if (Validate(lat, "centerLat", MinLat, MaxLat) is { } e1) return Err(e1);
        if (Validate(lon, "centerLon", MinLon, MaxLon) is { } e2) return Err(e2);

        double resolution;
        if (request.ScaleDenominator is { } scaleDenominator)
        {
            if (!double.IsFinite(scaleDenominator) || scaleDenominator <= 0)
            {
                return Err(new InvalidArgument(
                    "scaleDenominator",
                    $"value {scaleDenominator} must be a positive, finite number"));
            }

            resolution = MapScaleFormatter.ScaleDenominatorToResolution(scaleDenominator, lat);
        }
        else
        {
            var zoom = request.Zoom!.Value;
            if (double.IsNaN(zoom) || double.IsInfinity(zoom))
            {
                return Err(new InvalidArgument("zoom", $"value {zoom} is not a finite number"));
            }
            if (zoom < MinZoom || zoom > MaxZoom)
            {
                return Err(new InvalidArgument(
                    "zoom",
                    $"value {zoom} is outside the supported range [{MinZoom}, {MaxZoom}]"));
            }

            resolution = ResolutionAtZoomZero / Math.Pow(2, zoom);
        }

        var (cx, cy) = SphericalMercator.FromLonLat(lon, lat);
        host.SetViewportToCenterAndResolution(new MPoint(cx, cy), resolution);
        host.SetRotation(rotation);

        // Echo the WGS-84 frame implied by the centre + resolution so callers
        // can verify what was applied. The half-extent is computed in
        // Mercator and back-projected, which keeps the verification
        // identical to whatever the navigator will surface to the
        // render pass.
        var (south, west, north, east) = ResolveCenterFrame(cx, cy, resolution);
        var scale = ReadAppliedScale(host, fallbackLatitude: lat, fallbackResolution: resolution);
        return Ok(new SetViewportResult("center", south, west, north, east, rotation, scale));
    }

    /// <summary>
    /// Reads the scale the live map settled on — after zoom limits and, for a
    /// bbox, the fit to the control — using the status-bar conversion. Falls
    /// back to <paramref name="fallbackResolution"/> at
    /// <paramref name="fallbackLatitude"/> when the map is not laid out yet.
    /// </summary>
    private static double? ReadAppliedScale(
        IMapViewportController host, double fallbackLatitude, double? fallbackResolution)
    {
        if (host.TryGetViewportResolution() is { } liveResolution)
        {
            var latitude = host.TryGetViewportCenterWgs84()?.Latitude ?? fallbackLatitude;
            return MapScaleFormatter.ResolutionToScaleDenominator(liveResolution, latitude);
        }

        return fallbackResolution is { } resolution
            ? MapScaleFormatter.ResolutionToScaleDenominator(resolution, fallbackLatitude)
            : null;
    }

    /// <summary>
    /// Approximates the WGS-84 frame produced by the centre forms
    /// using a default 1024×768 reference viewport so the echoed bbox
    /// is reproducible regardless of the live control's current size.
    /// The reference matches the default render_to_image dimensions so
    /// scripted runs that pair set_viewport + render_to_image see a
    /// coherent frame.
    /// </summary>
    private static (double South, double West, double North, double East) ResolveCenterFrame(
        double centerMx, double centerMy, double resolution)
    {
        const int refWidthPx = 1024;
        const int refHeightPx = 768;
        var halfWMeters = refWidthPx * resolution * 0.5;
        var halfHMeters = refHeightPx * resolution * 0.5;
        var (west, south) = SphericalMercator.ToLonLat(centerMx - halfWMeters, centerMy - halfHMeters);
        var (east, north) = SphericalMercator.ToLonLat(centerMx + halfWMeters, centerMy + halfHMeters);
        // Clamp to valid WGS-84 ranges in case the centre+zoom strays
        // close to the poles where the Mercator projection diverges.
        return (Math.Clamp(south, MinLat, MaxLat), Math.Clamp(west, MinLon, MaxLon),
                Math.Clamp(north, MinLat, MaxLat), Math.Clamp(east, MinLon, MaxLon));
    }

    private static ToolError? Validate(double value, string name, double min, double max)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return new InvalidArgument(name, $"value {value} is not a finite number");
        if (value < min || value > max)
            return new InvalidArgument(name, $"value {value} is outside the WGS-84 range [{min}, {max}]");
        return null;
    }

    private static Task<ToolResult<SetViewportResult>> Ok(SetViewportResult value)
        => Task.FromResult(ToolResult<SetViewportResult>.Ok(value));

    private static Task<ToolResult<SetViewportResult>> Err(ToolError error)
        => Task.FromResult(ToolResult<SetViewportResult>.Err(error));
}
