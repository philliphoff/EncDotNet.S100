using EncDotNet.S100.Rendering.Scene;
using SkiaSharp;

namespace EncDotNet.S100.Cli.Infrastructure.Tiles;

/// <summary>
/// Draws a WMS GetMap image from the XYZ tiles of an <see cref="ITileSource"/>:
/// the tiles of the nearest zoom level are fetched (from the source's caches,
/// or rendered), stitched, and resampled to the requested box and size, in
/// Web Mercator or in plate carrée (EPSG:4326, CRS:84).
/// </summary>
/// <remarks>
/// <para>
/// The zoom level is the one whose resolution is nearest the request's, so the
/// tiles are scaled by a factor between about 0.7 and 1.4 and symbols and text
/// keep close to their portrayed size. A request coarser than the minimum zoom
/// draws nothing, as the tiles below it are empty; one finer than the maximum
/// zoom enlarges the maximum zoom's tiles.
/// </para>
/// <para>
/// Web Mercator columns are linear in longitude, so a plate carrée image is
/// drawn row by row: each output row is the stitched row at its latitude.
/// </para>
/// </remarks>
internal static class WmsMapComposer
{
    /// <summary>The largest image edge served, in pixels.</summary>
    public const int MaxSize = 4096;

    private const double MaxLatitude = 85.0511287798066;

    /// <summary>
    /// The image of <paramref name="box"/> at <paramref name="width"/> ×
    /// <paramref name="height"/> pixels, drawn over <paramref name="background"/>
    /// (transparent when <see langword="null"/>), encoded as <paramref name="format"/>.
    /// </summary>
    public static async Task<byte[]> ComposeAsync(
        ITileSource source,
        WmsBox box,
        int width,
        int height,
        string? palette,
        DateTime? time,
        SKColor? background,
        TileImageFormat format,
        CancellationToken cancellationToken)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(background ?? SKColors.Transparent);
            await DrawAsync(canvas, source, box, width, height, palette, time, cancellationToken).ConfigureAwait(false);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(EncodeFormat(format), format == TileImageFormat.Png ? 100 : 90)
            ?? throw new NotSupportedException($"SkiaSharp could not encode a {format} image on this platform.");
        return data.ToArray();
    }

    /// <summary>
    /// The zoom level whose tiles are drawn for a request of
    /// <paramref name="metresPerPixel"/> Web Mercator metres per pixel, or
    /// <see langword="null"/> when it's coarser than <paramref name="minZoom"/>.
    /// </summary>
    internal static int? ZoomFor(double metresPerPixel, int tilePixels, int minZoom, int maxZoom)
    {
        double ideal = Math.Log2(WebMercator.Circumference / (tilePixels * metresPerPixel));
        int zoom = (int)Math.Round(ideal, MidpointRounding.AwayFromZero);
        if (zoom < minZoom)
            return null;
        return Math.Min(zoom, maxZoom);
    }

    private static async Task DrawAsync(
        SKCanvas canvas,
        ITileSource source,
        WmsBox box,
        int width,
        int height,
        string? palette,
        DateTime? time,
        CancellationToken cancellationToken)
    {
        var json = source.ToTileJson(palette, time);
        int minZoom = (int?)json["minzoom"] ?? 0;
        int maxZoom = (int?)json["maxzoom"] ?? minZoom;
        int tilePixels = (int?)json["tileSize"] ?? XyzTileGrid.TileSize;

        // The Web Mercator extent drawn; latitudes past the projection's limit
        // stay empty.
        double south = Math.Max(box.South, -MaxLatitude), north = Math.Min(box.North, MaxLatitude);
        if (south >= north)
            return;

        var (minX, _) = WebMercator.FromLonLat(box.West, 0);
        var (maxX, _) = WebMercator.FromLonLat(box.East, 0);
        if (box.IsMercator)
        {
            minX = box.MinX;
            maxX = box.MaxX;
        }

        double minY = box.IsMercator ? Math.Max(box.MinY, -XyzTileGrid.Extent) : WebMercator.FromLonLat(0, south).Y;
        double maxY = box.IsMercator ? Math.Min(box.MaxY, XyzTileGrid.Extent) : WebMercator.FromLonLat(0, north).Y;
        if (maxX <= minX || maxY <= minY)
            return;

        if (ZoomFor((maxX - minX) / width, tilePixels, minZoom, maxZoom) is not { } zoom)
            return;

        // The tiles under the extent; columns unwrapped past ±180°.
        double tileSize = XyzTileGrid.TileWorldSize(zoom);
        int n = 1 << zoom;
        long firstColumn = (long)Math.Floor((minX + XyzTileGrid.Extent) / tileSize);
        long lastColumn = (long)Math.Ceiling((maxX + XyzTileGrid.Extent) / tileSize) - 1;
        int firstRow = Math.Clamp((int)Math.Floor((XyzTileGrid.Extent - maxY) / tileSize), 0, n - 1);
        int lastRow = Math.Clamp((int)Math.Ceiling((XyzTileGrid.Extent - minY) / tileSize) - 1, 0, n - 1);
        int columns = (int)(lastColumn - firstColumn + 1), rows = lastRow - firstRow + 1;

        var reads = new List<(int Column, int Row, Task<byte[]?> Data)>(columns * rows);
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                int x = (int)(((firstColumn + column) % n + n) % n);
                reads.Add((column, row, source.ReadAsync(zoom, x, firstRow + row, palette, time, cancellationToken).AsTask()));
            }
        }

        await Task.WhenAll(reads.Select(r => r.Data)).ConfigureAwait(false);
        if (reads.All(r => r.Data.Result is null))
            return;

        // Stitch at the tiles' own resolution, then resample once, so tile
        // edges don't show as seams.
        using var mosaic = new SKBitmap(new SKImageInfo(columns * tilePixels, rows * tilePixels, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var stitch = new SKCanvas(mosaic))
        {
            stitch.Clear(SKColors.Transparent);
            foreach (var (column, row, read) in reads)
            {
                if (read.Result is not { } data)
                    continue;
                using var tile = SKBitmap.Decode(data);
                if (tile is null)
                    continue;
                stitch.DrawBitmap(tile, SKRect.Create(column * tilePixels, row * tilePixels, tilePixels, tilePixels));
            }
        }

        using var mosaicImage = SKImage.FromBitmap(mosaic);
        double originX = (firstColumn * tileSize) - XyzTileGrid.Extent;
        double originY = XyzTileGrid.Extent - (firstRow * tileSize);
        double pixel = tileSize / tilePixels;
        float SourceX(double mercatorX) => (float)((mercatorX - originX) / pixel);
        float SourceY(double mercatorY) => (float)((originY - mercatorY) / pixel);

        var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
        using var paint = new SKPaint();
        if (box.IsMercator)
        {
            // Rows past the projection's limit (outside ±85.05°) stay empty.
            float top = (float)((box.MaxY - maxY) / (box.MaxY - box.MinY) * height);
            float bottom = (float)((box.MaxY - minY) / (box.MaxY - box.MinY) * height);
            canvas.DrawImage(
                mosaicImage,
                new SKRect(SourceX(minX), SourceY(maxY), SourceX(maxX), SourceY(minY)),
                new SKRect(0, top, width, bottom),
                sampling,
                paint);
            return;
        }

        // Plate carrée: one output row at a time, each from its latitude's row.
        double degreesPerRow = (box.North - box.South) / height;
        for (int row = 0; row < height; row++)
        {
            double rowNorth = box.North - (row * degreesPerRow);
            double rowSouth = rowNorth - degreesPerRow;
            if (rowSouth >= MaxLatitude || rowNorth <= -MaxLatitude)
                continue;

            double sourceTop = SourceY(WebMercator.FromLonLat(0, Math.Min(rowNorth, MaxLatitude)).Y);
            double sourceBottom = SourceY(WebMercator.FromLonLat(0, Math.Max(rowSouth, -MaxLatitude)).Y);
            canvas.DrawImage(
                mosaicImage,
                new SKRect(SourceX(minX), (float)sourceTop, SourceX(maxX), (float)Math.Max(sourceBottom, sourceTop + 0.01)),
                new SKRect(0, row, width, row + 1),
                sampling,
                paint);
        }
    }

    private static SKEncodedImageFormat EncodeFormat(TileImageFormat format) => format switch
    {
        TileImageFormat.Jpeg => SKEncodedImageFormat.Jpeg,
        TileImageFormat.Webp => SKEncodedImageFormat.Webp,
        _ => SKEncodedImageFormat.Png,
    };
}

/// <summary>
/// The area of a WMS request: in Web Mercator metres (<see cref="IsMercator"/>)
/// or in WGS-84 degrees, whichever the request's CRS is, with the degrees of
/// either.
/// </summary>
internal readonly record struct WmsBox(bool IsMercator, double MinX, double MinY, double MaxX, double MaxY)
{
    /// <summary>A box in Web Mercator metres.</summary>
    public static WmsBox Mercator(double minX, double minY, double maxX, double maxY) => new(true, minX, minY, maxX, maxY);

    /// <summary>A box in degrees: longitudes west and east, latitudes south and north.</summary>
    public static WmsBox Degrees(double west, double south, double east, double north) => new(false, west, south, east, north);

    public double West => IsMercator ? WebMercator.ToLonLat(MinX, 0).Longitude : MinX;

    public double East => IsMercator ? WebMercator.ToLonLat(MaxX, 0).Longitude : MaxX;

    public double South => IsMercator ? WebMercator.ToLonLat(0, MinY).Latitude : MinY;

    public double North => IsMercator ? WebMercator.ToLonLat(0, MaxY).Latitude : MaxY;

    /// <summary>
    /// The WGS-84 position of the centre of pixel <paramref name="i"/>,
    /// <paramref name="j"/> (from the top left) of a
    /// <paramref name="width"/> × <paramref name="height"/> image of the box,
    /// and the ground size of a pixel there in metres.
    /// </summary>
    public (double Latitude, double Longitude, double PixelMetres) PixelPosition(int i, int j, int width, int height)
    {
        double fx = (i + 0.5) / width, fy = (j + 0.5) / height;
        double x = MinX + (fx * (MaxX - MinX));
        double y = MaxY - (fy * (MaxY - MinY));
        if (IsMercator)
        {
            var (longitude, latitude) = WebMercator.ToLonLat(x, y);
            return (latitude, longitude, (MaxX - MinX) / width * Math.Cos(latitude * Math.PI / 180));
        }

        double metresPerDegree = WebMercator.Circumference / 360;
        return (y, x, (MaxX - MinX) / width * metresPerDegree * Math.Cos(y * Math.PI / 180));
    }
}
