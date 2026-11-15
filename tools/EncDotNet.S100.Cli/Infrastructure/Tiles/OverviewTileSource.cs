using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using EncDotNet.S100.Rendering.Scene;
using SkiaSharp;

namespace EncDotNet.S100.Cli.Infrastructure.Tiles;

/// <summary>
/// Serves an overview below the datasets' minimum zoom (issue #1503,
/// <c>tiles serve --overview</c>): each dataset's coverage outline and name,
/// where the chart tiles would answer <c>204 No Content</c>, so a GIS client
/// zoomed out still sees where the charts are.
/// </summary>
/// <remarks>
/// <para>
/// Wraps the source rendered from the datasets. At and above the chart's
/// minimum zoom every request goes to it unchanged. Below it, a tile (or a
/// WMS view) is drawn from the <c>/datasets</c> document
/// (<see cref="ITileSource.ToDatasetsJson"/>): the declared data coverage, or
/// the bounds where a dataset declares none. A dataset too small to see at a
/// zoom is drawn as a small square. Names are placed once per zoom level over
/// the whole world, the first dataset first, and left out where they would
/// overlap one already placed, so a name crossing a tile edge is drawn the
/// same on both tiles. A tile with no coverage on it still answers 204.
/// </para>
/// <para>
/// The TileJSON <c>minzoom</c> becomes 0, and <c>s100.overview</c> gives the
/// overview's zoom range and <c>s100.chartMinzoom</c> the chart's. WMS and
/// WMTS read the minimum zoom from there, so the WMS layer's
/// <c>MaxScaleDenominator</c> and the WMTS <c>TileMatrixSetLimits</c> widen
/// with it. The overview is the same in every time step and for every mariner
/// settings set of a palette; it follows a reload, keyed by
/// <see cref="ITileSource.Revision"/>.
/// </para>
/// </remarks>
internal sealed class OverviewTileSource : ITileSource, IAsyncDisposable
{
    /// <summary>The bytes of overview tiles kept in memory.</summary>
    private const long CacheBytes = 16L * 1024 * 1024;

    private readonly ITileSource _inner;
    private readonly TileCache _cache = new(CacheBytes);
    private OverviewData? _data;

    /// <param name="inner">The tiles rendered from datasets. The overview disposes it.</param>
    /// <exception cref="ArgumentException"><paramref name="inner"/> has no datasets behind it.</exception>
    public OverviewTileSource(ITileSource inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        if (!inner.HasDatasets)
            throw new ArgumentException("An overview needs datasets behind the tiles.", nameof(inner));
        _inner = inner;
    }

    /// <summary>The source the chart tiles come from.</summary>
    public ITileSource Inner => _inner;

    /// <summary>The chart's minimum zoom: the overview is drawn below it.</summary>
    public int ChartMinZoom => Current().ChartMinZoom;

    public string Path => _inner.Path;

    public TileImageFormat Format => _inner.Format;

    public IReadOnlyList<string> Palettes => _inner.Palettes;

    public IReadOnlyList<DateTime> Times => _inner.Times;

    public DatasetFeatureQuery? FeatureQuery => _inner.FeatureQuery;

    public MarinerStyleRegistry? MarinerStyles => _inner.MarinerStyles;

    public bool HasDatasets => _inner.HasDatasets;

    public JsonObject? ToDatasetsJson() => _inner.ToDatasetsJson();

    public string? Revision => _inner.Revision;

    public bool CanRenderViews => _inner.CanRenderViews;

    public async ValueTask<byte[]?> ReadAsync(int zoom, int x, int y, string? palette, DateTime? time, CancellationToken cancellationToken)
    {
        var data = Current();
        if (zoom >= data.ChartMinZoom)
            return await _inner.ReadAsync(zoom, x, y, palette, time, cancellationToken).ConfigureAwait(false);
        if (!TileSource.IsValid(zoom, x, y) || ResolvePalette(palette, data) is not { } name)
            return null;

        var key = new TileKey(name, zoom, x, y, Variant: "overview-" + data.Revision);
        if (!_cache.TryGet(key, out var tile))
        {
            tile = RenderTile(data, name, zoom, x, y);
            _cache.Add(key, tile);
        }

        return tile.Length == 0 ? null : tile;
    }

    public ValueTask<SKBitmap?> RenderViewAsync(
        int zoom, double minX, double minY, double maxX, double maxY, int width, int height,
        string? palette, DateTime? time, CancellationToken cancellationToken)
    {
        var data = Current();
        if (zoom >= data.ChartMinZoom)
            return _inner.RenderViewAsync(zoom, minX, minY, maxX, maxY, width, height, palette, time, cancellationToken);
        if (!_inner.CanRenderViews || ResolvePalette(palette, data) is not { } name || width <= 0 || height <= 0)
            return ValueTask.FromResult<SKBitmap?>(null);

        var view = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(view))
        {
            canvas.Clear(SKColors.Transparent);
            Draw(canvas, data, name, zoom, minX, maxY, (maxX - minX) / width, (maxY - minY) / height, width, height);
        }

        return ValueTask.FromResult<SKBitmap?>(view);
    }

    public JsonObject ToTileJson(string? palette, DateTime? time = null)
    {
        var json = _inner.ToTileJson(palette, time);
        int chartMinZoom = (int?)json["minzoom"] ?? 0;
        if (chartMinZoom > 0)
        {
            json["minzoom"] = 0;
            if (json["s100"] is JsonObject s100)
            {
                s100["chartMinzoom"] = chartMinZoom;
                s100["overview"] = new JsonObject
                {
                    ["minzoom"] = 0,
                    ["maxzoom"] = chartMinZoom - 1,
                };
            }
        }

        return json;
    }

    public async ValueTask DisposeAsync()
    {
        if (_inner is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        else
            (_inner as IDisposable)?.Dispose();
    }

    /// <summary>The overview of the datasets served now: built again after a reload.</summary>
    private OverviewData Current()
    {
        var revision = _inner.Revision ?? string.Empty;
        var data = Volatile.Read(ref _data);
        if (data is not null && data.Revision == revision)
            return data;

        data = OverviewData.Build(_inner, revision);
        Volatile.Write(ref _data, data);
        return data;
    }

    /// <summary>
    /// The palette of a requested style — <c>day</c>, <c>day-{hash}</c> or
    /// <see langword="null"/> for the default — or <see langword="null"/> when
    /// the source doesn't serve it.
    /// </summary>
    private string? ResolvePalette(string? requested, OverviewData data)
    {
        if (requested is null)
            return data.DefaultPalette;

        var style = requested.Trim().ToLowerInvariant();
        if (Palettes.Count > 0 && !Palettes.Contains(style))
            return null;

        int dash = style.IndexOf('-', StringComparison.Ordinal);
        return dash < 0 ? style : style[..dash];
    }

    private byte[] RenderTile(OverviewData data, string palette, int zoom, int x, int y)
    {
        int pixels = data.TilePixels;
        double size = XyzTileGrid.TileWorldSize(zoom);
        double originX = (x * size) - XyzTileGrid.Extent;
        double originY = XyzTileGrid.Extent - (y * size);

        using var bitmap = new SKBitmap(new SKImageInfo(pixels, pixels, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            // JPEG has no transparency.
            canvas.Clear(Format == TileImageFormat.Jpeg ? SKColors.White : SKColors.Transparent);
            if (!Draw(canvas, data, palette, zoom, originX, originY, size / pixels, size / pixels, pixels, pixels))
                return [];
        }

        var format = Format switch
        {
            TileImageFormat.Jpeg => SKEncodedImageFormat.Jpeg,
            TileImageFormat.Webp => SKEncodedImageFormat.Webp,
            _ => SKEncodedImageFormat.Png,
        };
        using var encoded = bitmap.Encode(format, 90);
        return encoded?.ToArray() ?? [];
    }

    /// <summary>
    /// Draws the overview of zoom level <paramref name="zoom"/> into a
    /// <paramref name="width"/> × <paramref name="height"/> image whose
    /// top-left corner lies at Web Mercator <paramref name="originX"/>,
    /// <paramref name="originY"/> and whose pixels are
    /// <paramref name="pixelX"/> × <paramref name="pixelY"/> metres. Lines,
    /// squares and text keep the size they have on the zoom level's tiles,
    /// scaled as resampling those tiles would scale them.
    /// </summary>
    /// <returns>Whether anything was drawn.</returns>
    internal static bool Draw(
        SKCanvas canvas, OverviewData data, string palette, int zoom,
        double originX, double originY, double pixelX, double pixelY, int width, int height)
    {
        var colours = OverviewColours.For(palette);
        double tilePixel = XyzTileGrid.TileWorldSize(zoom) / data.TilePixels;
        float scale = (float)(tilePixel / pixelX) * data.TilePixels / XyzTileGrid.TileSize;
        float marker = MarkerSize * scale;
        double world = 2 * XyzTileGrid.Extent;
        double viewMaxX = originX + (width * pixelX), viewMinY = originY - (height * pixelY);
        double marginX = Math.Max(marker, 2) * pixelX, marginY = Math.Max(marker, 2) * pixelY;

        using var fill = new SKPaint { Color = colours.Fill, Style = SKPaintStyle.Fill, IsAntialias = true };
        using var stroke = new SKPaint { Color = colours.Line, Style = SKPaintStyle.Stroke, StrokeWidth = LineWidth * scale, IsAntialias = true };
        using var solid = new SKPaint { Color = colours.Line, Style = SKPaintStyle.Fill, IsAntialias = true };

        bool drew = false;
        foreach (var dataset in data.Datasets)
        {
            // The world's copies either side, for views across ±180°.
            for (int copy = -1; copy <= 1; copy++)
            {
                double offset = copy * world;
                var b = dataset.Bounds;
                if (b.MaxX + offset < originX - marginX || b.MinX + offset > viewMaxX + marginX
                    || b.MaxY < viewMinY - marginY || b.MinY > originY + marginY)
                {
                    continue;
                }

                float Px(double mx) => (float)((mx + offset - originX) / pixelX);
                float Py(double my) => (float)((originY - my) / pixelY);

                if ((b.MaxX - b.MinX) / pixelX < marker && (b.MaxY - b.MinY) / pixelY < marker)
                {
                    // Too small to outline: a square on its centre.
                    float cx = Px((b.MinX + b.MaxX) / 2), cy = Py((b.MinY + b.MaxY) / 2);
                    canvas.DrawRect(cx - (marker / 2), cy - (marker / 2), marker, marker, solid);
                    drew = true;
                    continue;
                }

                using var path = new SKPath { FillType = SKPathFillType.EvenOdd };
                foreach (var ring in dataset.Rings)
                {
                    if (ring.Length < 3)
                        continue;
                    path.MoveTo(Px(ring[0].X), Py(ring[0].Y));
                    for (int i = 1; i < ring.Length; i++)
                        path.LineTo(Px(ring[i].X), Py(ring[i].Y));
                    path.Close();
                }

                canvas.DrawPath(path, fill);
                canvas.DrawPath(path, stroke);
                drew = true;
            }
        }

        if (!drew)
            return false;

        // Names, as placed for this zoom level, in tile pixels from the world's top-left corner.
        using var font = new SKFont(OverviewData.Typeface, LabelSize * scale);
        using var halo = new SKPaint { Color = colours.Halo, Style = SKPaintStyle.Stroke, StrokeWidth = 3 * scale, StrokeJoin = SKStrokeJoin.Round, IsAntialias = true };
        using var text = new SKPaint { Color = colours.Text, IsAntialias = true };
        foreach (var label in data.LabelsAt(zoom))
        {
            for (int copy = -1; copy <= 1; copy++)
            {
                double x = (label.CenterX * tilePixel) - XyzTileGrid.Extent + (copy * world);
                double y = XyzTileGrid.Extent - (label.BaselineY * tilePixel);
                float px = (float)((x - originX) / pixelX), py = (float)((originY - y) / pixelY);
                float half = label.Width * scale / 2;
                if (px + half < 0 || px - half > width || py + (font.Size / 2) < 0 || py - font.Size > height)
                    continue;

                canvas.DrawText(label.Text, px, py, SKTextAlign.Center, font, halo);
                canvas.DrawText(label.Text, px, py, SKTextAlign.Center, font, text);
            }
        }

        return true;
    }

    /// <summary>The outline width on a 256-pixel tile, in pixels.</summary>
    internal const float LineWidth = 1.5f;

    /// <summary>The edge of the square drawn for a dataset too small to outline, on a 256-pixel tile.</summary>
    internal const float MarkerSize = 6;

    /// <summary>The size of a name on a 256-pixel tile, in pixels.</summary>
    internal const float LabelSize = 11;
}

/// <summary>The overview's colours in a palette.</summary>
internal readonly record struct OverviewColours(SKColor Line, SKColor Fill, SKColor Text, SKColor Halo)
{
    /// <summary>
    /// The colours for <paramref name="palette"/>: magenta, as chart limits
    /// are drawn, dimmed at dusk and at night.
    /// </summary>
    public static OverviewColours For(string palette) => palette switch
    {
        "night" => new(new SKColor(110, 40, 110), new SKColor(110, 40, 110, 24), new SKColor(140, 120, 140), new SKColor(0, 0, 0, 200)),
        "dusk" => new(new SKColor(190, 90, 190), new SKColor(190, 90, 190, 32), new SKColor(225, 210, 225), new SKColor(20, 20, 30, 200)),
        _ => new(new SKColor(176, 0, 176), new SKColor(176, 0, 176, 28), new SKColor(50, 20, 50), new SKColor(255, 255, 255, 220)),
    };
}

/// <summary>
/// What the overview draws, from the <c>/datasets</c> document: each dataset's
/// coverage rings in Web Mercator and its name, and where names go at each
/// zoom level.
/// </summary>
internal sealed class OverviewData
{
    private const double MaxLatitude = 85.0511287798066;

    private readonly ConcurrentDictionary<int, IReadOnlyList<OverviewLabel>> _labels = new();

    private OverviewData(string revision, int chartMinZoom, int tilePixels, string defaultPalette, IReadOnlyList<OverviewDataset> datasets)
    {
        Revision = revision;
        ChartMinZoom = chartMinZoom;
        TilePixels = tilePixels;
        DefaultPalette = defaultPalette;
        Datasets = datasets;
    }

    /// <summary>A typeface with glyphs: the default, or any the host has when the default is empty.</summary>
    internal static SKTypeface Typeface { get; } =
        SKTypeface.Default.GlyphCount > 0 ? SKTypeface.Default : SKFontManager.Default.MatchCharacter('A') ?? SKTypeface.Default;

    /// <summary>The revision of the datasets this was built from.</summary>
    public string Revision { get; }

    /// <summary>The chart tiles' minimum zoom.</summary>
    public int ChartMinZoom { get; }

    /// <summary>The edge of a tile, in pixels.</summary>
    public int TilePixels { get; }

    /// <summary>The palette served without one asked for.</summary>
    public string DefaultPalette { get; }

    /// <summary>The datasets, in input order.</summary>
    public IReadOnlyList<OverviewDataset> Datasets { get; }

    /// <summary>Builds the overview of the datasets behind <paramref name="source"/>.</summary>
    public static OverviewData Build(ITileSource source, string revision)
    {
        var tileJson = source.ToTileJson(palette: null);
        int chartMinZoom = (int?)tileJson["minzoom"] ?? 0;
        int tilePixels = (int?)tileJson["tileSize"] ?? XyzTileGrid.TileSize;
        var defaultPalette = ((string?)tileJson["s100"]?["palette"] ?? RenderedTileSource.AllPalettes[0]).Trim().ToLowerInvariant();
        return new OverviewData(revision, chartMinZoom, tilePixels, defaultPalette, Parse(source.ToDatasetsJson()));
    }

    /// <summary>The datasets of a <c>/datasets</c> document that have a coverage.</summary>
    internal static IReadOnlyList<OverviewDataset> Parse(JsonObject? document)
    {
        var datasets = new List<OverviewDataset>();
        foreach (var node in document?["datasets"]?.AsArray() ?? [])
        {
            if (node?["coverage"]?["coordinates"] is not JsonArray polygons)
                continue;

            var rings = new List<(double X, double Y)[]>();
            foreach (var polygon in polygons)
            {
                foreach (var ring in polygon?.AsArray() ?? [])
                {
                    var points = (ring?.AsArray() ?? [])
                        .Select(p => p!.AsArray())
                        .Where(p => p.Count >= 2)
                        .Select(p => WebMercator.FromLonLat((double)p[0]!, Math.Clamp((double)p[1]!, -MaxLatitude, MaxLatitude)))
                        .Select(p => (p.X, p.Y))
                        .ToArray();
                    if (points.Length > 0)
                        rings.Add(points);
                }
            }

            if (rings.Count == 0)
                continue;

            var all = rings.SelectMany(r => r).ToArray();
            var bounds = new MercatorBounds(all.Min(p => p.X), all.Min(p => p.Y), all.Max(p => p.X), all.Max(p => p.Y));
            var name = (string?)node["id"] ?? (string?)node["file"] ?? string.Empty;
            datasets.Add(new OverviewDataset(name, rings, bounds));
        }

        return datasets;
    }

    /// <summary>
    /// The names placed at <paramref name="zoom"/>: one per dataset, on its
    /// centre (under its square when it's too small to outline), left out
    /// where it would overlap a name already placed.
    /// </summary>
    public IReadOnlyList<OverviewLabel> LabelsAt(int zoom) => _labels.GetOrAdd(zoom, Place);

    private List<OverviewLabel> Place(int zoom)
    {
        // Tile pixels at this zoom, from the world's top-left corner; sizes on
        // a 256-pixel tile scale with the tile.
        double tilePixel = XyzTileGrid.TileWorldSize(zoom) / TilePixels;
        float ratio = (float)TilePixels / XyzTileGrid.TileSize;
        float size = OverviewTileSource.LabelSize * ratio;
        float marker = OverviewTileSource.MarkerSize * ratio;
        using var font = new SKFont(Typeface, size);

        var placed = new List<OverviewLabel>();
        var taken = new List<SKRect>();
        foreach (var dataset in Datasets)
        {
            if (dataset.Name.Length == 0)
                continue;

            var b = dataset.Bounds;
            double cx = ((b.MinX + b.MaxX) / 2 + XyzTileGrid.Extent) / tilePixel;
            double cy = (XyzTileGrid.Extent - ((b.MinY + b.MaxY) / 2)) / tilePixel;
            double pixelHeight = (b.MaxY - b.MinY) / tilePixel;

            // On the centre when the outline is tall enough to hold it; otherwise under it.
            double baseline = pixelHeight >= 2 * size
                ? cy + (size * 0.35)
                : cy + (Math.Max(pixelHeight, marker) / 2) + (size * 1.05);
            float width = font.MeasureText(dataset.Name);
            var rect = new SKRect((float)(cx - (width / 2) - 2), (float)(baseline - size - 1), (float)(cx + (width / 2) + 2), (float)(baseline + (size * 0.3) + 1));
            if (taken.Any(r => r.IntersectsWith(rect)))
                continue;

            taken.Add(rect);
            placed.Add(new OverviewLabel(dataset.Name, cx, baseline, width / ratio));
        }

        return placed;
    }
}

/// <summary>A Web Mercator box.</summary>
internal readonly record struct MercatorBounds(double MinX, double MinY, double MaxX, double MaxY);

/// <summary>A dataset's name and coverage rings, in Web Mercator metres.</summary>
internal sealed record OverviewDataset(string Name, IReadOnlyList<(double X, double Y)[]> Rings, MercatorBounds Bounds);

/// <summary>
/// A placed name: its centre and baseline in tile pixels of its zoom level
/// from the world's top-left corner, and its width on a 256-pixel tile.
/// </summary>
internal sealed record OverviewLabel(string Text, double CenterX, double BaselineY, float Width);
