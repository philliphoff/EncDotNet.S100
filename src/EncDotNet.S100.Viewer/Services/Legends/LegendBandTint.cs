using EncDotNet.S100.Collections;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using Mapsui.Projections;

namespace EncDotNet.S100.Viewer.Services.Legends;

/// <summary>
/// The map view a band tint is sampled over: its EPSG:3857 extent, in the
/// frame the map shows (past ±180° on a world copy), and its resolution.
/// </summary>
/// <param name="MinX">West edge, EPSG:3857 metres.</param>
/// <param name="MinY">South edge, EPSG:3857 metres.</param>
/// <param name="MaxX">East edge, EPSG:3857 metres.</param>
/// <param name="MaxY">North edge, EPSG:3857 metres.</param>
/// <param name="Resolution">Metres per screen pixel; 0 or less when not known.</param>
internal readonly record struct LegendTintView(double MinX, double MinY, double MaxX, double MaxY, double Resolution)
{
    /// <summary>The view of a viewport snapshot; null without one.</summary>
    /// <param name="snapshot">The map's viewport.</param>
    public static LegendTintView? Of(MapViewportSnapshot? snapshot)
    {
        if (snapshot is null)
            return null;
        var (minX, minY) = SphericalMercator.FromLonLat(snapshot.MinLongitude, snapshot.MinLatitude);
        var (maxX, maxY) = SphericalMercator.FromLonLat(snapshot.MaxLongitude, snapshot.MaxLatitude);
        return maxX > minX && maxY > minY
            ? new LegendTintView(minX, minY, maxX, maxY, snapshot.MercatorResolution)
            : null;
    }
}

/// <summary>
/// The area of a colour or arrow ramp band drawn in the map view (hover to
/// find, #1433): a low-resolution mask over the view, one cell per few
/// screen pixels, set where the coverage drawn there has a value in the band.
/// </summary>
/// <param name="View">The view the mask covers.</param>
/// <param name="Width">Mask columns, west to east.</param>
/// <param name="Height">Mask rows, north to south.</param>
/// <param name="Mask">Row-major, row 0 at the north edge: whether each mask cell is in the band.</param>
internal sealed record LegendBandTint(LegendTintView View, int Width, int Height, bool[] Mask)
{
    /// <summary>How many mask cells are tinted.</summary>
    public int TintedCells { get; init; }

    /// <summary>The share of the view tinted, 0 to 1.</summary>
    public double ViewFraction => Width * Height == 0 ? 0 : TintedCells / (double)(Width * Height);

    /// <summary>The tinted area on the ground, in square kilometres.</summary>
    public double AreaSquareKilometres { get; init; }

    /// <summary>The bounds of the tinted cells; null when none is.</summary>
    public GeoBounds? Bounds { get; init; }

    /// <summary>Whether mask cell (<paramref name="column"/>, <paramref name="row"/>) is tinted.</summary>
    public bool IsTinted(int column, int row) => Mask[(row * Width) + column];
}

/// <summary>
/// Builds a <see cref="LegendBandTint"/> from the coverage layers a band row
/// describes, reading the grids the stack already holds: nothing is portrayed
/// or sampled from the source again.
/// </summary>
/// <remarks>
/// <para>Each mask cell's centre goes from EPSG:3857 to WGS-84 and then into
/// the grid's own CRS, the inverse of the renderers' node projection
/// (<c>MapsuiCoverageRenderer</c>, <c>CoverageSymbolField</c>), and the grid
/// node nearest to it is read. So a UTM S-102 grid, a geographic grid in a
/// 0…360° frame, or one whose axis order was swapped (the styled layer is
/// already read back to latitude rows) tints where it is drawn.</para>
/// <para>Layers are added top of the stack first. A colour fill is opaque: the
/// top fill with data in a cell decides it, as it does on screen. Arrows
/// don't hide each other, so any arrow layer in the band tints a cell; every
/// grid cell in the band is tinted, not only the thinned arrows drawn.</para>
/// <para>The land clip of a fill clipped to water isn't applied: the tint
/// follows the grid cells.</para>
/// </remarks>
internal sealed class LegendBandTinter
{
    /// <summary>The most mask cells on the long side of the view.</summary>
    public const int MaxMaskSize = 256;

    /// <summary>The fewest screen pixels per mask cell.</summary>
    public const int ScreenPixelsPerCell = 3;

    private readonly LegendTintView _view;
    private readonly ICrsTransformFactory _transforms;
    private readonly int _width;
    private readonly int _height;
    private readonly bool[] _mask;
    private readonly bool[] _decided;
    private readonly double[] _lon;
    private readonly double[] _lat;

    /// <summary>Starts a mask over <paramref name="view"/>.</summary>
    /// <param name="view">The map view.</param>
    /// <param name="transforms">Transforms WGS-84 into each grid's CRS.</param>
    public LegendBandTinter(LegendTintView view, ICrsTransformFactory transforms)
    {
        ArgumentNullException.ThrowIfNull(transforms);
        _view = view;
        _transforms = transforms;
        (_width, _height) = MaskSize(view);
        _mask = new bool[_width * _height];
        _decided = new bool[_width * _height];

        // Mercator is separable: a column has one longitude, a row one latitude.
        _lon = new double[_width];
        _lat = new double[_height];
        var cellX = (view.MaxX - view.MinX) / _width;
        var cellY = (view.MaxY - view.MinY) / _height;
        for (var i = 0; i < _width; i++)
            _lon[i] = SphericalMercator.ToLonLat(view.MinX + ((i + 0.5) * cellX), 0).lon;
        for (var j = 0; j < _height; j++)
            _lat[j] = SphericalMercator.ToLonLat(0, view.MaxY - ((j + 0.5) * cellY)).lat;
    }

    /// <summary>How many layers tinted at least one cell.</summary>
    public int Layers { get; private set; }

    /// <summary>The mask size for a view: a cell per few screen pixels, at most <see cref="MaxMaskSize"/> on the long side.</summary>
    /// <param name="view">The map view.</param>
    public static (int Width, int Height) MaskSize(LegendTintView view)
    {
        var spanX = view.MaxX - view.MinX;
        var spanY = view.MaxY - view.MinY;
        double width, height;
        if (view.Resolution > 0)
        {
            width = spanX / view.Resolution / ScreenPixelsPerCell;
            height = spanY / view.Resolution / ScreenPixelsPerCell;
        }
        else
        {
            width = spanX >= spanY ? MaxMaskSize : MaxMaskSize * spanX / spanY;
            height = spanY > spanX ? MaxMaskSize : MaxMaskSize * spanY / spanX;
        }
        var shrink = Math.Min(1.0, MaxMaskSize / Math.Max(width, height));
        return (
            Math.Clamp((int)Math.Ceiling(width * shrink), 1, MaxMaskSize),
            Math.Clamp((int)Math.Ceiling(height * shrink), 1, MaxMaskSize));
    }

    /// <summary>
    /// The band a legend row is in a ramp of <paramref name="bandCount"/>
    /// bands: the row's index when the rows are the bands, lowest first
    /// (<see cref="LegendEntry.FromScheme"/>, S-102's depth legend, S-111's
    /// speed legend); otherwise the band labelled as the row; null when neither.
    /// </summary>
    /// <param name="rows">The sub-layer's legend rows.</param>
    /// <param name="rowLabel">The row's label.</param>
    /// <param name="bandLabels">The ramp's band labels, lowest first.</param>
    public static int? BandIndex(IReadOnlyList<LegendEntry> rows, string rowLabel, IReadOnlyList<string?> bandLabels)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(bandLabels);
        if (rows.Count == bandLabels.Count)
        {
            for (var i = 0; i < rows.Count; i++)
            {
                if (string.Equals(rows[i].Label, rowLabel, StringComparison.Ordinal))
                    return i;
            }
        }
        for (var i = 0; i < bandLabels.Count; i++)
        {
            if (string.Equals(bandLabels[i], rowLabel, StringComparison.Ordinal))
                return i;
        }
        return null;
    }

    /// <summary>
    /// Adds a coverage sub-layer's cells in the band of <paramref name="rowLabel"/>.
    /// Call top of the stack first.
    /// </summary>
    /// <param name="subLayer">A colour fill or arrow sub-layer.</param>
    /// <param name="rows">Its legend rows.</param>
    /// <param name="rowLabel">The row's label.</param>
    /// <returns>Whether the row is a band of the sub-layer's ramp.</returns>
    public bool Add(CoverageSubLayerBase subLayer, IReadOnlyList<LegendEntry> rows, string rowLabel)
    {
        ArgumentNullException.ThrowIfNull(subLayer);
        ArgumentNullException.ThrowIfNull(rows);
        switch (subLayer)
        {
            case GridCoverageSubLayer { Coverage: { ColorScheme: { } scheme } coverage }:
                {
                    if (BandIndex(rows, rowLabel, [.. scheme.Bands.Select(b => b.Label)]) is not { } index)
                        return false;
                    var band = scheme.Bands[index];
                    Sample(coverage, scheme.FieldName, opaque: true, noDataDecides: !string.IsNullOrEmpty(scheme.NoDataColor),
                        (value, _) => value >= band.MinValue && value < band.MaxValue);
                    return true;
                }
            case ArrowCoverageSubLayer { Coverage: { SymbolScheme: { } symbols } coverage }:
                {
                    if (BandIndex(rows, rowLabel, [.. symbols.Bands.Select(b => b.Label)]) is not { } index)
                        return false;
                    var north = symbols.Bands[index];
                    var south = symbols.SouthernHemisphereBands is { } s && index < s.Count ? s[index] : north;
                    Sample(coverage, symbols.ValueFieldName, opaque: false, noDataDecides: false,
                        (value, lat) => lat < 0 ? value >= south.MinValue && value < south.MaxValue : value >= north.MinValue && value < north.MaxValue);
                    return true;
                }
            default:
                return false;
        }
    }

    /// <summary>The tint so far.</summary>
    public LegendBandTint Build()
    {
        var cellX = (_view.MaxX - _view.MinX) / _width;
        var cellY = (_view.MaxY - _view.MinY) / _height;
        int tinted = 0;
        double area = 0;
        double south = double.MaxValue, north = double.MinValue, west = double.MaxValue, east = double.MinValue;
        for (var j = 0; j < _height; j++)
        {
            // A Mercator cell's ground size is its projected size times cos(latitude).
            var cos = Math.Cos(_lat[j] * Math.PI / 180);
            for (var i = 0; i < _width; i++)
            {
                if (!_mask[(j * _width) + i])
                    continue;
                tinted++;
                area += cellX * cellY * cos * cos;
                south = Math.Min(south, _lat[j]);
                north = Math.Max(north, _lat[j]);
                west = Math.Min(west, _lon[i]);
                east = Math.Max(east, _lon[i]);
            }
        }
        GeoBounds? bounds = null;
        if (tinted > 0)
        {
            bounds = east - west >= 360
                ? new GeoBounds(south, -180, north, 180)
                : new GeoBounds(south, GeoBounds.NormalizeLongitude(west), north, GeoBounds.NormalizeLongitude(east));
        }
        return new LegendBandTint(_view, _width, _height, _mask)
        {
            TintedCells = tinted,
            AreaSquareKilometres = area / 1e6,
            Bounds = bounds,
        };
    }

    private void Sample(StyledCoverageLayer layer, string field, bool opaque, bool noDataDecides, Func<float, double, bool> inBand)
    {
        var sampled = layer.Coverage;
        if (!sampled.HasField(field))
            return;
        var values = sampled.GetValues(field);
        int rows = sampled.Metadata.NumRows, cols = sampled.Metadata.NumColumns;
        if (rows <= 0 || cols <= 0 || values.Length < rows * cols)
            return;

        var geo = layer.Georeferencer;
        var grid = geo.Metadata;
        if (grid.SpacingLatitudinal == 0 || grid.SpacingLongitudinal == 0)
            return;
        var toNative = _transforms.Create("EPSG:4326", geo.CRS);
        var footprint = Footprint(geo, rows, cols, _transforms.Create(geo.CRS, "EPSG:4326"));
        if (footprint is not { } fp)
            return;

        var noData = layer.NoDataValue;
        var noDataIsNaN = float.IsNaN(noData);
        var any = false;
        for (var j = 0; j < _height; j++)
        {
            var lat = _lat[j];
            if (lat < fp.South || lat > fp.North)
                continue;
            for (var i = 0; i < _width; i++)
            {
                var p = (j * _width) + i;
                if (opaque && _decided[p])
                    continue;
                // The longitude in the footprint's frame (−180…180 or 0…360).
                if (!InFrame(GeoBounds.NormalizeLongitude(_lon[i]), fp.West, fp.East, out var lon))
                    continue;

                var (x, y) = toNative.IsIdentity ? (lon, lat) : toNative.Transform(lon, lat);
                var row = (int)Math.Round((y - grid.OriginLatitude) / grid.SpacingLatitudinal);
                var col = (int)Math.Round((x - grid.OriginLongitude) / grid.SpacingLongitudinal);
                if ((uint)row >= (uint)rows || (uint)col >= (uint)cols)
                    continue;

                var value = values[(row * cols) + col];
                if (noDataIsNaN ? float.IsNaN(value) : value == noData)
                {
                    if (opaque && noDataDecides)
                        _decided[p] = true;
                    continue;
                }
                if (opaque)
                    _decided[p] = true;
                if (inBand(value, lat))
                {
                    _mask[p] = true;
                    any = true;
                }
            }
        }
        if (any)
            Layers++;
    }

    private static bool InFrame(double lon, double west, double east, out double framed)
    {
        foreach (var shift in (ReadOnlySpan<double>)[0, 360, -360])
        {
            framed = lon + shift;
            if (framed >= west && framed <= east)
                return true;
        }
        framed = lon;
        return false;
    }

    // The grid's WGS-84 bounds, from its edge nodes, padded by a cell: a cheap
    // reject before a cell centre is transformed.
    private static (double South, double West, double North, double East)? Footprint(
        GridGeoreferencer geo, int rows, int cols, ICrsTransform toWgs84)
    {
        double south = double.MaxValue, north = double.MinValue, west = double.MaxValue, east = double.MinValue;
        const int steps = 16;
        void Add(double r, double c)
        {
            var x = geo.Metadata.OriginLongitude + (c * geo.Metadata.SpacingLongitudinal);
            var y = geo.Metadata.OriginLatitude + (r * geo.Metadata.SpacingLatitudinal);
            var (lon, lat) = toWgs84.IsIdentity ? (x, y) : toWgs84.Transform(x, y);
            if (!double.IsFinite(lon) || !double.IsFinite(lat))
                return;
            south = Math.Min(south, lat);
            north = Math.Max(north, lat);
            west = Math.Min(west, lon);
            east = Math.Max(east, lon);
        }
        // A cell past each edge node, so a centre half a cell out still counts.
        double r0 = -1, r1 = rows, c0 = -1, c1 = cols;
        for (var k = 0; k <= steps; k++)
        {
            var t = k / (double)steps;
            Add(r0 + ((r1 - r0) * t), c0);
            Add(r0 + ((r1 - r0) * t), c1);
            Add(r0, c0 + ((c1 - c0) * t));
            Add(r1, c0 + ((c1 - c0) * t));
        }
        return south <= north && west <= east ? (south, west, north, east) : null;
    }
}
