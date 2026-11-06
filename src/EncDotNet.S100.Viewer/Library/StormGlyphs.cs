using System.Collections.Concurrent;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using EncDotNet.S100.Datasets.Weather;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Portrayals.Application;
using SkiaSharp;

namespace EncDotNet.S100.Viewer.Library;

/// <summary>
/// The class symbols a storm's Library row shows as its swatch (V27), drawn
/// once each as the Layer stack draws legend symbols, in the Day palette.
/// </summary>
internal static class StormGlyphs
{
    /// <summary>The device scale glyphs are drawn at, so they stay sharp on a high-density display.</summary>
    private const float DeviceScale = 2f;

    private static readonly ConcurrentDictionary<string, IImage> Cache = new(StringComparer.Ordinal);

    /// <summary>The picture of <paramref name="symbol"/>, a <c>WXTC*</c> class symbol.</summary>
    /// <param name="symbol">The symbol's name.</param>
    public static IImage For(string symbol) => Cache.GetOrAdd(symbol, Render);

    private static IImage Render(string symbol)
    {
        var legend = new LegendBuilder(WeatherPalette.Palette, PaletteType.Day, WeatherCoverageStyle.Symbols.Provider);
        using var bitmap = Renderers.Skia.LegendGlyphRenderer.Render(legend.Symbol(symbol, symbol), DeviceScale);
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(png.ToArray());
        return new Bitmap(stream);
    }
}
