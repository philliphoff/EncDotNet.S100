using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Rendering.Scene;
using SkiaSharp;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Options for <see cref="HeadlessTileRenderer"/>.
/// </summary>
public sealed class HeadlessTileOptions
{
    /// <summary>
    /// Image pixels per logical tile pixel: 1 for 256-pixel tiles, 2 for
    /// 512-pixel high-DPI ("@2x") tiles of the same grid. Defaults to 1.
    /// </summary>
    public int PixelRatio { get; init; } = 1;

    /// <summary>
    /// The latitude, in degrees, at which each zoom level's display scale is
    /// measured for scale-visibility culling (see <see cref="HeadlessTileRenderer.ScaleDenominator"/>).
    /// Defaults to 0 (the equator).
    /// </summary>
    public double ReferenceLatitude { get; init; }

    /// <summary>The colour each tile is cleared to. Defaults to transparent.</summary>
    public RgbaColor Background { get; init; } = RgbaColor.Transparent;
}

/// <summary>
/// A block of adjacent tiles at one zoom level that is rendered as one image
/// (a metatile) and then cut into its tiles.
/// </summary>
/// <param name="Zoom">The zoom level.</param>
/// <param name="X">The westernmost tile column.</param>
/// <param name="Y">The northernmost tile row.</param>
/// <param name="Columns">The number of tile columns.</param>
/// <param name="Rows">The number of tile rows.</param>
public readonly record struct TileBlock(int Zoom, int X, int Y, int Columns, int Rows)
{
    /// <summary>The number of tiles in the block.</summary>
    public int TileCount => Columns * Rows;
}

/// <summary>
/// One rendered XYZ tile.
/// </summary>
public sealed class HeadlessTile : IDisposable
{
    internal HeadlessTile(int zoom, int x, int y, SKBitmap bitmap, bool isEmpty)
    {
        Zoom = zoom;
        X = x;
        Y = y;
        Bitmap = bitmap;
        IsEmpty = isEmpty;
    }

    /// <summary>The zoom level.</summary>
    public int Zoom { get; }

    /// <summary>The tile column.</summary>
    public int X { get; }

    /// <summary>The tile row.</summary>
    public int Y { get; }

    /// <summary>The tile image.</summary>
    public SKBitmap Bitmap { get; }

    /// <summary>Whether nothing was drawn on the tile: every pixel is the background.</summary>
    public bool IsEmpty { get; }

    /// <summary>Releases the tile image.</summary>
    public void Dispose() => Bitmap.Dispose();
}

/// <summary>
/// Renders a prepared <see cref="HeadlessCompositeScene"/> as XYZ Web-Mercator
/// raster tiles (see <see cref="XyzTileGrid"/>).
/// </summary>
/// <remarks>
/// <para>
/// Prepare the scene with <see cref="HeadlessCompositeOptions.HonorScaleVisibility"/>
/// on, so each zoom level shows only what is visible at its scale, and
/// <see cref="HeadlessCompositeOptions.EnableSeamWrap"/> off, because this
/// renderer draws the data on either side of the antimeridian itself.
/// </para>
/// <para>
/// Tiles are rendered in blocks (<see cref="TileBlock"/>) and cut apart. The
/// renderer maps world to pixels with one continuous transform and does not clip
/// geometry to the viewport, and it keeps point symbols and text whose anchor
/// lies up to <see cref="Renderers.Skia.Scene.SkiaDisplayListRenderer.PointCullMarginPx"/>
/// pixels outside it. So a feature crossing a tile edge draws identically on
/// both sides, whether the two tiles share a block or not.
/// </para>
/// <para>
/// Each zoom level is evaluated at one scale denominator, measured at
/// <see cref="HeadlessTileOptions.ReferenceLatitude"/>, rather than at each
/// tile's own latitude. Measuring per tile would switch SCAMIN at row
/// boundaries and cut features off along tile edges.
/// </para>
/// <para>
/// The renderer is immutable; <see cref="Render"/> may be called concurrently.
/// </para>
/// </remarks>
public sealed class HeadlessTileRenderer
{
    private static readonly IReadOnlyList<double> NoWorldCopies = [];
    private static readonly IReadOnlyList<double> AdjacentWorldCopies = [-360.0, 360.0];

    private readonly HeadlessCompositeScene _scene;
    private readonly HeadlessTileOptions _options;
    private readonly IReadOnlyList<double> _worldCopies;

    /// <summary>Creates a tile renderer over a prepared scene.</summary>
    /// <param name="scene">The prepared scene.</param>
    /// <param name="options">Tile options; defaults when <see langword="null"/>.</param>
    public HeadlessTileRenderer(HeadlessCompositeScene scene, HeadlessTileOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        options ??= new HeadlessTileOptions();
        if (options.PixelRatio is < 1 or > 4)
            throw new ArgumentOutOfRangeException(nameof(options), options.PixelRatio, "PixelRatio must be between 1 and 4.");

        _scene = scene;
        _options = options;

        // Data kept in a 0…360° frame, or straddling the antimeridian, reaches
        // past the canonical world; draw its copies one world east and west so
        // it lands in the wrapped tiles.
        _worldCopies = scene.TryGetWorldBounds(out double minX, out _, out double maxX, out _)
            && (minX < -XyzTileGrid.Extent || maxX > XyzTileGrid.Extent)
                ? AdjacentWorldCopies
                : NoWorldCopies;
    }

    /// <summary>The edge length of each tile image, in pixels.</summary>
    public int TilePixelSize => XyzTileGrid.TileSize * _options.PixelRatio;

    /// <summary>
    /// The scale denominator <paramref name="zoom"/> is rendered at:
    /// <see cref="XyzTileGrid.ScaleDenominator"/> at the reference latitude.
    /// </summary>
    /// <param name="zoom">The zoom level.</param>
    public double ScaleDenominator(int zoom) => XyzTileGrid.ScaleDenominator(zoom, _options.ReferenceLatitude);

    /// <summary>
    /// Splits the tiles of one zoom level into blocks of at most
    /// <paramref name="blockSize"/> × <paramref name="blockSize"/> tiles, aligned
    /// to multiples of <paramref name="blockSize"/> so neighbouring runs share
    /// block boundaries.
    /// </summary>
    /// <param name="zoom">The zoom level.</param>
    /// <param name="columns">The tile columns to cover, ascending (see <see cref="XyzTileGrid.Columns"/>).</param>
    /// <param name="firstRow">The first tile row to cover.</param>
    /// <param name="lastRow">The last tile row to cover.</param>
    /// <param name="blockSize">The block edge length, in tiles.</param>
    /// <returns>The blocks, row-major.</returns>
    public static IReadOnlyList<TileBlock> PlanBlocks(
        int zoom, IReadOnlyList<int> columns, int firstRow, int lastRow, int blockSize)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockSize);

        // Contiguous column runs; wrapped extents give two.
        var runs = new List<(int First, int Last)>();
        foreach (var column in columns)
        {
            if (runs.Count > 0 && runs[^1].Last == column - 1)
                runs[^1] = (runs[^1].First, column);
            else
                runs.Add((column, column));
        }

        var blocks = new List<TileBlock>();
        for (int rowStart = firstRow - Mod(firstRow, blockSize); rowStart <= lastRow; rowStart += blockSize)
        {
            int y = Math.Max(rowStart, firstRow);
            int rows = Math.Min(rowStart + blockSize - 1, lastRow) - y + 1;
            foreach (var (first, last) in runs)
            {
                for (int columnStart = first - Mod(first, blockSize); columnStart <= last; columnStart += blockSize)
                {
                    int x = Math.Max(columnStart, first);
                    int cols = Math.Min(columnStart + blockSize - 1, last) - x + 1;
                    blocks.Add(new TileBlock(zoom, x, y, cols, rows));
                }
            }
        }

        return blocks;

        static int Mod(int value, int divisor) => ((value % divisor) + divisor) % divisor;
    }

    /// <summary>
    /// Renders a block of tiles and cuts it into one image per tile.
    /// </summary>
    /// <param name="block">The block to render. It must lie inside the grid.</param>
    /// <returns>The tiles, row-major. The caller disposes them.</returns>
    public IReadOnlyList<HeadlessTile> Render(TileBlock block)
    {
        int n = XyzTileGrid.TilesPerAxis(block.Zoom);
        if (block.Columns <= 0 || block.Rows <= 0
            || block.X < 0 || block.Y < 0 || block.X + block.Columns > n || block.Y + block.Rows > n)
        {
            throw new ArgumentOutOfRangeException(nameof(block), block, "The block must lie inside the tile grid.");
        }

        var (minX, _, _, maxY) = XyzTileGrid.TileWorldBounds(block.Zoom, block.X, block.Y);
        var (_, minY, maxX, _) = XyzTileGrid.TileWorldBounds(
            block.Zoom, block.X + block.Columns - 1, block.Y + block.Rows - 1);

        // Lossless inverse, so the renderer reproduces these exact world bounds
        // (see WebMercator.ToLonLat).
        var (minLon, minLat) = WebMercator.ToLonLat(minX, minY, clampLatitude: false);
        var (maxLon, maxLat) = WebMercator.ToLonLat(maxX, maxY, clampLatitude: false);

        var viewport = new Viewport
        {
            MinLongitude = minLon,
            MaxLongitude = maxLon,
            MinLatitude = minLat,
            MaxLatitude = maxLat,
            WidthPixels = block.Columns * XyzTileGrid.TileSize,
            HeightPixels = block.Rows * XyzTileGrid.TileSize,
            ScaleDenominator = ScaleDenominator(block.Zoom),
        };

        int tilePx = TilePixelSize;
        using var bitmap = new SKBitmap(
            block.Columns * tilePx, block.Rows * tilePx, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(ToSkia(_options.Background));
            canvas.Scale(_options.PixelRatio);
            _scene.Draw(canvas, viewport, _worldCopies);
            canvas.Flush();
        }

        uint background = BackgroundPixel(_options.Background);
        var tiles = new List<HeadlessTile>(block.TileCount);
        for (int row = 0; row < block.Rows; row++)
        {
            for (int column = 0; column < block.Columns; column++)
            {
                var rect = SKRectI.Create(column * tilePx, row * tilePx, tilePx, tilePx);
                bool isEmpty = IsUniform(bitmap, rect, background);

                // The subset shares the block's pixels; copy them out so each
                // tile outlives the block bitmap.
                using var subset = new SKBitmap();
                if (!bitmap.ExtractSubset(subset, rect))
                    throw new InvalidOperationException("Could not cut a tile out of its block.");
                var owned = subset.Copy()
                    ?? throw new InvalidOperationException("Could not copy a tile out of its block.");
                tiles.Add(new HeadlessTile(block.Zoom, block.X + column, block.Y + row, owned, isEmpty));
            }
        }

        return tiles;
    }

    private static uint BackgroundPixel(RgbaColor color)
    {
        using var probe = new SKBitmap(1, 1, SKColorType.Rgba8888, SKAlphaType.Premul);
        probe.Erase(ToSkia(color));
        return BitConverter.ToUInt32(probe.GetPixelSpan());
    }

    private static SKColor ToSkia(RgbaColor color) => new(color.R, color.G, color.B, color.A);

    private static bool IsUniform(SKBitmap bitmap, SKRectI rect, uint pixel)
    {
        var pixels = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(bitmap.GetPixelSpan());
        int stride = bitmap.RowBytes / sizeof(uint);
        for (int y = rect.Top; y < rect.Bottom; y++)
        {
            var row = pixels.Slice(y * stride + rect.Left, rect.Width);
            if (row.IndexOfAnyExcept(pixel) >= 0)
                return false;
        }

        return true;
    }
}
