using EncDotNet.S100.Renderers.Skia.Scene;
using SkiaSharp;

namespace EncDotNet.S100.Pipelines.Tests;

public class PointGlyphCompositeLayerTests
{
    [Fact]
    public void Draw_MalformedSvg_SkipsGlyph()
    {
        var layer = new PointGlyphCompositeLayer(
        [
            new SkiaPointGlyph
            {
                MercatorX = 0,
                MercatorY = 0,
                Symbol = SkiaPointGlyphSymbol.Svg,
                SvgSource = "<svg><",
                FillColor = new RgbaColor(0, 0, 0),
                OutlineColor = new RgbaColor(0, 0, 0),
            },
        ]);
        var viewport = new Viewport
        {
            MinLatitude = -1,
            MaxLatitude = 1,
            MinLongitude = -1,
            MaxLongitude = 1,
            WidthPixels = 64,
            HeightPixels = 64,
            ScaleDenominator = 1,
        };
        using var bitmap = new SKBitmap(64, 64);
        using var canvas = new SKCanvas(bitmap);

        var exception = Record.Exception(() => layer.Draw(canvas, viewport));

        Assert.Null(exception);
    }
}
