using System.Runtime.InteropServices;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Renderers.Skia;
using EncDotNet.S100.Rendering.Scene;
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
    /// Cache of the palette- and value-independent projection layout. The
    /// grid-node reprojection (native CRS → Web Mercator) and the resulting
    /// node→pixel mapping depend only on the grid geometry, so they can be
    /// reused across renders that change only the colour palette, ECDIS display
    /// mode, or coverage time-step. Defaults to a cache owned by this renderer;
    /// a dataset session supplies its own so the layout outlives the renderer.
    /// </summary>
    internal CoverageLayoutCache LayoutCache { get; init; } = new();

    /// <summary>Number of times the projection layout was reused from cache.</summary>
    internal long LayoutCacheHits => LayoutCache.Hits;

    /// <summary>Number of times the projection layout was (re)built.</summary>
    internal long LayoutCacheMisses => LayoutCache.Misses;

    /// <summary>Name assigned to the generated Mapsui layer.</summary>
    public string LayerName { get; set; } = "S-102 Coverage";

    /// <summary>
    /// The portrayal opacity of the layer (0.0–1.0). Defaults to 0.8. A map
    /// session multiplies it with the user's dataset opacity rather than
    /// replacing it.
    /// </summary>
    public double Opacity { get; set; } = 0.8;

    /// <summary>
    /// Optional S-101 <c>LandArea</c> surface geometries (WGS84
    /// <c>(latitude, longitude)</c>) used to clip the surface to water. When set,
    /// the surface raster is up-sampled and clipped with these polygons
    /// (even–odd, honouring interior water rings) at output-pixel resolution, so
    /// it never bleeds over land regardless of how coarse the grid is (a real
    /// S-104 grid can be a handful of ~1&#160;km cells). Set by the S-98
    /// water-area clip rule for the S-104 gridded surface (issue #483).
    /// <see langword="null"/> or empty disables clipping.
    /// </summary>
    public IReadOnlyList<FeatureGeometry>? LandAreas { get; set; }

    /// <summary>
    /// Creates a coverage renderer that reprojects grids to Web Mercator.
    /// </summary>
    /// <param name="transformFactory">
    /// Creates the transform from a coverage's native CRS to WGS84, which is
    /// then projected to Web Mercator.
    /// </param>
    public MapsuiCoverageRenderer(ICrsTransformFactory transformFactory)
    {
        _transformFactory = transformFactory;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Returns a <see cref="MemoryLayer"/> holding a single georeferenced
    /// raster, named <see cref="LayerName"/> and drawn at <see cref="Opacity"/>.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The layer has no <see cref="StyledCoverageLayer.ColorScheme"/>.
    /// </exception>
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
        AxisCells? cells = layout.Cells;

        // Render: for each grid node, fill its precomputed Mercator pixel cell.
        // Fill a uint[] (RGBA8888) buffer directly to avoid the per-pixel
        // managed→native round-trip that SKBitmap.SetPixel performs.
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
                if (target < 0) continue;
                if (cells is null)
                {
                    pixels[target] = packed;
                    continue;
                }

                int start = cells.RowY0[r] * outCols + cells.ColX0[c];
                int width = cells.ColWidth[c], height = cells.RowHeight[r];
                if ((width | height) == 1)
                {
                    pixels[start] = packed;
                    continue;
                }

                for (int y = 0; y < height; y++)
                    pixels.AsSpan(start + y * outCols, width).Fill(packed);
            }

        using var bmp = new SKBitmap(new SKImageInfo(outCols, outRows, SKColorType.Rgba8888, SKAlphaType.Premul));
        // Bulk-copy the pixel buffer into the bitmap's native backing store.
        var pixelSpan = MemoryMarshal.AsBytes(pixels.AsSpan());
        pixelSpan.CopyTo(new Span<byte>((void*)bmp.GetPixels(), pixelCount * 4));

        var mercatorExtent = new MRect(layout.MercMinX, layout.MercMinY, layout.MercMaxX, layout.MercMaxY);

        // Encode to PNG. When land polygons are supplied (S-98 water-area clip,
        // issue #483), up-sample the surface and clip it to water at output-pixel
        // resolution so the (often very coarse) grid does not paint over land.
        byte[] pngBytes = LandAreas is { Count: > 0 } land
            ? EncodeClippedToWater(bmp, land, outCols, outRows, layout)
            : EncodePng(bmp);

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

    // Web Mercator with the latitude clamped to the projection limit. A global
    // grid has rows at ±90°, where the northing diverges (−∞ at the south
    // pole), which would make the raster extent infinite and its height zero.
    private static (double X, double Y) ToMercator(double lon, double lat) =>
        SphericalMercator.FromLonLat(lon, Math.Clamp(lat, -WebMercator.MaxLatitude, WebMercator.MaxLatitude));

    private static byte[] EncodePng(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>
    /// Up-samples the coarse node raster and clips it to water using the S-101
    /// land polygons, projected into the same Web-Mercator extent so the clip
    /// registers with the surface. Even–odd fill keeps the surface over interior
    /// water rings; the land region is removed with
    /// <see cref="SKClipOperation.Difference"/>. Antialiased so the coastline is
    /// smooth even though the underlying grid is blocky.
    /// </summary>
    private static byte[] EncodeClippedToWater(
        SKBitmap source,
        IReadOnlyList<FeatureGeometry> landAreas,
        int outCols,
        int outRows,
        LayoutCacheEntry layout)
    {
        // Up-sample so the clip has enough resolution to follow the coastline
        // (the node raster can be only a handful of cells across). Never shrink
        // below native and never exceed the output-size cap.
        int longSide = Math.Max(outCols, outRows);
        int targetLong = longSide >= 1024 ? Math.Min(longSide, MaxDim) : Math.Min(1024, MaxDim);
        double factor = (double)targetLong / longSide;
        int tCols = Math.Max(1, (int)Math.Round(outCols * factor));
        int tRows = Math.Max(1, (int)Math.Round(outRows * factor));

        double spanX = layout.MercMaxX - layout.MercMinX;
        double spanY = layout.MercMaxY - layout.MercMinY;

        using var target = new SKBitmap(new SKImageInfo(tCols, tRows, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(target))
        {
            canvas.Clear(SKColors.Transparent);

            using var landPath = CoverageLandClip.BuildLandPath(
                landAreas,
                (lon, lat) =>
                {
                    var (mx, my) = ToMercator(lon, lat);
                    float px = (float)((mx - layout.MercMinX) / spanX * tCols);
                    // Row 0 of the source raster is north (top); flip Y so the
                    // clip matches the raster's north-up orientation.
                    float py = (float)((1.0 - (my - layout.MercMinY) / spanY) * tRows);
                    return new SKPoint(px, py);
                });

            if (landPath is not null)
            {
                canvas.Save();
                canvas.ClipPath(landPath, SKClipOperation.Difference, antialias: true);
            }

            using (var image = SKImage.FromBitmap(source))
            {
                canvas.DrawImage(
                    image,
                    new SKRect(0, 0, tCols, tRows),
                    new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
            }

            if (landPath is not null)
            {
                canvas.Restore();
            }
        }

        return EncodePng(target);
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
        return LayoutCache.GetOrBuild(
            georeferencer.CRS,
            srcRows,
            srcCols,
            meta,
            () => BuildLayout(georeferencer, srcRows, srcCols, meta));
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

                var (mx, my) = ToMercator(lon, lat);
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

        // A longitude/latitude grid's Mercator x depends only on the column
        // and y only on the row, so each column and row gets a pixel span.
        var cells = nativeToWgs84.IsIdentity
            ? AxisCells.Build(nodePositions, outCols, outRows, cellSizeX, cellSizeY, mercMinX, mercMinY)
            : null;

        return new LayoutCacheEntry(
            georeferencer.CRS, srcRows, srcCols,
            meta.OriginLongitude, meta.OriginLatitude,
            meta.SpacingLongitudinal, meta.SpacingLatitudinal,
            outCols, outRows, nodeToPixel, cells,
            mercMinX, mercMinY, mercMaxX, mercMaxY);
    }

    /// <summary>
    /// The pixel cell each node of a separable (longitude/latitude) grid
    /// fills: the pixels whose centres lie between the midpoints to its
    /// neighbours, as a span per column and per row. One pixel per node would
    /// leave gaps wherever Mercator spaces columns or rows wider than the
    /// average cell, as it does towards the poles on a global grid. Each cell
    /// always holds the node's own pixel.
    /// </summary>
    internal sealed class AxisCells
    {
        private AxisCells(short[] colX0, short[] colWidth, short[] rowY0, short[] rowHeight)
        {
            ColX0 = colX0;
            ColWidth = colWidth;
            RowY0 = rowY0;
            RowHeight = rowHeight;
        }

        /// <summary>Per column, the first pixel column of its cells.</summary>
        public short[] ColX0 { get; }

        /// <summary>Per column, the width of its cells in pixels.</summary>
        public short[] ColWidth { get; }

        /// <summary>Per row, the first pixel row (top-down) of its cells.</summary>
        public short[] RowY0 { get; }

        /// <summary>Per row, the height of its cells in pixels.</summary>
        public short[] RowHeight { get; }

        /// <summary>
        /// Builds the spans, or returns <see langword="null"/> when every cell
        /// is a single pixel.
        /// </summary>
        public static AxisCells? Build(
            (double MercX, double MercY)[,] nodePositions,
            int outCols, int outRows, double cellSizeX, double cellSizeY, double mercMinX, double mercMinY)
        {
            int srcRows = nodePositions.GetLength(0), srcCols = nodePositions.GetLength(1);
            bool anyLarge = false;

            var colX0 = new short[srcCols];
            var colWidth = new short[srcCols];
            for (int c = 0; c < srcCols; c++)
            {
                double at = (nodePositions[0, c].MercX - mercMinX) / cellSizeX;
                var (left, right) = CellSpan(
                    at,
                    c > 0 ? (nodePositions[0, c - 1].MercX - mercMinX) / cellSizeX : null,
                    c < srcCols - 1 ? (nodePositions[0, c + 1].MercX - mercMinX) / cellSizeX : null);
                int px = Math.Clamp((int)at, 0, outCols - 1);
                int x0 = Math.Clamp(Math.Min((int)Math.Ceiling(left - 0.5), px), 0, outCols - 1);
                int x1 = Math.Clamp(Math.Max((int)Math.Ceiling(right - 0.5), px + 1), x0 + 1, outCols);
                colX0[c] = (short)x0;
                colWidth[c] = (short)(x1 - x0);
                anyLarge |= x1 - x0 > 1;
            }

            var rowY0 = new short[srcRows];
            var rowHeight = new short[srcRows];
            for (int r = 0; r < srcRows; r++)
            {
                // Bottom-up pixel rows whose centres fall in [bottom, top).
                double at = (nodePositions[r, 0].MercY - mercMinY) / cellSizeY;
                var (bottom, top) = CellSpan(
                    at,
                    r > 0 ? (nodePositions[r - 1, 0].MercY - mercMinY) / cellSizeY : null,
                    r < srcRows - 1 ? (nodePositions[r + 1, 0].MercY - mercMinY) / cellSizeY : null);
                int py = Math.Clamp(outRows - 1 - (int)at, 0, outRows - 1);
                int y0 = Math.Clamp(Math.Min(outRows - (int)Math.Ceiling(top - 0.5), py), 0, outRows - 1);
                int y1 = Math.Clamp(Math.Max(outRows - (int)Math.Ceiling(bottom - 0.5), py + 1), y0 + 1, outRows);
                rowY0[r] = (short)y0;
                rowHeight[r] = (short)(y1 - y0);
                anyLarge |= y1 - y0 > 1;
            }

            return anyLarge ? new AxisCells(colX0, colWidth, rowY0, rowHeight) : null;
        }
    }

    // The extent of a node's cell along one axis, in pixel units: halfway to
    // each neighbour, mirroring the other side at the grid edge, or half a
    // pixel either way for a lone node.
    private static (double Low, double High) CellSpan(double at, double? before, double? after)
    {
        double low = before is { } b ? (b + at) / 2 : after is { } a1 ? at - (a1 - at) / 2 : at - 0.5;
        double high = after is { } a ? (a + at) / 2 : before is { } b1 ? at + (at - b1) / 2 : at + 0.5;
        return low <= high ? (low, high) : (high, low);
    }

    /// <summary>
    /// Stable, palette-independent projection layout for one grid geometry.
    /// </summary>
    internal sealed class LayoutCacheEntry
    {
        public LayoutCacheEntry(
            string crs, int srcRows, int srcCols,
            double originLon, double originLat,
            double spacingLon, double spacingLat,
            int outCols, int outRows, int[] nodeToPixel, AxisCells? cells,
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
            Cells = cells;
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
        /// <summary>
        /// Per node, the pixel it falls in (rows top-down), or -1 when the node
        /// is outside the raster.
        /// </summary>
        public int[] NodeToPixel { get; }

        /// <summary>
        /// The pixel cells the nodes fill, or <see langword="null"/> when each
        /// node fills only its own pixel.
        /// </summary>
        public AxisCells? Cells { get; }
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
