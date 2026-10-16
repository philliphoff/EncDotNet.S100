using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Renderers.Skia.Scene;
using SkiaSharp;

namespace EncDotNet.S100.Datasets.Pipelines.Portrayal;

internal static class IsolineHeadlessAdapter
{
    public static IsolineScene CreateScene(IsolineCoverageSubLayer subLayer)
    {
        ArgumentNullException.ThrowIfNull(subLayer);

        return new IsolineScene(
            subLayer.Isolines.Select(line => new IsolinePolyline(line.Label, line.MercatorCoordinates)).ToArray(),
            new IsolineAppearance
            {
                Ink = ToSkia(subLayer.Ink),
                LabelHalo = ToSkia(subLayer.LabelHalo),
                LineWidth = (float)subLayer.LineWidth,
                DashPattern = subLayer.DashPattern.Select(d => (float)d).ToArray(),
                LabelFontSize = (float)subLayer.LabelFontSize,
                LabelSpacing = (float)subLayer.LabelSpacing,
            },
            subLayer.LandAreaMask);
    }

    /// <summary>
    /// Renders the isolines framed on the grid's extent, the way the coverage
    /// raster was framed before the isolines replaced it.
    /// </summary>
    public static SKBitmap Render(
        IsolineCoverageSubLayer subLayer,
        int widthPixels,
        int heightPixels,
        RgbaColor background,
        BasemapKind basemap = BasemapKind.None)
    {
        if (subLayer.Extent is not { } extent)
            throw new InvalidOperationException("The S-104 isolines have no geographic extent.");

        using var scene = CreateScene(subLayer);
        return PointGlyphHeadlessAdapter.RenderFitted(
            new IsolineCompositeLayer(scene), extent, widthPixels, heightPixels, background, basemap, padding: 0);
    }

    private static SKColor ToSkia(RgbaColor color) => new(color.R, color.G, color.B, color.A);
}
