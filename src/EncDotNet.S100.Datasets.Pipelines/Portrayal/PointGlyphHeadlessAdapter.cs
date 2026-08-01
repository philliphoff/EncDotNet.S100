using EncDotNet.S100.Pipelines;
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
            // PointGlyph stores Mapsui's counter-clockwise rotation. Skia's
            // screen coordinate system uses clockwise-positive rotation.
            RotationDegrees = -glyph.Rotation,
        }).ToArray();

        return new PointGlyphCompositeLayer(glyphs);
    }

    public static SKBitmap Render(
        GlyphCoverageSubLayer subLayer,
        MercatorBounds extent,
        int widthPixels,
        int heightPixels,
        RgbaColor background)
    {
        var viewport = FitViewport(extent, widthPixels, heightPixels);
        return new HeadlessCompositeRenderer
        {
            Background = background,
        }.Render(viewport, [CreateLayer(subLayer)]);
    }

    private static Viewport FitViewport(MercatorBounds extent, int widthPixels, int heightPixels)
    {
        double minX = extent.MinX;
        double minY = extent.MinY;
        double maxX = extent.MaxX;
        double maxY = extent.MaxY;
        double spanX = maxX - minX;
        double spanY = maxY - minY;

        double padX = spanX > 0 ? spanX * 0.1 : 1000;
        double padY = spanY > 0 ? spanY * 0.1 : 1000;
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
