using System.ComponentModel;
using EncDotNet.S100.Core;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Mcp.Tools;
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
    [property: Description("Screen X in pixels from the left edge. Must be paired with y; mutually exclusive with latitude/longitude.")] double? X = null,
    [property: Description("Screen Y in pixels from the top edge. Must be paired with x; mutually exclusive with latitude/longitude.")] double? Y = null,
    [property: Description("Pick latitude in decimal degrees (WGS-84). Must be paired with longitude; mutually exclusive with the x/y form.")] double? Latitude = null,
    [property: Description("Pick longitude in decimal degrees (WGS-84). Must be paired with latitude; mutually exclusive with the x/y form.")] double? Longitude = null,
    [property: Description("Width the pixel's source image was rendered at — pass the 'width' echoed by render_to_image when the pixel comes from a capture. Must be paired with imageHeight. When omitted, x/y are interpreted in the live on-screen viewport's pixel space.")] int? ImageWidth = null,
    [property: Description("Height the pixel's source image was rendered at — pass the 'height' echoed by render_to_image when the pixel comes from a capture. Must be paired with imageWidth.")] int? ImageHeight = null,
    [property: Description("Optional spec filter; null matches every vector spec.")] SpecRef? Spec = null,
    [property: Description("Search tolerance for point/curve features in metres; area features use exact containment and ignore it. Clamped to [0, 100000]. Default 50.")] double RadiusMeters = 50.0,
    [property: Description("Maximum ranked matches to return; clamped to [1, 200]. Default 20.")] int MaxResults = 20,
    [property: Description("When true, also show the pick on the live viewer: the resolved features populate the Object Information panel and the map draws a pick highlight (marker + selected-feature outline), exactly like a user click. Default false (read-only).")] bool Select = false);

/// <summary>Result of <see cref="PickFeaturesTool"/>.</summary>
[Description("Result of pick_features: the input form that resolved the pick (pixel or geo), the resolved WGS-84 point, and the ranked vector features under it (identical shape to identify_features).")]
internal sealed record PickFeaturesResult(
    [property: Description("\"pixel\" when the pick resolved through the x/y form; \"geo\" when it resolved through latitude/longitude.")] string Source,
    [property: Description("Resolved pick latitude in decimal degrees, WGS-84.")] double Latitude,
    [property: Description("Resolved pick longitude in decimal degrees, WGS-84.")] double Longitude,
    [property: Description("Ranked matches: drawn features by drawing priority, highest first, where the dataset was portrayed (S-98 15.1); then most-specific first (point before curve before area; ties broken by smaller area / nearer distance).")] IReadOnlyList<IdentifyMatch> Features,
    [property: Description("Total number of features that matched before applying maxResults.")] int TotalMatched,
    [property: Description("True when more features matched than were returned.")] bool Truncated,
    [property: Description("True when select=true was honoured and the pick was shown on the live viewer (panel + highlight).")] bool Selected = false,
    [property: Description("With select=true, the live overlay features at the point that Object information shows too, such as a predicted tide station or an AIS target; empty otherwise.")] IReadOnlyList<PickDynamicFeature>? DynamicFeatures = null);

/// <summary>A live overlay feature a <c>pick_features</c> select showed.</summary>
[Description("A live overlay feature at the pick: its overlay and its label.")]
internal sealed record PickDynamicFeature(
    [property: Description("The overlay's id, such as 'tide-stations' or 'ndbc'.")] string SourceId,
    [property: Description("The feature's id in the overlay, such as 'tide:9447130'.")] string FeatureId,
    [property: Description("The feature's label, such as the station name.")] string Label);

/// <summary>
/// Resolves the vector features under a screen pixel (or a geographic
/// point) on the live viewer map — the inverse of
/// the <c>render_to_image</c> tool, closing the "pixels in, features out"
/// loop so automated portrayal QA can ask "what is drawn here?" about a
/// specific spot in a captured image.
/// </summary>
/// <remarks>
/// <para>
/// The pixel form projects the requested coordinate through the live
/// map's Web-Mercator projection to a WGS-84 point, then delegates to the
/// shared <see cref="IdentifyFeaturesTool"/> ranking so a pick and a
/// geographic identify return an identical feature shape. Like a click, it
/// returns only what the map draws: time-dependent products at the view time,
/// without the S-124 parts the map hides (<see cref="IMapDrawState"/>). The geographic
/// form skips the projection step and is available even before the map is
/// laid out.
/// </para>
/// <para>
/// A pixel can be interpreted in two coordinate spaces. When
/// <see cref="PickFeaturesRequest.ImageWidth"/> /
/// <see cref="PickFeaturesRequest.ImageHeight"/> are supplied, the pixel
/// is treated as coming from a <c>render_to_image</c> capture of that size
/// and is resolved with the <em>same</em> fit geometry that tool uses —
/// making this a faithful inverse of <c>render_to_image</c> at any capture
/// size or aspect ratio. When they are omitted, the pixel is interpreted
/// in the live on-screen viewport's pixel space.
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

    private readonly ICapabilityAccessor<IMapCoordinateConverter> _accessor;
    private readonly IdentifyFeaturesTool _identify;
    private readonly IGeographicPickPresenter? _presenter;

    /// <summary>
    /// Creates a new <see cref="PickFeaturesTool"/> over
    /// <paramref name="identify"/>, which should follow the map
    /// (<see cref="IMapDrawState"/>) so a pick returns only what the map draws,
    /// with an optional presenter used to honour
    /// <see cref="PickFeaturesRequest.Select"/> by publishing the pick to the
    /// live viewer panel + highlight.
    /// </summary>
    public PickFeaturesTool(
        ICapabilityAccessor<IMapCoordinateConverter> accessor,
        IdentifyFeaturesTool identify,
        IGeographicPickPresenter? presenter)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        ArgumentNullException.ThrowIfNull(identify);
        _accessor = accessor;
        _identify = identify;
        _presenter = presenter;
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

        var selected = false;
        IReadOnlyList<PickDynamicFeature> dynamicFeatures = [];
        if (request.Select && _presenter is not null)
        {
            var refs = new GeographicPickFeature[value.Features.Count];
            for (var i = 0; i < value.Features.Count; i++)
            {
                var match = value.Features[i];
                refs[i] = new GeographicPickFeature(match.DatasetId.Value, match.FeatureId);
            }

            var dynamicHits = _presenter.Present(value.Point.Latitude, value.Point.Longitude, refs);
            dynamicFeatures = [.. dynamicHits.Select(h => new PickDynamicFeature(h.SourceId, h.FeatureId, h.DisplayLabel))];
            selected = refs.Length > 0 || dynamicHits.Count > 0;
        }

        return ToolResult<PickFeaturesResult>.Ok(new PickFeaturesResult(
            source,
            value.Point.Latitude,
            value.Point.Longitude,
            value.Features,
            value.TotalMatched,
            value.Truncated,
            selected,
            dynamicFeatures));
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

        var hasImageWidth = request.ImageWidth.HasValue;
        var hasImageHeight = request.ImageHeight.HasValue;
        if (hasImageWidth != hasImageHeight)
        {
            return new InvalidArgument(
                "request",
                "imageWidth and imageHeight must be supplied together (pass the width and height echoed by render_to_image), or both omitted to use the live viewport");
        }
        if (hasImageWidth && request.ImageWidth!.Value <= 0)
        {
            return new InvalidArgument("imageWidth", $"value {request.ImageWidth!.Value} must be positive");
        }
        if (hasImageHeight && request.ImageHeight!.Value <= 0)
        {
            return new InvalidArgument("imageHeight", $"value {request.ImageHeight!.Value} must be positive");
        }

        return null;
    }

    private GeoPosition? ResolvePixelPoint(
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

        // Image-space form: the pixel comes from a render_to_image capture of
        // a known size — resolve it with that tool's fit geometry so the pick
        // is a faithful inverse regardless of the capture's aspect ratio.
        if (request.ImageWidth is { } imageWidth && request.ImageHeight is { } imageHeight)
        {
            if (host.TryGetViewportSizePx() is null)
            {
                error = new MapNotReady("the viewer's map viewport has not been laid out yet");
                return null;
            }

            if (x < 0 || x > imageWidth || y < 0 || y > imageHeight)
            {
                error = new InvalidArgument(
                    "request",
                    $"pixel ({x}, {y}) is outside the {imageWidth} x {imageHeight} image [0, {imageWidth}] x [0, {imageHeight}]");
                return null;
            }

            if (host.TryImagePixelToWgs84(x, y, imageWidth, imageHeight) is not { } imagePoint)
            {
                error = new InvalidArgument(
                    "request",
                    $"pixel ({x}, {y}) does not project to a valid WGS-84 coordinate");
                return null;
            }

            error = null;
            return imagePoint;
        }

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
