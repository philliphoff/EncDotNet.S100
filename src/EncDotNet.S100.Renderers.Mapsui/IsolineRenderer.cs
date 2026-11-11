using EncDotNet.S100.Renderers.Skia.Scene;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Rendering;
using Mapsui.Rendering.Skia.SkiaStyles;
using Mapsui.Styles;
using SkiaSharp;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// The Mapsui style carrying an <see cref="IsolineScene"/>; drawn by
/// <see cref="IsolineRenderer"/>. Attach it to one feature covering the scene's
/// extent so Mapsui fetches it whenever the extent is in view.
/// </summary>
public sealed class IsolineStyle : BaseStyle
{
    /// <summary>The isolines to draw.</summary>
    public required IsolineScene Scene { get; init; }
}

/// <summary>
/// Draws an <see cref="IsolineStyle"/> in screen space each frame, so dashes and
/// label spacing stay the same at every zoom.
/// </summary>
public sealed class IsolineRenderer : ISkiaStyleRenderer
{
    /// <summary>Singleton instance for registration.</summary>
    public static IsolineRenderer Instance { get; } = new();

    internal static void Register() =>
        global::Mapsui.Rendering.Skia.MapRenderer.RegisterStyleRenderer(typeof(IsolineStyle), Instance);

    /// <inheritdoc/>
    public bool Draw(SKCanvas canvas, Viewport viewport, ILayer layer, IFeature feature, IStyle style, RenderService renderService, long iteration)
    {
        if (style is not IsolineStyle { Scene: var scene })
            return false;

        scene.Draw(
            canvas,
            (x, y) =>
            {
                var (sx, sy) = viewport.WorldToScreenXY(x, y);
                return new SKPoint((float)sx, (float)sy);
            },
            (float)Math.Clamp(layer.Opacity * style.Opacity, 0, 1));
        return true;
    }
}
