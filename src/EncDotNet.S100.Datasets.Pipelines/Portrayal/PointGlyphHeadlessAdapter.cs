using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Renderers.Skia.Scene;
using EncDotNet.S100.Rendering.Scene;
using SkiaSharp;

namespace EncDotNet.S100.Datasets.Pipelines.Portrayal;

internal static class PointGlyphHeadlessAdapter
{
    public static PointGlyphCompositeLayer CreateLayer(GlyphCoverageSubLayer subLayer)
    {
        ArgumentNullException.ThrowIfNull(subLayer);

        var glyphs = subLayer.Glyphs.Select(glyph => new SkiaPointGlyph
        {
            MercatorX = glyph.MercatorX,
            MercatorY = glyph.MercatorY,
            Symbol = glyph.Symbol switch
            {
                PointGlyphSymbol.Ellipse => SkiaPointGlyphSymbol.Ellipse,
                PointGlyphSymbol.Triangle => SkiaPointGlyphSymbol.Triangle,
                PointGlyphSymbol.Svg => SkiaPointGlyphSymbol.Svg,
                _ => throw new ArgumentOutOfRangeException(nameof(glyph.Symbol)),
            },
            SvgSource = glyph.SvgSource,
            FillColor = glyph.FillColor,
            OutlineColor = glyph.OutlineColor,
            OutlineWidth = glyph.OutlineWidth,
            SymbolScale = glyph.SymbolScale,
            // PointGlyph.Rotation is clockwise on screen (Mapsui's
            // SymbolRotation convention), as is Skia's RotateDegrees.
            RotationDegrees = glyph.Rotation,
            ThinningPriority = glyph.ThinningPriority,
        }).ToArray();

        return subLayer.Thinning is { } thinning
            ? new PointGlyphCompositeLayer(
                glyphs, thinning.SymbolLengthPixelsPerScale, thinning.MaxSymbolToSpacingRatio)
            : new PointGlyphCompositeLayer(glyphs);
    }

    /// <summary>
    /// Lowers a <see cref="VectorCoverageSubLayer"/> to a scene with the same
    /// builder a vector product uses, so its symbols and labels draw by the
    /// same rules.
    /// </summary>
    public static VectorScene BuildScene(VectorCoverageSubLayer subLayer, DrawingInstructionCategory hiddenCategories) =>
        HeadlessVectorRenderer.BuildScene(
            subLayer.Instructions,
            subLayer.GeometryProvider,
            subLayer.Palette,
            subLayer.SymbolProvider,
            lineStyleProvider: null,
            subLayer.SymbolScale,
            subLayer.TextScale,
            hiddenCategories: hiddenCategories);

    /// <summary>
    /// Renders a <see cref="VectorCoverageSubLayer"/> framed on
    /// <paramref name="extent"/>, honouring scale visibility at the framed scale.
    /// </summary>
    public static SKBitmap Render(
        VectorCoverageSubLayer subLayer,
        MercatorBounds extent,
        int widthPixels,
        int heightPixels,
        RgbaColor background,
        BasemapKind basemap,
        DrawingInstructionCategory hiddenCategories) =>
        RenderFitted(
            new VectorCompositeLayer(BuildScene(subLayer, hiddenCategories)),
            extent, widthPixels, heightPixels, background, basemap, padding: 0.1);

    public static SKBitmap Render(
        GlyphCoverageSubLayer subLayer,
        MercatorBounds extent,
        int widthPixels,
        int heightPixels,
        RgbaColor background,
        BasemapKind basemap = BasemapKind.None)
    {
        return RenderFitted(CreateLayer(subLayer), extent, widthPixels, heightPixels, background, basemap, padding: 0.1);
    }

    /// <summary>
    /// Renders <paramref name="layer"/> into a new bitmap framed on
    /// <paramref name="extent"/> (grown by <paramref name="padding"/> of its span
    /// on each side and to the bitmap's aspect), over an optional land basemap.
    /// </summary>
    internal static SKBitmap RenderFitted(
        CompositeLayer layer,
        MercatorBounds extent,
        int widthPixels,
        int heightPixels,
        RgbaColor background,
        BasemapKind basemap,
        double padding)
    {
        var viewport = FitViewport(extent, widthPixels, heightPixels, padding);
        CompositeLayer[] layers = basemap == BasemapKind.Offline
            ?
            [
                // The land basemap goes under the layer, against the same viewport.
                new VectorCompositeLayer(NaturalEarthBasemap.GetLandScene(viewport), honorScaleVisibility: false),
                layer,
            ]
            : [layer];
        return new HeadlessCompositeRenderer
        {
            Background = background,
        }.Render(viewport, layers);
    }

    private static Viewport FitViewport(MercatorBounds extent, int widthPixels, int heightPixels, double padding)
    {
        double minX = extent.MinX;
        double minY = extent.MinY;
        double maxX = extent.MaxX;
        double maxY = extent.MaxY;
        double spanX = maxX - minX;
        double spanY = maxY - minY;

        double padX = spanX > 0 ? spanX * padding : 1000;
        double padY = spanY > 0 ? spanY * padding : 1000;
        minX -= padX;
        maxX += padX;
        minY -= padY;
        maxY += padY;
        spanX = maxX - minX;
        spanY = maxY - minY;

        double viewAspect = (double)widthPixels / heightPixels;
        double dataAspect = spanX / spanY;
        if (dataAspect > viewAspect)
        {
            double grow = (spanX / viewAspect - spanY) / 2.0;
            minY -= grow;
            maxY += grow;
        }
        else
        {
            double grow = (spanY * viewAspect - spanX) / 2.0;
            minX -= grow;
            maxX += grow;
        }

        var (minLongitude, minLatitude) = WebMercator.ToLonLat(minX, minY, clampLatitude: false);
        var (maxLongitude, maxLatitude) = WebMercator.ToLonLat(maxX, maxY, clampLatitude: false);
        double middleLatitudeRadians = (minLatitude + maxLatitude) * Math.PI / 360.0;
        double groundMetresPerPixel = (maxX - minX) / widthPixels * Math.Cos(middleLatitudeRadians);

        return new Viewport
        {
            MinLatitude = minLatitude,
            MaxLatitude = maxLatitude,
            MinLongitude = minLongitude,
            MaxLongitude = maxLongitude,
            WidthPixels = widthPixels,
            HeightPixels = heightPixels,
            ScaleDenominator = Math.Max(1.0, groundMetresPerPixel / ScaleVisibility.DenomToResolutionMetres),
        };
    }
}
