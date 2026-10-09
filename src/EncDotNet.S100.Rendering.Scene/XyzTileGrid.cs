namespace EncDotNet.S100.Rendering.Scene;

/// <summary>
/// The standard XYZ ("slippy map") Web-Mercator tile pyramid used by web maps
/// such as MapLibre, Leaflet and OpenLayers: zoom <c>z</c> splits the EPSG:3857
/// world square into <c>2^z × 2^z</c> tiles of 256 logical pixels, with column
/// <c>x</c> increasing east from −180° and row <c>y</c> increasing south from
/// the northern projection limit.
/// </summary>
/// <remarks>
/// The grid is the same one the viewer's tile renderer uses, but has no Mapsui
/// dependency, so headless tools (the <c>s100 tiles</c> command) can share it.
/// </remarks>
public static class XyzTileGrid
{
    /// <summary>Tile edge length in logical pixels.</summary>
    public const int TileSize = 256;

    /// <summary>The largest zoom level the grid supports.</summary>
    public const int MaxZoom = 24;

    /// <summary>
    /// Half the EPSG:3857 world extent in metres (<c>π · 6378137</c>). The grid
    /// spans <c>[-Extent, +Extent]</c> on both axes.
    /// </summary>
    public const double Extent = Math.PI * WebMercator.EarthRadius;

    /// <summary>The number of tiles along one axis at <paramref name="zoom"/> (<c>2^zoom</c>).</summary>
    /// <param name="zoom">The zoom level (0 to <see cref="MaxZoom"/>).</param>
    public static int TilesPerAxis(int zoom)
    {
        ValidateZoom(zoom);
        return 1 << zoom;
    }

    /// <summary>The world size of one tile at <paramref name="zoom"/>, in EPSG:3857 metres.</summary>
    /// <param name="zoom">The zoom level (0 to <see cref="MaxZoom"/>).</param>
    public static double TileWorldSize(int zoom) => 2.0 * Extent / TilesPerAxis(zoom);

    /// <summary>
    /// The EPSG:3857 resolution, in metres per logical pixel, at
    /// <paramref name="zoom"/>.
    /// </summary>
    /// <param name="zoom">The zoom level (0 to <see cref="MaxZoom"/>).</param>
    public static double Resolution(int zoom) => TileWorldSize(zoom) / TileSize;

    /// <summary>The EPSG:3857 bounds of a tile, in metres.</summary>
    /// <param name="zoom">The zoom level.</param>
    /// <param name="x">The tile column (0 at −180°).</param>
    /// <param name="y">The tile row (0 at the northern limit).</param>
    public static (double MinX, double MinY, double MaxX, double MaxY) TileWorldBounds(int zoom, int x, int y)
    {
        double size = TileWorldSize(zoom);
        double minX = -Extent + x * size;
        double maxY = Extent - y * size;
        return (minX, maxY - size, minX + size, maxY);
    }

    /// <summary>
    /// The S-100 display scale denominator of <paramref name="zoom"/> at
    /// <paramref name="latitude"/>, using the 0.28 mm nominal pixel of
    /// <see cref="ScaleVisibility.DenomToResolutionMetres"/>.
    /// </summary>
    /// <remarks>
    /// Web-Mercator stretches the ground by <c>1 / cos(latitude)</c>, so one zoom
    /// level shows a different true scale at each latitude.
    /// </remarks>
    /// <param name="zoom">The zoom level.</param>
    /// <param name="latitude">The latitude, in degrees, the scale is measured at.</param>
    public static double ScaleDenominator(int zoom, double latitude)
    {
        double clamped = Math.Clamp(latitude, -WebMercator.MaxLatitude, WebMercator.MaxLatitude);
        return Resolution(zoom) * Math.Cos(clamped * Math.PI / 180.0)
            / ScaleVisibility.DenomToResolutionMetres;
    }

    /// <summary>
    /// The zoom level whose display scale at <paramref name="latitude"/> is
    /// closest (in log space) to <paramref name="scaleDenominator"/>, clamped to
    /// 0 to <see cref="MaxZoom"/>.
    /// </summary>
    /// <param name="scaleDenominator">The scale denominator, such as 50 000 for 1:50 000.</param>
    /// <param name="latitude">The latitude, in degrees, the scale is measured at.</param>
    public static int ZoomForScaleDenominator(double scaleDenominator, double latitude)
    {
        if (!(scaleDenominator > 0) || double.IsInfinity(scaleDenominator))
            return 0;

        double zoom = Math.Log2(ScaleDenominator(0, latitude) / scaleDenominator);
        return Math.Clamp((int)Math.Round(zoom), 0, MaxZoom);
    }

    /// <summary>
    /// The tile columns at <paramref name="zoom"/> that intersect the world-X
    /// span <paramref name="minX"/> to <paramref name="maxX"/>, in ascending
    /// order and without duplicates.
    /// </summary>
    /// <remarks>
    /// The span may extend past ±<see cref="Extent"/>, as it does for data kept in
    /// a 0…360° frame or a seam-aware extent that crosses the antimeridian. The
    /// columns past either edge wrap around onto the canonical grid.
    /// </remarks>
    /// <param name="zoom">The zoom level.</param>
    /// <param name="minX">The western edge, in EPSG:3857 metres.</param>
    /// <param name="maxX">The eastern edge, in EPSG:3857 metres.</param>
    public static IReadOnlyList<int> Columns(int zoom, double minX, double maxX)
    {
        int n = TilesPerAxis(zoom);
        if (!(maxX >= minX))
            return [];

        double size = TileWorldSize(zoom);
        long first = (long)Math.Floor((minX + Extent) / size);
        long last = Math.Max(first, (long)Math.Ceiling((maxX + Extent) / size) - 1);
        if (last - first + 1 >= n)
            return Enumerable.Range(0, n).ToArray();

        var columns = new SortedSet<int>();
        for (long column = first; column <= last; column++)
            columns.Add((int)(((column % n) + n) % n));
        return columns.ToArray();
    }

    /// <summary>
    /// The first and last tile rows at <paramref name="zoom"/> that intersect the
    /// world-Y span <paramref name="minY"/> to <paramref name="maxY"/>, clamped to
    /// the grid.
    /// </summary>
    /// <param name="zoom">The zoom level.</param>
    /// <param name="minY">The southern edge, in EPSG:3857 metres.</param>
    /// <param name="maxY">The northern edge, in EPSG:3857 metres.</param>
    public static (int First, int Last) Rows(int zoom, double minY, double maxY)
    {
        int n = TilesPerAxis(zoom);
        double size = TileWorldSize(zoom);
        int first = (int)Math.Clamp(Math.Floor((Extent - maxY) / size), 0, n - 1);
        int last = (int)Math.Clamp(Math.Ceiling((Extent - minY) / size) - 1, 0, n - 1);
        return (first, Math.Max(first, last));
    }

    private static void ValidateZoom(int zoom)
    {
        if (zoom is < 0 or > MaxZoom)
            throw new ArgumentOutOfRangeException(nameof(zoom), zoom, $"Zoom must be between 0 and {MaxZoom}.");
    }
}
