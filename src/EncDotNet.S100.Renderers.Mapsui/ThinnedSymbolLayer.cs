using EncDotNet.S100.Pipelines.Coverage;
using Mapsui;
using Mapsui.Layers;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// A Mapsui layer of scaled point symbols (S-111 current arrows) that thins
/// itself for the zoom it is drawn at. Each time Mapsui asks for the visible
/// features, the layer runs <see cref="SymbolThinning"/> for the current
/// resolution and returns only the symbols that keep the required on-screen
/// spacing, so arrow density stays legible from a whole-bay view down to a
/// harbour.
/// </summary>
/// <remarks>
/// <para>
/// A regular grid (constructed with <c>rows × cols</c> symbols) is thinned with
/// <see cref="SymbolThinning.ThinGrid"/> over the displayed field, as S-98
/// Appendix G-1.1 describes. Scattered points (a station series or an
/// ungeorectified mesh, constructed with <c>rows = 0</c>) are thinned with
/// <see cref="SymbolThinning.ThinPoints"/> over every point, so the selection
/// only changes with zoom and stays put while panning.
/// </para>
/// <para>
/// Features are created lazily, once per symbol, through the supplied factory
/// and reused across frames. The last query is cached, so repeated paints of an
/// unchanged view cost nothing.
/// </para>
/// </remarks>
public sealed class ThinnedSymbolLayer : BaseLayer
{
    private readonly int _rows;
    private readonly int _cols;
    private readonly double[] _x;
    private readonly double[] _y;
    private readonly float[] _scale;
    private readonly float[] _priority;
    private readonly double _lengthPixelsPerScale;
    private readonly double _maxRatio;
    private readonly Func<int, IFeature?> _featureFactory;
    private readonly IFeature?[] _features;
    private readonly bool[] _featureCreated;
    private readonly MRect? _extent;
    private readonly float _maxScale;
    private readonly object _gate = new();

    private MRect? _cachedRect;
    private double _cachedResolution = double.NaN;
    private IReadOnlyList<IFeature> _cachedFeatures = Array.Empty<IFeature>();

    private double _pointsResolution = double.NaN;
    private readonly List<int> _pointsKept = new();
    private readonly List<int> _scratch = new();

    /// <summary>
    /// Creates a thinned symbol layer.
    /// </summary>
    /// <param name="rows">
    /// Number of grid rows when the symbols form a regular grid (row-major, with
    /// <paramref name="cols"/> columns); 0 for scattered points.
    /// </param>
    /// <param name="cols">Number of grid columns; ignored when <paramref name="rows"/> is 0.</param>
    /// <param name="x">Per-symbol EPSG:3857 x.</param>
    /// <param name="y">Per-symbol EPSG:3857 y.</param>
    /// <param name="scale">
    /// Per-symbol scale relative to <paramref name="lengthPixelsPerScale"/>; NaN or
    /// non-positive for a cell without a symbol.
    /// </param>
    /// <param name="priority">Per-symbol thinning priority (higher is kept first; for S-111, speed).</param>
    /// <param name="lengthPixelsPerScale">On-screen length, in pixels, of a symbol at scale 1.</param>
    /// <param name="maxRatio">The thinning ratio <c>Rmax</c>.</param>
    /// <param name="featureFactory">Creates the feature for a symbol index; may return null to skip it.</param>
    public ThinnedSymbolLayer(
        int rows,
        int cols,
        double[] x,
        double[] y,
        float[] scale,
        float[] priority,
        double lengthPixelsPerScale,
        double maxRatio,
        Func<int, IFeature?> featureFactory)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        ArgumentNullException.ThrowIfNull(scale);
        ArgumentNullException.ThrowIfNull(priority);
        ArgumentNullException.ThrowIfNull(featureFactory);

        int count = x.Length;
        if (y.Length != count || scale.Length != count || priority.Length != count)
            throw new ArgumentException("Symbol arrays must have the same length.");
        if (rows > 0 && rows * cols != count)
            throw new ArgumentException("rows × cols must equal the number of symbols.", nameof(rows));

        _rows = Math.Max(0, rows);
        _cols = cols;
        _x = x;
        _y = y;
        _scale = scale;
        _priority = priority;
        _lengthPixelsPerScale = lengthPixelsPerScale;
        _maxRatio = maxRatio;
        _featureFactory = featureFactory;
        _features = new IFeature?[count];
        _featureCreated = new bool[count];

        double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        // The extent covers every symbol position, with or without data (a grid's
        // land cells included), so it is the dataset's extent rather than the
        // wet cells' — which can collapse to a line or a point.
        for (int i = 0; i < count; i++)
        {
            float s = scale[i];
            if (s > _maxScale) _maxScale = s;
            if (!double.IsFinite(x[i]) || !double.IsFinite(y[i]))
                continue;
            if (x[i] < minX) minX = x[i];
            if (x[i] > maxX) maxX = x[i];
            if (y[i] < minY) minY = y[i];
            if (y[i] > maxY) maxY = y[i];
        }

        _extent = minX <= maxX ? new MRect(minX, minY, maxX, maxY) : null;
    }

    /// <summary>Total number of symbol positions (drawn or not) the layer holds.</summary>
    public int SymbolCount => _x.Length;

    /// <inheritdoc />
    public override MRect? Extent => _extent;

    /// <inheritdoc />
    public override IEnumerable<IFeature> GetFeatures(MRect? rect, double resolution)
    {
        if (rect is null || !(resolution > 0) || _extent is null)
            return Array.Empty<IFeature>();

        lock (_gate)
        {
            if (resolution == _cachedResolution && _cachedRect is { } last
                && last.MinX == rect.MinX && last.MinY == rect.MinY
                && last.MaxX == rect.MaxX && last.MaxY == rect.MaxY)
            {
                return _cachedFeatures;
            }

            double lengthPerScale = _lengthPixelsPerScale * resolution;

            // Include symbols whose pivot is just off screen but whose arrow
            // reaches into it, so arrows do not pop in at the edges.
            double margin = _maxScale * lengthPerScale / 2.0;
            var displayed = new SymbolRect(rect.MinX, rect.MinY, rect.MaxX, rect.MaxY).Inflate(margin);

            if (_rows > 0)
            {
                SymbolThinning.ThinGrid(
                    _rows, _cols, _x, _y, _scale, _priority,
                    lengthPerScale, displayed, _maxRatio, _scratch);
            }
            else
            {
                if (resolution != _pointsResolution)
                {
                    var lengths = new double[_x.Length];
                    var priorities = new double[_x.Length];
                    for (int i = 0; i < lengths.Length; i++)
                    {
                        lengths[i] = _scale[i] * lengthPerScale;
                        priorities[i] = _priority[i];
                    }
                    SymbolThinning.ThinPoints(_x, _y, lengths, priorities, _maxRatio, _pointsKept);
                    _pointsResolution = resolution;
                }

                _scratch.Clear();
                foreach (int i in _pointsKept)
                {
                    if (displayed.Contains(_x[i], _y[i]))
                        _scratch.Add(i);
                }
            }

            var features = new List<IFeature>(_scratch.Count);
            foreach (int i in _scratch)
            {
                if (!_featureCreated[i])
                {
                    _features[i] = _featureFactory(i);
                    _featureCreated[i] = true;
                }
                if (_features[i] is { } feature)
                    features.Add(feature);
            }

            _cachedRect = rect.Copy();
            _cachedResolution = resolution;
            _cachedFeatures = features;
            return features;
        }
    }
}
