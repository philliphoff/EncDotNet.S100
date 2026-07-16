using System.Runtime.InteropServices;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Styles;
using SkiaSharp;
using PipelineViewport = EncDotNet.S100.Pipelines.Viewport;
using S100Diag = EncDotNet.S100.Renderers.Mapsui.Diagnostics;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Renders a <see cref="StyledCoverageLayer"/> to a Mapsui <see cref="ILayer"/>
/// by reprojecting each grid node to EPSG:3857 (Web Mercator) and producing a
/// georeferenced raster overlay.
/// </summary>
public sealed class MapsuiCoverageRenderer : ICoverageRenderer<ILayer>
{
    private const string TargetCrs = "EPSG:3857";
    private const int MaxDim = 4096;

    private readonly ICrsTransformFactory _transformFactory;

    /// <summary>
    /// Cached palette- and value-independent projection layout. The grid-node
    /// reprojection (native CRS → Web Mercator) and the resulting node→pixel
    /// mapping depend only on the grid geometry, so they can be reused across
    /// renders that change only the colour palette, ECDIS display mode, or
    /// coverage time-step. Published atomically via <see cref="Volatile"/>.
    /// </summary>
    private LayoutCacheEntry? _layoutCache;

    /// <summary>Number of times the projection layout was reused from cache.</summary>
    internal long LayoutCacheHits { get; private set; }

    /// <summary>Number of times the projection layout was (re)built.</summary>
    internal long LayoutCacheMisses { get; private set; }

    private readonly object _cacheLock = new();

    /// <summary>Name assigned to the generated Mapsui layer.</summary>
    public string LayerName { get; set; } = "S-102 Coverage";

    /// <summary>Layer opacity (0.0–1.0). Defaults to 0.8.</summary>
    public double Opacity { get; set; } = 0.8;

    public MapsuiCoverageRenderer(ICrsTransformFactory transformFactory)
    {
        _transformFactory = transformFactory;
    }

    public ILayer Render(StyledCoverageLayer layer, PipelineViewport viewport)
    {
        using var __activity = S100Diag.Telemetry.ActivitySource.StartActivity("s100.render.coverage.build");
        __activity?.SetTag("s100.render.target", "mapsui");
        return RenderUnsafe(layer, viewport);
    }

    private unsafe ILayer RenderUnsafe(StyledCoverageLayer layer, PipelineViewport viewport)
    {
        var sampled = layer.Coverage;
        var georeferencer = layer.Georeferencer;
        var colorScheme = layer.ColorScheme
            ?? throw new InvalidOperationException(
                "MapsuiCoverageRenderer requires a non-null ColorScheme. " +
                "Processors must skip this renderer when the portrayal " +
                "catalogue does not define a coverage colour fill " +
                "(e.g. S-111 Edition 2.0.0 arrow-only catalogue).");
        var fieldData = sampled.GetField(colorScheme.FieldName);
        int srcRows = fieldData.GetLength(0);
        int srcCols = fieldData.GetLength(1);

        // Pre-resolve color bands
        var bands = new (float Min, float Max, SKColor Color)[colorScheme.Bands.Count];
        for (int i = 0; i < colorScheme.Bands.Count; i++)
        {
            var band = colorScheme.Bands[i];
            var rgba = RgbaColor.FromHex(band.Color);
            bands[i] = (band.MinValue, band.MaxValue, new SKColor(rgba.R, rgba.G, rgba.B, rgba.A));
        }

        float noDataValue = layer.NoDataValue;
        bool noDataIsNaN = float.IsNaN(noDataValue);

        // Optional NODTA fill colour for cells whose value equals the
        // coverage's no-data sentinel. When the colour scheme leaves
        // NoDataColor null we preserve legacy behaviour (transparent).
        uint noDataPacked = 0;
        bool noDataIsFilled = false;
        if (!string.IsNullOrEmpty(colorScheme.NoDataColor))
        {
            var rgba = RgbaColor.FromHex(colorScheme.NoDataColor);
            noDataPacked = PackRgba(new SKColor(rgba.R, rgba.G, rgba.B, rgba.A));
            noDataIsFilled = noDataPacked != 0;
        }

        // Resolve the palette- and value-independent projection layout. The
        // node→pixel mapping is reused when the grid geometry is unchanged
        // (e.g. across palette switches or coverage time-step changes),
        // skipping the per-node CRS transform + Mercator projection pass.
        var layout = GetOrBuildLayout(georeferencer, srcRows, srcCols);

        int outCols = layout.OutCols;
        int outRows = layout.OutRows;
        int[] nodeToPixel = layout.NodeToPixel;

        // Render: for each grid node, place its color at the precomputed
        // Mercator pixel. Fill a uint[] (RGBA8888) buffer directly to avoid
        // the per-pixel managed→native round-trip that SKBitmap.SetPixel
        // performs.
        int pixelCount = outCols * outRows;
        var pixels = new uint[pixelCount];

        // Pre-resolve band colors to packed RGBA8888 (matches SKColorType.Rgba8888).
        var bandPacked = new uint[bands.Length];
        for (int i = 0; i < bands.Length; i++)
            bandPacked[i] = PackRgba(bands[i].Color);

        for (int r = 0; r < srcRows; r++)
            for (int c = 0; c < srcCols; c++)
            {
                float value = fieldData[r, c];
                bool isNoData = noDataIsNaN ? float.IsNaN(value) : value == noDataValue;

                uint packed;
                if (isNoData)
                {
                    if (!noDataIsFilled) continue;
                    packed = noDataPacked;
                }
                else
                {
                    packed = 0;
                    for (int b = 0; b < bands.Length; b++)
                    {
                        if (value >= bands[b].Min && value < bands[b].Max)
                        {
                            packed = bandPacked[b];
                            break;
                        }
                    }
                    if (packed == 0) continue;
                }

                int target = nodeToPixel[r * srcCols + c];
                if (target >= 0)
                    pixels[target] = packed;
            }

        using var bmp = new SKBitmap(new SKImageInfo(outCols, outRows, SKColorType.Rgba8888, SKAlphaType.Premul));
        // Bulk-copy the pixel buffer into the bitmap's native backing store.
        var pixelSpan = MemoryMarshal.AsBytes(pixels.AsSpan());
        pixelSpan.CopyTo(new Span<byte>((void*)bmp.GetPixels(), pixelCount * 4));

        // Encode to PNG
        using var image = SKImage.FromBitmap(bmp);
        using var data0 = image.Encode(SKEncodedImageFormat.Png, 100);
        byte[] pngBytes = data0.ToArray();

        var mercatorExtent = new MRect(layout.MercMinX, layout.MercMinY, layout.MercMaxX, layout.MercMaxY);

        // Build the Mapsui layer
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
    /// Returns the cached projection layout when the grid geometry matches the
    /// last render, otherwise (re)builds it. The cache key is value-based: it
    /// compares the native CRS, grid dimensions, and the affine georeferencing
    /// parameters (origin and spacing), all of which fully determine the
    /// node→pixel mapping.
    /// </summary>
    private LayoutCacheEntry GetOrBuildLayout(GridGeoreferencer georeferencer, int srcRows, int srcCols)
    {
        var meta = georeferencer.Metadata;
        var cached = Volatile.Read(ref _layoutCache);
        if (cached is not null && cached.Matches(georeferencer.CRS, srcRows, srcCols, meta))
        {
            lock (_cacheLock) { LayoutCacheHits++; }
            return cached;
        }

        var built = BuildLayout(georeferencer, srcRows, srcCols, meta);

        // Build immutable entry locally, then publish atomically.
        Volatile.Write(ref _layoutCache, built);
        lock (_cacheLock) { LayoutCacheMisses++; }
        return built;
    }

    /// <summary>
    /// Projects every grid node to Web Mercator and computes the output raster
    /// dimensions and the per-node destination pixel index. This is the
    /// palette- and value-independent portion of a render.
    /// </summary>
    private LayoutCacheEntry BuildLayout(GridGeoreferencer georeferencer, int srcRows, int srcCols, GridMetadata meta)
    {
        // Build CRS transform: native grid CRS → WGS84
        var nativeToWgs84 = _transformFactory.Create(georeferencer.CRS, "EPSG:4326");

        // First pass: project every grid node to Mercator and find bounding box
        var nodePositions = new (double MercX, double MercY)[srcRows, srcCols];
        double mercMinX = double.MaxValue, mercMinY = double.MaxValue;
        double mercMaxX = double.MinValue, mercMaxY = double.MinValue;

        for (int r = 0; r < srcRows; r++)
            for (int c = 0; c < srcCols; c++)
            {
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
                nodePositions[r, c] = (mx, my);

                if (mx < mercMinX) mercMinX = mx;
                if (my < mercMinY) mercMinY = my;
                if (mx > mercMaxX) mercMaxX = mx;
                if (my > mercMaxY) mercMaxY = my;
            }

        // Determine output bitmap dimensions using separate X/Y cell sizes
        // to account for non-conformal projections (e.g. WGS84 → Mercator distortion).
        double cellSizeX = (mercMaxX - mercMinX) / Math.Max(srcCols - 1, 1);
        double cellSizeY = (mercMaxY - mercMinY) / Math.Max(srcRows - 1, 1);

        // Pad extent by half a cell
        mercMinX -= cellSizeX / 2;
        mercMinY -= cellSizeY / 2;
        mercMaxX += cellSizeX / 2;
        mercMaxY += cellSizeY / 2;

        int outCols = (int)Math.Ceiling((mercMaxX - mercMinX) / cellSizeX);
        int outRows = (int)Math.Ceiling((mercMaxY - mercMinY) / cellSizeY);

        // Cap output size
        if (outCols > MaxDim || outRows > MaxDim)
        {
            double scale = (double)MaxDim / Math.Max(outCols, outRows);
            outCols = (int)(outCols * scale);
            outRows = (int)(outRows * scale);
            cellSizeX = (mercMaxX - mercMinX) / outCols;
            cellSizeY = (mercMaxY - mercMinY) / outRows;
        }

        // Compute the destination pixel for each grid node using the final
        // (post-MaxDim) cell sizes. Out-of-bounds nodes map to -1.
        var nodeToPixel = new int[srcRows * srcCols];
        for (int r = 0; r < srcRows; r++)
            for (int c = 0; c < srcCols; c++)
            {
                var (mx, my) = nodePositions[r, c];
                int px = (int)((mx - mercMinX) / cellSizeX);
                int py = outRows - 1 - (int)((my - mercMinY) / cellSizeY);
                nodeToPixel[r * srcCols + c] =
                    (px >= 0 && px < outCols && py >= 0 && py < outRows)
                        ? py * outCols + px
                        : -1;
            }

        return new LayoutCacheEntry(
            georeferencer.CRS, srcRows, srcCols,
            meta.OriginLongitude, meta.OriginLatitude,
            meta.SpacingLongitudinal, meta.SpacingLatitudinal,
            outCols, outRows, nodeToPixel,
            mercMinX, mercMinY, mercMaxX, mercMaxY);
    }

    /// <summary>
    /// Stable, palette-independent projection layout for one grid geometry.
    /// </summary>
    private sealed class LayoutCacheEntry
    {
        public LayoutCacheEntry(
            string crs, int srcRows, int srcCols,
            double originLon, double originLat,
            double spacingLon, double spacingLat,
            int outCols, int outRows, int[] nodeToPixel,
            double mercMinX, double mercMinY, double mercMaxX, double mercMaxY)
        {
            _crs = crs;
            _srcRows = srcRows;
            _srcCols = srcCols;
            _originLon = originLon;
            _originLat = originLat;
            _spacingLon = spacingLon;
            _spacingLat = spacingLat;
            OutCols = outCols;
            OutRows = outRows;
            NodeToPixel = nodeToPixel;
            MercMinX = mercMinX;
            MercMinY = mercMinY;
            MercMaxX = mercMaxX;
            MercMaxY = mercMaxY;
        }

        private readonly string _crs;
        private readonly int _srcRows;
        private readonly int _srcCols;
        private readonly double _originLon;
        private readonly double _originLat;
        private readonly double _spacingLon;
        private readonly double _spacingLat;

        public int OutCols { get; }
        public int OutRows { get; }
        public int[] NodeToPixel { get; }
        public double MercMinX { get; }
        public double MercMinY { get; }
        public double MercMaxX { get; }
        public double MercMaxY { get; }

        public bool Matches(string crs, int srcRows, int srcCols, GridMetadata meta) =>
            _srcRows == srcRows &&
            _srcCols == srcCols &&
            _crs == crs &&
            _originLon == meta.OriginLongitude &&
            _originLat == meta.OriginLatitude &&
            _spacingLon == meta.SpacingLongitudinal &&
            _spacingLat == meta.SpacingLatitudinal;
    }

    /// <summary>
    /// Pack an <see cref="SKColor"/> into a 32-bit little-endian RGBA8888
    /// pixel (matching <see cref="SKColorType.Rgba8888"/>). Premultiplied
    /// alpha is applied so the result is suitable for a Premul bitmap.
    /// </summary>
    private static uint PackRgba(SKColor c)
    {
        byte a = c.Alpha;
        byte r = a == 255 ? c.Red : (byte)((c.Red * a + 127) / 255);
        byte g = a == 255 ? c.Green : (byte)((c.Green * a + 127) / 255);
        byte b = a == 255 ? c.Blue : (byte)((c.Blue * a + 127) / 255);
        // Memory order on little-endian: R G B A → uint = A<<24 | B<<16 | G<<8 | R
        return ((uint)a << 24) | ((uint)b << 16) | ((uint)g << 8) | r;
    }
}
