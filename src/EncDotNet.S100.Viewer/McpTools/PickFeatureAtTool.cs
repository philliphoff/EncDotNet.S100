using System;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using EncDotNet.S100.Mcp.Tools;
using EncDotNet.S100.Mcp.Tools.Catalog;
using EncDotNet.S100.Mcp.Tools.Geometry;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>
/// Request payload for <see cref="PickFeatureAtTool"/>.
/// </summary>
[Description("Request for pick_feature_at: a pixel coordinate (x, y) in an image of the given dimensions. Width/height default to render_to_image's 1024x768 so a pixel from a prior render_to_image call resolves to the same world point.")]
internal sealed record PickFeatureAtRequest(
    [property: Description("Horizontal pixel offset from the image's left edge. Must be within [0, width].")] double X,
    [property: Description("Vertical pixel offset from the image's top edge. Must be within [0, height].")] double Y,
    [property: Description("Reference image width in pixels; null defaults to 1024 (render_to_image's default). Clamped to [64, 4096].")] int? Width = null,
    [property: Description("Reference image height in pixels; null defaults to 768 (render_to_image's default). Clamped to [64, 4096].")] int? Height = null);

/// <summary>Result of <see cref="PickFeatureAtTool"/>.</summary>
[Description("Result of pick_feature_at: the world coordinate under the pixel plus the GML features and datasets whose bounds contain it.")]
internal sealed record PickFeatureAtResult(
    [property: Description("Latitude under the pixel in decimal degrees, WGS-84.")] double WorldLatitude,
    [property: Description("Longitude under the pixel in decimal degrees, WGS-84.")] double WorldLongitude,
    [property: Description("Reference image width actually used (post-default/clamp).")] int Width,
    [property: Description("Reference image height actually used (post-default/clamp).")] int Height,
    [property: Description("GML-encoded vector features whose bounding box contains the world point, in catalog order; follow up with describe_feature for full attributes.")] ImmutableArray<FeatureMatch> Features,
    [property: Description("Datasets whose declared bounding box contains the world point, in catalog order.")] ImmutableArray<DatasetSummary> Datasets,
    [property: Description("True when more features matched than were returned (the feature list was truncated).")] bool FeaturesTruncated);

/// <summary>
/// Resolves the world point under a pixel of a (notional)
/// <c>render_to_image</c> capture and reports the GML features and
/// datasets whose bounds contain it. Lets an agent ask "what is at
/// pixel (x, y) of the view I just rendered?" — turning a screenshot
/// coordinate into concrete feature identities for precise visual
/// assertions without pixel-diff baselines.
/// </summary>
/// <remarks>
/// <para>
/// The pixel is converted using the same world extent
/// <c>render_to_image</c> captures at the supplied dimensions, so pixel
/// coordinates are consistent between the two tools. Feature matching is
/// at bounding-box precision (the catalog-wide convention): a feature is
/// returned when its bounding box contains the point, not when a rendered
/// symbol pixel is hit. Coverage products (S-102 / S-104 / S-111) and
/// S-101 contribute datasets but not features — sample them with
/// <c>sample_coverage</c>.
/// </para>
/// </remarks>
internal sealed class PickFeatureAtTool
{
    /// <summary>Public tool name as exposed over MCP.</summary>
    public const string Name = "pick_feature_at";

    internal const int DefaultWidth = 1024;
    internal const int DefaultHeight = 768;
    internal const int MinDimension = 64;
    internal const int MaxDimension = 4096;

    // Cap the feature list so a pick over a dense dataset cannot return
    // an unbounded payload; the truncation flag signals when this bites.
    internal const int MaxFeatures = 200;

    private readonly IMapHostAccessor _accessor;
    private readonly QueryFeaturesTool _queryFeatures;
    private readonly FindAtTool _findAt;

    /// <summary>Creates a new <see cref="PickFeatureAtTool"/>.</summary>
    public PickFeatureAtTool(IMapHostAccessor accessor, IDatasetCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        ArgumentNullException.ThrowIfNull(catalog);
        _accessor = accessor;
        _queryFeatures = new QueryFeaturesTool(catalog);
        _findAt = new FindAtTool(catalog);
    }

    /// <summary>Executes the tool.</summary>
    public async Task<ToolResult<PickFeatureAtResult>> InvokeAsync(
        PickFeatureAtRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (NotFinite(request.X))
            return Err(new InvalidArgument("x", $"value {request.X} is not a finite number"));
        if (NotFinite(request.Y))
            return Err(new InvalidArgument("y", $"value {request.Y} is not a finite number"));

        var width = ResolveDimension(request.Width, DefaultWidth);
        var height = ResolveDimension(request.Height, DefaultHeight);

        if (request.X < 0 || request.X > width)
            return Err(new InvalidArgument("x", $"value {request.X} is outside the image width [0, {width}]"));
        if (request.Y < 0 || request.Y > height)
            return Err(new InvalidArgument("y", $"value {request.Y} is outside the image height [0, {height}]"));

        var host = _accessor.Current;
        if (host is null)
            return Err(new MapNotReady("the viewer's map control has not been initialised yet"));

        if (host.TryScreenToWgs84(request.X, request.Y, width, height) is not { } world)
            return Err(new MapNotReady("no current viewport (map has no size yet) or the pixel resolved outside valid WGS-84 ranges"));

        var point = new GeoQuery.Point(new GeoPoint(world.Latitude, world.Longitude));

        var featuresResult = await _queryFeatures.InvokeAsync(
            new QueryFeaturesRequest(point, PageSize: MaxFeatures),
            cancellationToken).ConfigureAwait(false);
        if (!featuresResult.TryGetValue(out var features))
        {
            featuresResult.TryGetError(out var fErr);
            return ToolResult<PickFeatureAtResult>.Err(fErr!);
        }

        var datasetsResult = await _findAt.InvokeAsync(
            new FindAtRequest(world.Latitude, world.Longitude, PageSize: 500),
            cancellationToken).ConfigureAwait(false);
        if (!datasetsResult.TryGetValue(out var datasets))
        {
            datasetsResult.TryGetError(out var dErr);
            return ToolResult<PickFeatureAtResult>.Err(dErr!);
        }

        return ToolResult<PickFeatureAtResult>.Ok(new PickFeatureAtResult(
            world.Latitude,
            world.Longitude,
            width,
            height,
            features.Features,
            datasets.Datasets,
            features.HasMore));
    }

    private static int ResolveDimension(int? requested, int @default)
        => requested is null ? @default : Math.Clamp(requested.Value, MinDimension, MaxDimension);

    private static bool NotFinite(double v) => double.IsNaN(v) || double.IsInfinity(v);

    private static Task<ToolResult<PickFeatureAtResult>> Err(ToolError error)
        => Task.FromResult(ToolResult<PickFeatureAtResult>.Err(error));
}
