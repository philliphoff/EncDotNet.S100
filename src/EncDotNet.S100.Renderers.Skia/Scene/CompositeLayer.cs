using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Rendering.Scene;
using EncDotNet.S100.Renderers.Skia;
using SkiaSharp;

namespace EncDotNet.S100.Renderers.Skia.Scene;

/// <summary>
/// A single renderer-neutral drawable in a headless composite. Each layer knows
/// how to paint itself onto a canvas against a <em>shared</em>
/// <see cref="Viewport"/>, so that multiple layered S-100 datasets (e.g. an
/// S-101 ENC beneath an S-102 bathymetry surface) register pixel-for-pixel.
/// The compositor draws layers in list order (bottom-most first), reproducing
/// the S-98 cross-dataset draw order already resolved by the
/// <c>SubLayerStackItem</c> engine.
/// </summary>
public abstract class CompositeLayer
{
    private protected CompositeLayer()
    {
    }

    /// <summary>
    /// Paints this layer onto <paramref name="canvas"/> against the shared
    /// composite <paramref name="viewport"/>. Implementations must not clear the
    /// canvas — the compositor clears the background once before drawing the
    /// ordered layer list.
    /// </summary>
    /// <param name="canvas">The composite canvas.</param>
    /// <param name="viewport">The shared composite viewport (pixel space).</param>
    public abstract void Draw(SKCanvas canvas, Viewport viewport);
}

/// <summary>
/// A <see cref="CompositeLayer"/> that paints a lowered vector
/// <see cref="VectorScene"/> via <see cref="SkiaDisplayListRenderer"/>.
/// </summary>
public sealed class VectorCompositeLayer : CompositeLayer
{
    private readonly VectorScene _scene;
    private readonly RgbaColor _background;
    private readonly bool _honorScaleVisibility;

    /// <summary>
    /// Creates a vector composite layer from a resolved scene.
    /// </summary>
    /// <param name="scene">The lowered vector scene (from <c>VectorSceneBuilder</c>).</param>
    /// <param name="honorScaleVisibility">
    /// Whether to apply S-100 Part 9 §11.1 scale-visibility culling using the
    /// shared viewport's scale denominator. Defaults to <see langword="true"/>.
    /// </param>
    public VectorCompositeLayer(VectorScene scene, bool honorScaleVisibility = true)
    {
        ArgumentNullException.ThrowIfNull(scene);
        _scene = scene;
        // The composite background is cleared once by the compositor, so each
        // vector layer paints transparently over what is already there.
        _background = RgbaColor.Transparent;
        _honorScaleVisibility = honorScaleVisibility;
    }

    /// <inheritdoc/>
    public override void Draw(SKCanvas canvas, Viewport viewport)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(viewport);

        var renderer = new SkiaDisplayListRenderer
        {
            Background = _background,
            HonorScaleVisibility = _honorScaleVisibility,
        };
        renderer.RenderOnto(canvas, _scene, viewport);
    }
}

/// <summary>
/// A <see cref="CompositeLayer"/> that paints a styled coverage surface
/// (colour-band fill and/or oriented arrows) via
/// <see cref="CoverageHeadlessRenderer.DrawOnto"/>, projecting the coverage's
/// geographic extent into the shared viewport's pixel space.
/// </summary>
public sealed class CoverageCompositeLayer : CompositeLayer
{
    private readonly StyledCoverageLayer _layer;
    private readonly double _west;
    private readonly double _east;
    private readonly double _south;
    private readonly double _north;
    private readonly CoverageHeadlessRenderer _renderer;

    /// <summary>
    /// Creates a coverage composite layer.
    /// </summary>
    /// <param name="layer">The styled coverage layer (colour and/or symbol scheme).</param>
    /// <param name="westLongitude">Western extent edge in WGS84 degrees.</param>
    /// <param name="eastLongitude">Eastern extent edge in WGS84 degrees.</param>
    /// <param name="southLatitude">Southern extent edge in WGS84 degrees.</param>
    /// <param name="northLatitude">Northern extent edge in WGS84 degrees.</param>
    /// <param name="arrowRenderer">Optional arrow renderer for symbol schemes.</param>
    /// <param name="nativeToWgs84">Transform from the grid's native CRS to WGS84; defaults to identity.</param>
    public CoverageCompositeLayer(
        StyledCoverageLayer layer,
        double westLongitude,
        double eastLongitude,
        double southLatitude,
        double northLatitude,
        SkiaCoverageArrowRenderer? arrowRenderer = null,
        ICrsTransform? nativeToWgs84 = null)
    {
        ArgumentNullException.ThrowIfNull(layer);
        _layer = layer;
        _west = westLongitude;
        _east = eastLongitude;
        _south = southLatitude;
        _north = northLatitude;
        _renderer = new CoverageHeadlessRenderer
        {
            // Coverage layers paint over what is already composited, so no-data
            // and out-of-coverage areas must stay transparent.
            Background = RgbaColor.Transparent,
            NoDataColor = RgbaColor.Transparent,
            ArrowRenderer = arrowRenderer,
            NativeToWgs84 = nativeToWgs84 ?? IdentityCrsTransform.Instance,
        };
    }

    /// <inheritdoc/>
    public override void Draw(SKCanvas canvas, Viewport viewport)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(viewport);
        _renderer.DrawOnto(canvas, viewport, _layer, _west, _east, _south, _north);
    }
}
