using System.Runtime.CompilerServices;
using System.Xml.Linq;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Portrayals;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Styles;
using SkiaSharp;

using PipelineViewport = EncDotNet.S100.Pipelines.Viewport;

[assembly: InternalsVisibleTo("EncDotNet.S100.Datasets.S111.Tests")]

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Renders oriented symbols (e.g. current arrows) from a <see cref="StyledCoverageLayer"/>
/// as a georeferenced raster that scales with map zoom. Symbol geometry is parsed from
/// the portrayal catalogue SVGs at runtime using <see cref="SKPath.ParseSvgPathData"/>,
/// so the renderer is not coupled to any specific symbol shape.
/// </summary>
public sealed class MapsuiCoverageArrowRenderer
{
    private const int MaxDim = 4096;
    private static readonly XNamespace SvgNs = "http://www.w3.org/2000/svg";

    private readonly ICrsTransformFactory _transformFactory;
    private readonly Dictionary<string, ParsedSymbol?> _symbolCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Name assigned to the generated Mapsui layer.</summary>
    public string LayerName { get; set; } = "Coverage Arrows";

    /// <summary>Layer opacity (0.0–1.0). Defaults to 1.0.</summary>
    public double Opacity { get; set; } = 1.0;

    /// <summary>
    /// Target maximum number of arrows along the longest grid axis.
    /// The renderer computes a stride so that no more than roughly
    /// <c>MaxArrowsPerAxis²</c> arrows are produced.
    /// Set to 0 to disable subsampling.
    /// </summary>
    public int MaxArrowsPerAxis { get; set; } = 80;

    /// <summary>
    /// Minimum number of pixels allocated per arrow in the output raster
    /// (along the shorter arrow axis). Controls the output bitmap resolution.
    /// </summary>
    public int MinArrowPixels { get; set; } = 60;

    /// <summary>
    /// The color palette used to resolve SVG CSS fill/stroke tokens.
    /// </summary>
    public ColorPalette? Palette { get; set; }

    /// <summary>
    /// Returns raw SVG content for a symbol reference name (e.g. "SCAROW01").
    /// </summary>
    public required Func<string, string?> SymbolProvider { get; set; }

    public MapsuiCoverageArrowRenderer(ICrsTransformFactory transformFactory)
    {
        _transformFactory = transformFactory;
    }

    /// <summary>
    /// Renders the symbol scheme from a styled coverage layer into a georeferenced
    /// raster layer with rotated symbol polygons. Returns <c>null</c> if the layer
    /// has no symbol scheme.
    /// </summary>
    public ILayer? Render(StyledCoverageLayer layer, PipelineViewport viewport)
    {
        var symbolScheme = layer.SymbolScheme;
        if (symbolScheme is null)
            return null;

        var sampled = layer.Coverage;
        var georeferencer = layer.Georeferencer;
        var valueData = sampled.GetField(symbolScheme.ValueFieldName);
        var rotationData = sampled.GetField(symbolScheme.RotationFieldName);
        int srcRows = valueData.GetLength(0);
        int srcCols = valueData.GetLength(1);

        float noDataValue = layer.NoDataValue;
        bool noDataIsNaN = float.IsNaN(noDataValue);

        // Build CRS transform: native grid CRS → WGS84
        var nativeToWgs84 = _transformFactory.Create(georeferencer.CRS, "EPSG:4326");

        // Compute stride to keep arrow count manageable on large grids.
        int stride = 1;
        if (MaxArrowsPerAxis > 0)
        {
            int longestAxis = Math.Max(srcRows, srcCols);
            stride = Math.Max(1, (longestAxis + MaxArrowsPerAxis - 1) / MaxArrowsPerAxis);
        }

        // Project grid corners to Mercator to determine extent.
        double mercMinX = double.MaxValue, mercMinY = double.MaxValue;
        double mercMaxX = double.MinValue, mercMaxY = double.MinValue;

        void ExpandExtent(int r, int c)
        {
            var (nX, nY) = georeferencer.ToNative(r, c);
            double lon, lat;
            if (nativeToWgs84.IsIdentity) { lon = nX; lat = nY; }
            else { (lon, lat) = nativeToWgs84.Transform(nX, nY); }
            var (mx, my) = SphericalMercator.FromLonLat(lon, lat);
            if (mx < mercMinX) mercMinX = mx;
            if (my < mercMinY) mercMinY = my;
            if (mx > mercMaxX) mercMaxX = mx;
            if (my > mercMaxY) mercMaxY = my;
        }

        ExpandExtent(0, 0);
        ExpandExtent(0, srcCols - 1);
        ExpandExtent(srcRows - 1, 0);
        ExpandExtent(srcRows - 1, srcCols - 1);

        double cellSizeX = (mercMaxX - mercMinX) / Math.Max(srcCols - 1, 1);
        double cellSizeY = (mercMaxY - mercMinY) / Math.Max(srcRows - 1, 1);

        // Pad extent by half a cell (match color renderer alignment)
        mercMinX -= cellSizeX / 2;
        mercMinY -= cellSizeY / 2;
        mercMaxX += cellSizeX / 2;
        mercMaxY += cellSizeY / 2;

        double mercWidth = mercMaxX - mercMinX;
        double mercHeight = mercMaxY - mercMinY;

        // Compute output bitmap dimensions: ensure each arrow gets MinArrowPixels,
        // while preserving the geographic aspect ratio.
        int arrowsX = (srcCols + stride - 1) / stride;
        int arrowsY = (srcRows + stride - 1) / stride;
        double aspect = mercWidth / mercHeight;

        int outCols, outRows;
        if (aspect >= 1.0)
        {
            outCols = Math.Min(MaxDim, arrowsX * MinArrowPixels);
            outRows = Math.Max(1, (int)(outCols / aspect));
        }
        else
        {
            outRows = Math.Min(MaxDim, arrowsY * MinArrowPixels);
            outCols = Math.Max(1, (int)(outRows * aspect));
        }

        double pxPerMercX = outCols / mercWidth;
        double pxPerMercY = outRows / mercHeight;

        // Draw symbols onto a transparent bitmap
        using var bmp = new SKBitmap(outCols, outRows, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);

        using var fillPaint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true };
        using var strokePaint = new SKPaint { Style = SKPaintStyle.Stroke, IsAntialias = true };

        for (int r = 0; r < srcRows; r += stride)
        for (int c = 0; c < srcCols; c += stride)
        {
            float value = valueData[r, c];
            bool isNoData = noDataIsNaN ? float.IsNaN(value) : value == noDataValue;
            if (isNoData) continue;

            float direction = rotationData[r, c];
            bool dirNoData = noDataIsNaN ? float.IsNaN(direction) : direction == noDataValue;
            if (dirNoData) continue;

            // Resolve symbol band
            var symbolBand = symbolScheme.Resolve(value);
            if (symbolBand is null) continue;

            // Load and cache the parsed SVG symbol
            var parsed = GetParsedSymbol(symbolBand.SymbolRef);
            if (parsed is null) continue;

            // Arrow size in pixels: proportional to stride × cell spacing.
            // Scale so the symbol's viewBox height maps to ~50% of the arrow spacing.
            double arrowSpacingPx = stride * cellSizeY * pxPerMercY;
            double baseScale = arrowSpacingPx * 0.5 / parsed.ViewBoxHeight;

            double bandScale = symbolBand.ScaleByValue
                ? symbolBand.ScaleFactor * value
                : symbolBand.ScaleFactor;
            float totalScale = (float)(baseScale * bandScale);

            // Project to pixel coordinates
            var (nativeX, nativeY) = georeferencer.ToNative(r, c);
            double lon, lat;
            if (nativeToWgs84.IsIdentity) { lon = nativeX; lat = nativeY; }
            else { (lon, lat) = nativeToWgs84.Transform(nativeX, nativeY); }
            var (mx, my) = SphericalMercator.FromLonLat(lon, lat);

            float px = (float)((mx - mercMinX) * pxPerMercX);
            float py = (float)(outRows - 1 - (my - mercMinY) * pxPerMercY);

            canvas.Save();
            canvas.Translate(px, py);
            canvas.RotateDegrees(direction);
            canvas.Scale(totalScale);

            // Draw each drawing command from the parsed SVG
            foreach (var cmd in parsed.Commands)
            {
                if (cmd.IsFill)
                {
                    fillPaint.Color = cmd.Color;
                    canvas.DrawPath(cmd.Path, fillPaint);
                }
                else
                {
                    strokePaint.Color = cmd.Color;
                    strokePaint.StrokeWidth = cmd.StrokeWidth;
                    strokePaint.StrokeJoin = SKStrokeJoin.Round;
                    strokePaint.StrokeCap = SKStrokeCap.Round;
                    canvas.DrawPath(cmd.Path, strokePaint);
                }
            }

            canvas.Restore();
        }

        // Encode to PNG and wrap as a georeferenced raster layer
        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var pngBytes = data.ToArray();

        var mercatorExtent = new MRect(mercMinX, mercMinY, mercMaxX, mercMaxY);
        var rasterFeature = new RasterFeature(new MRaster(pngBytes, mercatorExtent))
        {
            Styles = { new RasterStyle() },
        };

        return new MemoryLayer
        {
            Name = LayerName,
            Features = new List<RasterFeature> { rasterFeature },
            Style = null,
            Opacity = Opacity,
        };
    }

    /// <summary>
    /// Parses an S-100 SVG symbol into drawing commands (fill/stroke paths with colors).
    /// Resolves CSS classes (e.g. <c>fSCBN1</c>, <c>sCHBLK</c>) via the <see cref="Palette"/>.
    /// </summary>
    private ParsedSymbol? GetParsedSymbol(string symbolRef)
    {
        if (_symbolCache.TryGetValue(symbolRef, out var cached))
            return cached;

        ParsedSymbol? result = null;
        try
        {
            var svgContent = SymbolProvider(symbolRef);
            if (svgContent is not null)
                result = ParseSvgSymbol(svgContent);
        }
        catch
        {
            // Symbol not found or malformed
        }

        _symbolCache[symbolRef] = result;
        return result;
    }

    private ParsedSymbol ParseSvgSymbol(string svgContent)
        => ParseSvgSymbol(svgContent, Palette);

    /// <summary>
    /// Parses an S-100 Part 9-style SVG symbol into a list of fill/stroke
    /// draw commands with palette tokens resolved to RGB colours.
    /// Exposed <c>internal</c> for rendering-correctness tests that pin
    /// per-band colour wiring (S-111 Ed 2.0.0, content/S111/pc).
    /// </summary>
    internal static ParsedSymbol ParseSvgSymbol(string svgContent, ColorPalette? palette)
    {
        var doc = XDocument.Parse(svgContent);
        var svg = doc.Root!;

        // Parse viewBox for coordinate system: "minX minY width height"
        double vbHeight = 10.0; // default
        var viewBoxAttr = svg.Attribute("viewBox");
        if (viewBoxAttr is not null)
        {
            var parts = viewBoxAttr.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 4 && double.TryParse(parts[3], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var h))
                vbHeight = h;
        }

        var commands = new List<DrawCommand>();

        foreach (var pathEl in svg.Elements(SvgNs + "path"))
        {
            var dAttr = pathEl.Attribute("d");
            if (dAttr is null) continue;

            var classAttr = pathEl.Attribute("class")?.Value ?? "";
            var classes = classAttr.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            // Skip layout elements
            if (classes.Contains("layout")) continue;

            var skPath = SKPath.ParseSvgPathData(dAttr.Value);
            if (skPath is null) continue;

            // Determine if this is a fill or stroke path by inspecting CSS classes
            bool hasNoFill = classes.Contains("f0");
            string? fillToken = null;
            string? strokeToken = null;

            foreach (var cls in classes)
            {
                if (cls == "f0" || cls == "sl") continue;
                if (cls.Length > 1 && cls[0] == 'f' && char.IsUpper(cls[1]))
                    fillToken = cls[1..];
                else if (cls.Length > 1 && cls[0] == 's' && char.IsUpper(cls[1]))
                    strokeToken = cls[1..];
            }

            if (!hasNoFill && fillToken is not null)
            {
                // Fill path — resolve color token via palette
                var color = ResolveToken(fillToken, palette);
                commands.Add(new DrawCommand(skPath, true, color, 0));
            }

            if (strokeToken is not null)
            {
                // Stroke path
                var color = ResolveToken(strokeToken, palette);
                float sw = 0.32f;
                var swAttr = pathEl.Attribute("stroke-width");
                if (swAttr is not null &&
                    float.TryParse(swAttr.Value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                    sw = parsed;

                commands.Add(new DrawCommand(skPath, false, color, sw));
            }
        }

        return new ParsedSymbol(commands, vbHeight);
    }

    private SKColor ResolveToken(string token) => ResolveToken(token, Palette);

    private static SKColor ResolveToken(string token, ColorPalette? palette)
    {
        if (palette is not null && palette.TryResolve(token, out var hex))
        {
            var rgba = RgbaColor.FromHex(hex);
            return new SKColor(rgba.R, rgba.G, rgba.B, rgba.A);
        }

        // Common fallback: CHBLK = black
        if (token.Equals("CHBLK", StringComparison.OrdinalIgnoreCase))
            return SKColors.Black;

        return SKColors.Black;
    }

    internal sealed record DrawCommand(SKPath Path, bool IsFill, SKColor Color, float StrokeWidth);

    internal sealed record ParsedSymbol(IReadOnlyList<DrawCommand> Commands, double ViewBoxHeight);
}
