using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Portrayals;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Styles;

using PipelineViewport = EncDotNet.S100.Pipelines.Viewport;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Renders oriented symbols (e.g. current arrows) from a <see cref="StyledCoverageLayer"/>
/// as a Mapsui <see cref="ILayer"/> of <see cref="PointFeature"/>s with rotated SVG images.
/// </summary>
public sealed class MapsuiCoverageArrowRenderer
{
    private readonly ICrsTransformFactory _transformFactory;
    private readonly Dictionary<string, string?> _symbolDataUriCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Name assigned to the generated Mapsui layer.</summary>
    public string LayerName { get; set; } = "Coverage Arrows";

    /// <summary>Layer opacity (0.0–1.0). Defaults to 1.0.</summary>
    public double Opacity { get; set; } = 1.0;

    /// <summary>
    /// Base scale applied to all symbol sizes before band-specific scaling.
    /// Adjust to taste depending on map zoom.
    /// </summary>
    public double BaseSymbolScale { get; set; } = 0.3;

    /// <summary>
    /// Target maximum number of arrows along the longest grid axis.
    /// The renderer computes a stride so that no more than roughly
    /// <c>MaxArrowsPerAxis²</c> arrows are produced, preventing
    /// excessive feature counts on large regional grids.
    /// Set to 0 to disable subsampling.
    /// </summary>
    public int MaxArrowsPerAxis { get; set; } = 80;

    /// <summary>
    /// The color palette used to resolve SVG CSS fill tokens to inline colors.
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
    /// Renders the symbol scheme from a styled coverage layer into a Mapsui point layer.
    /// Returns <c>null</c> if the layer has no symbol scheme.
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

        var features = new List<PointFeature>();

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
            var band = symbolScheme.Resolve(value);
            if (band is null) continue;

            // Get processed SVG data URI (cached)
            var svgSource = GetSymbolSource(band.SymbolRef);
            if (svgSource is null) continue;

            // Project grid cell to Mercator
            var (nativeX, nativeY) = georeferencer.ToNative(r, c);
            double lon, lat;
            if (nativeToWgs84.IsIdentity)
            {
                lon = nativeX;
                lat = nativeY;
            }
            else
            {
                (lon, lat) = nativeToWgs84.Transform(nativeX, nativeY);
            }

            var (mx, my) = SphericalMercator.FromLonLat(lon, lat);

            // Compute scale
            double scale = band.ScaleByValue
                ? BaseSymbolScale * band.ScaleFactor * value
                : BaseSymbolScale * band.ScaleFactor;

            var feature = new PointFeature(mx, my);
            feature.Styles.Add(new ImageStyle
            {
                Image = new Image { Source = svgSource, RasterizeSvg = true },
                SymbolScale = scale,
                SymbolRotation = direction,
            });

            features.Add(feature);
        }

        return new MemoryLayer
        {
            Name = LayerName,
            Features = features,
            Style = null,
            Opacity = Opacity,
        };
    }

    private string? GetSymbolSource(string symbolRef)
    {
        if (_symbolDataUriCache.TryGetValue(symbolRef, out var cached))
            return cached;

        string? source = null;
        try
        {
            var svgContent = SymbolProvider(symbolRef);
            if (svgContent is not null)
            {
                var processed = SvgProcessor.Process(svgContent, Palette);
                source = "svg-content://" + processed;
            }
        }
        catch
        {
            // Symbol not found or malformed
        }

        _symbolDataUriCache[symbolRef] = source;
        return source;
    }
}
