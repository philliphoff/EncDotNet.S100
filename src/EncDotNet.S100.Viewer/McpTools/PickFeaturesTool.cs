using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using EncDotNet.S100.Core;
using EncDotNet.S100.Mcp.Tools;
using EncDotNet.S100.Mcp.Tools.Catalog;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>
/// Request payload for <see cref="PickFeaturesTool"/>.
/// </summary>
/// <remarks>
/// Two mutually-exclusive forms are accepted: a screen pixel
/// (<paramref name="X"/>/<paramref name="Y"/>, relative to the live
/// on-screen viewport's top-left in device-independent pixels) or a
/// WGS-84 geographic point (<paramref name="Latitude"/>/<paramref name="Longitude"/>).
/// Exactly one form must be fully supplied; mixing or omitting both is
/// rejected with <see cref="InvalidArgument"/>.
/// </remarks>
[Description("Request for pick_features: supply EITHER a screen pixel (x/y, device-independent pixels from the live viewport's top-left) OR a WGS-84 geographic point (latitude/longitude). The pixel form is the inverse of render_to_image — it resolves the pixel through the live map's projection and returns the vector features under it.")]
internal sealed record PickFeaturesRequest(
    [property: Description("Screen X in device-independent pixels from the live viewport's left edge. Must be paired with y; mutually exclusive with latitude/longitude.")] double? X = null,
    [property: Description("Screen Y in device-independent pixels from the live viewport's top edge. Must be paired with x; mutually exclusive with latitude/longitude.")] double? Y = null,
    [property: Description("Pick latitude in decimal degrees (WGS-84). Must be paired with longitude; mutually exclusive with the x/y form.")] double? Latitude = null,
    [property: Description("Pick longitude in decimal degrees (WGS-84). Must be paired with latitude; mutually exclusive with the x/y form.")] double? Longitude = null,
    [property: Description("Optional spec filter; null matches every vector spec.")] SpecRef? Spec = null,
    [property: Description("Search tolerance for point/curve features in metres; area features use exact containment and ignore it. Clamped to [0, 100000]. Default 50.")] double RadiusMeters = 50.0,
    [property: Description("Maximum ranked matches to return; clamped to [1, 200]. Default 20.")] int MaxResults = 20);

/// <summary>Result of <see cref="PickFeaturesTool"/>.</summary>
[Description("Result of pick_features: the input form that resolved the pick (pixel or geo), the resolved WGS-84 point, and the ranked vector features under it (identical shape to identify_features).")]
internal sealed record PickFeaturesResult(
    [property: Description("\"pixel\" when the pick resolved through the x/y form; \"geo\" when it resolved through latitude/longitude.")] string Source,
    [property: Description("Resolved pick latitude in decimal degrees, WGS-84.")] double Latitude,
    [property: Description("Resolved pick longitude in decimal degrees, WGS-84.")] double Longitude,
    [property: Description("Ranked matches, most-specific first (point before curve before area; ties broken by smaller area / nearer distance).")] System.Collections.Immutable.ImmutableArray<IdentifyMatch> Features,
    [property: Description("Total number of features that matched before applying maxResults.")] int TotalMatched,
    [property: Description("True when more features matched than were returned.")] bool Truncated);

/// <summary>
/// Resolves the vector features under a screen pixel (or a geographic
/// point) on the live viewer map — the inverse of
/// <see cref="RenderToImageTool"/>, closing the "pixels in, features out"
/// loop so automated portrayal QA can ask "what is drawn here?" about a
/// specific spot in a captured image.
/// </summary>
/// <remarks>
/// <para>
/// The pixel form projects the requested screen coordinate through the
/// live map's navigator (the same Web-Mercator projection used to render
/// it) to a WGS-84 point, then delegates to the shared
/// <see cref="IdentifyFeaturesTool"/> ranking so a pick and a geographic
/// identify return an identical feature shape. The geographic form skips
/// the projection step and is available even before the map is laid out.
/// </para>
/// <para>
/// This is a read-only tool: it observes the live viewport and the loaded
/// dataset catalog without mutating either.
/// </para>
/// </remarks>
internal sealed class PickFeaturesTool
{
    /// <summary>Public tool name as exposed over MCP.</summary>
    public const string Name = "pick_features";

    private readonly IMapHostAccessor _accessor;
    private readonly IdentifyFeaturesTool _identify;

    /// <summary>Creates a new <see cref="PickFeaturesTool"/>.</summary>
    public PickFeaturesTool(IMapHostAccessor accessor, IDatasetCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        ArgumentNullException.ThrowIfNull(catalog);
        _accessor = accessor;
        _identify = new IdentifyFeaturesTool(catalog);
    }

    /// <summary>Executes the tool.</summary>
    public async Task<ToolResult<PickFeaturesResult>> InvokeAsync(
        PickFeaturesRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var hasPixel = request.X.HasValue || request.Y.HasValue;
        var hasGeo = request.Latitude.HasValue || request.Longitude.HasValue;

        if (hasPixel && hasGeo)
        {
            return Err(new InvalidArgument(
                "request",
                "supply EITHER a screen pixel (x/y) OR a geographic point (latitude/longitude), not both"));
        }
        if (!hasPixel && !hasGeo)
        {
            return Err(new InvalidArgument(
                "request",
                "must supply either a screen pixel (x/y) or a geographic point (latitude/longitude)"));
        }

        string source;
        double latitude;
        double longitude;

        if (hasPixel)
        {
            var resolved = ResolvePixelPoint(request, out var error);
            if (error is not null)
            {
                return Err(error);
            }

            source = "pixel";
            (latitude, longitude) = resolved!.Value;
        }
        else
        {
            if (request.Latitude is not { } lat || request.Longitude is not { } lon)
            {
                return Err(new InvalidArgument(
                    "request",
                    "geographic form requires both latitude and longitude"));
            }

            source = "geo";
            latitude = lat;
            longitude = lon;
        }

        var identified = await _identify.InvokeAsync(
            new IdentifyFeaturesRequest(latitude, longitude, request.Spec, request.RadiusMeters, request.MaxResults),
            cancellationToken).ConfigureAwait(false);

        if (!identified.TryGetValue(out var value))
        {
            identified.TryGetError(out var err);
            return ToolResult<PickFeaturesResult>.Err(err!);
        }

        return ToolResult<PickFeaturesResult>.Ok(new PickFeaturesResult(
            source,
            value.Point.Latitude,
            value.Point.Longitude,
            value.Features,
            value.TotalMatched,
            value.Truncated));
    }

    private static ToolError? ResolvePixel(PickFeaturesRequest request)
    {
        if (request.X is not { } x || request.Y is not { } y)
        {
            return new InvalidArgument("request", "pixel form requires both x and y");
        }

        if (double.IsNaN(x) || double.IsInfinity(x))
        {
            return new InvalidArgument("x", $"value {x} is not a finite number");
        }
        if (double.IsNaN(y) || double.IsInfinity(y))
        {
            return new InvalidArgument("y", $"value {y} is not a finite number");
        }

        return null;
    }

    private (double Latitude, double Longitude)? ResolvePixelPoint(
        PickFeaturesRequest request,
        out ToolError? error)
    {
        if (ResolvePixel(request) is { } validation)
        {
            error = validation;
            return null;
        }

        var host = _accessor.Current;
        if (host is null)
        {
            error = new MapNotReady("the viewer's map control has not been initialised yet");
            return null;
        }

        var x = request.X!.Value;
        var y = request.Y!.Value;

        if (host.TryGetViewportSizePx() is not { } size)
        {
            error = new MapNotReady("the viewer's map viewport has not been laid out yet");
            return null;
        }

        if (x < 0 || x > size.Width || y < 0 || y > size.Height)
        {
            error = new InvalidArgument(
                "request",
                $"pixel ({x}, {y}) is outside the live viewport [0, {size.Width}] x [0, {size.Height}]");
            return null;
        }

        if (host.TryScreenToWgs84(x, y) is not { } point)
        {
            error = new InvalidArgument(
                "request",
                $"pixel ({x}, {y}) does not project to a valid WGS-84 coordinate");
            return null;
        }

        error = null;
        return point;
    }

    private static ToolResult<PickFeaturesResult> Err(ToolError error)
        => ToolResult<PickFeaturesResult>.Err(error);
}
