using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Services.Depth;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services.Legends;

/// <summary>The chart cell at the view centre, for the position legend (D17).</summary>
/// <param name="DatasetId">The dataset's session id.</param>
/// <param name="Name">The cell's name, as the Datasets panel shows it.</param>
/// <param name="Spec">The product it loads under ("S-101", "S-57").</param>
/// <param name="Edition">The cell's edition, or null when it declares none.</param>
/// <param name="CompilationScale">The scale it was compiled for (<see cref="IChartDepthCell.CompilationScale"/>), or null.</param>
/// <param name="DatumCode">The S-100 vertical datum code of its <c>SoundingDatum</c> at the centre, or null when not declared.</param>
internal sealed record PositionLegendCell(
    string DatasetId,
    string Name,
    string Spec,
    ChartCellEdition? Edition,
    int? CompilationScale,
    int? DatumCode);

/// <summary>
/// The S-98 position legend (Main §12.10.1, Table 12-5; legends design note
/// D17): what applies at the view centre.
/// </summary>
/// <param name="Latitude">The view centre's latitude, or null before the map has a view.</param>
/// <param name="Longitude">The view centre's longitude (−180…180), or null.</param>
/// <param name="DisplayScale">The display scale denominator at the centre, as the status bar computes it; null before the map has a view.</param>
/// <param name="DepthUnit">The Mariner setting depths are shown in.</param>
/// <param name="SafetyContourMetres">The Mariner's safety contour, in metres.</param>
/// <param name="DatumCode">
/// The datum the depths at the centre are relative to: the cell's, or with no
/// cell there, the one datum every depth source in view shares (the status
/// bar's depth basis).
/// </param>
/// <param name="HasDatum">Whether there's a datum to show: false with no cell at the centre and no single datum in view.</param>
/// <param name="Cell">The finest chart cell covering the centre, or null when none does.</param>
internal sealed record PositionLegend(
    double? Latitude,
    double? Longitude,
    double? DisplayScale,
    DepthUnit DepthUnit,
    double SafetyContourMetres,
    int? DatumCode,
    bool HasDatum,
    PositionLegendCell? Cell)
{
    /// <summary>Factors at or below this read as in scale (the overscale evaluator's margin).</summary>
    private const double OverscaleThreshold = 1.0 + 1e-6;

    /// <summary>How many times finer the display is than the cell's compilation scale; null when it isn't overscaled.</summary>
    public double? OverscaleFactor =>
        Cell?.CompilationScale is int compiled && DisplayScale is double shown && shown > 0 && compiled / shown > OverscaleThreshold
            ? compiled / shown
            : null;
}

/// <summary>
/// Keeps the S-98 position legend (#1407, legends D17): units, depth datum and
/// safety contour, then the cell at the view centre with its edition and
/// compilation scale against the display scale.
/// </summary>
/// <remarks>
/// <para>
/// It follows <see cref="LegendService.Changed"/>, so it updates on pan end
/// (<see cref="LegendService.PanSettleDelay"/>) and when the layer stack
/// changes, and follows the Mariner settings and the depth basis. The cell is
/// the one the pick report's depth uses: among the drawn chart cells whose
/// coverage contains the centre, the finest by <c>CellScaleRanking</c>
/// (<see cref="LocationDepthProbe.FinestCoveringCells"/>), the topmost of
/// peers.
/// </para>
/// <para>
/// Reading a cell's coverage and datum can mean indexing its features, so the
/// cells are read through <c>read</c> (the app runs it off the UI thread);
/// <see cref="Changed"/> is raised on the UI thread.
/// </para>
/// </remarks>
internal sealed class PositionLegendService : IDisposable
{
    private readonly IDatasetLoaderService _loader;
    private readonly DatasetsViewModel _datasets;
    private readonly IMapViewportNotifier? _viewport;
    private readonly IMarinerSettingsProvider _mariner;
    private readonly DepthBasisService? _depthBasis;
    private readonly LegendService _legend;
    private readonly Action<Action> _post;
    private readonly Func<Func<PositionLegendCell?>, Task<PositionLegendCell?>> _read;
    private Task? _running;
    private bool _again;
    private bool _pending;
    private bool _disposed;

    /// <summary>Creates the service and subscribes to its inputs.</summary>
    /// <param name="loader">The loaded chart cells.</param>
    /// <param name="datasets">The loaded datasets: names, visibility and stack order.</param>
    /// <param name="viewport">The map view; null in hosts without a map.</param>
    /// <param name="mariner">The Mariner settings: depth unit and safety contour.</param>
    /// <param name="depthBasis">The status bar's depth basis, for the datum when no cell is at the centre; optional.</param>
    /// <param name="legend">The legend, whose rebuilds (pan end, the layer stack) this follows.</param>
    /// <param name="post">Schedules work on the UI thread (the app passes <see cref="UiThread.PostBackground"/>).</param>
    /// <param name="read">Runs the cell read (the app passes <see cref="Task.Run{TResult}(Func{TResult})"/>).</param>
    public PositionLegendService(
        IDatasetLoaderService loader,
        DatasetsViewModel datasets,
        IMapViewportNotifier? viewport,
        IMarinerSettingsProvider mariner,
        DepthBasisService? depthBasis,
        LegendService legend,
        Action<Action> post,
        Func<Func<PositionLegendCell?>, Task<PositionLegendCell?>> read)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(datasets);
        ArgumentNullException.ThrowIfNull(mariner);
        ArgumentNullException.ThrowIfNull(legend);
        ArgumentNullException.ThrowIfNull(post);
        ArgumentNullException.ThrowIfNull(read);
        _loader = loader;
        _datasets = datasets;
        _viewport = viewport;
        _mariner = mariner;
        _depthBasis = depthBasis;
        _legend = legend;
        _post = post;
        _read = read;
        Current = Build(cell: null, view: null);

        _legend.Changed += OnInputChanged;
        _mariner.Changed += OnMarinerChanged;
        if (_depthBasis is not null)
            _depthBasis.Changed += OnInputChanged;
        Invalidate();
    }

    /// <summary>The legend as last read.</summary>
    public PositionLegend Current { get; private set; }

    /// <summary>Raised on the UI thread when <see cref="Current"/> changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Schedules a read on the next UI frame; calls before it runs collapse into one.</summary>
    public void Invalidate()
    {
        if (_disposed || _pending)
            return;
        _pending = true;
        _post(() =>
        {
            _pending = false;
            if (!_disposed)
                _ = RefreshAsync();
        });
    }

    /// <summary>
    /// Reads the view centre now and completes once <see cref="Current"/>
    /// shows it; MCP <c>get_legend</c> awaits it. Call on the UI thread.
    /// </summary>
    public Task RefreshAsync()
    {
        if (_running is { IsCompleted: false } running)
        {
            // A read is under way for an older view: read again after it.
            _again = true;
            return running;
        }
        _running = RunAsync();
        return _running;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _legend.Changed -= OnInputChanged;
        _mariner.Changed -= OnMarinerChanged;
        if (_depthBasis is not null)
            _depthBasis.Changed -= OnInputChanged;
    }

    private async Task RunAsync()
    {
        do
        {
            _again = false;
            var view = _viewport?.Current;
            PositionLegendCell? cell = null;
            if (Centre(view) is var (latitude, longitude))
            {
                var processors = _loader.AcquireProcessors();
                try
                {
                    var candidates = Candidates(processors);
                    cell = candidates.Count == 0 ? null : await _read(() => CellAt(candidates, latitude, longitude));
                }
                catch (Exception)
                {
                    // A cell whose features can't be read leaves the centre without a chart.
                    cell = null;
                }
                finally
                {
                    processors.Dispose();
                }
            }

            if (_disposed)
                return;
            var next = Build(cell, view);
            if (next != Current)
            {
                Current = next;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
        while (_again && !_disposed);
    }

    /// <summary>The drawn chart cells, top of the Datasets list first.</summary>
    private List<(DatasetEntry Entry, IChartDepthCell Cell)> Candidates(DatasetProcessorSnapshot processors)
    {
        var order = new Dictionary<DatasetEntry, int>();
        for (var i = 0; i < _datasets.Entries.Count; i++)
            order.TryAdd(_datasets.Entries[i], i);

        var cells = new List<(DatasetEntry Entry, IChartDepthCell Cell)>();
        foreach (var (entry, processor) in processors)
        {
            if (processor is IChartDepthCell cell && entry.IsVisible && _loader.GetActive(entry.Id.Value))
                cells.Add((entry, cell));
        }
        cells.Sort((a, b) => Order(a.Entry).CompareTo(Order(b.Entry)));
        return cells;

        int Order(DatasetEntry entry) => order.TryGetValue(entry, out var index) ? index : int.MaxValue;
    }

    /// <summary>The finest cell covering the point, the topmost of peers, with what the legend shows of it.</summary>
    internal static PositionLegendCell? CellAt(
        IReadOnlyList<(DatasetEntry Entry, IChartDepthCell Cell)> cells, double latitude, double longitude)
    {
        if (LocationDepthProbe.FinestCoveringCells(cells.Select(c => c.Cell), latitude, longitude) is not { } finest)
            return null;
        foreach (var (entry, cell) in cells)
        {
            if (!finest.Contains(cell))
                continue;
            return new PositionLegendCell(
                entry.Id.Value,
                entry.DisplayName,
                entry.ProductSpec,
                Safe(() => cell.Edition),
                Safe(() => cell.CompilationScale),
                Safe(() => cell.SoundingDatumAt(latitude, longitude) ?? cell.SoundingDatum));
        }
        return null;
    }

    private PositionLegend Build(PositionLegendCell? cell, MapViewportSnapshot? view)
    {
        var mariner = _mariner.Current;
        var centre = Centre(view);
        double? scale = centre is var (latitude, _) && view is not null
            ? MapScaleFormatter.ResolutionToScaleDenominator(view.MercatorResolution, latitude)
            : null;

        int? datum;
        bool hasDatum;
        if (cell is not null)
        {
            datum = cell.DatumCode;
            hasDatum = true;
        }
        else if (_depthBasis?.Current.DepthSources is { Count: > 0 } sources
                 && sources.Select(s => s.VerticalDatumCode).Distinct().Count() == 1)
        {
            datum = sources[0].VerticalDatumCode;
            hasDatum = datum is not null;
        }
        else
        {
            datum = null;
            hasDatum = false;
        }

        return new PositionLegend(
            centre?.Latitude,
            centre?.Longitude,
            scale,
            mariner.DepthUnit,
            mariner.SafetyContour.TotalMetres,
            datum,
            hasDatum,
            cell);
    }

    /// <summary>The centre of the view, its longitude brought into −180…180 (world copies, #773).</summary>
    private static (double Latitude, double Longitude)? Centre(MapViewportSnapshot? view)
    {
        if (view is null)
            return null;
        var latitude = (view.MinLatitude + view.MaxLatitude) / 2.0;
        var longitude = (view.MinLongitude + view.MaxLongitude) / 2.0;
        longitude = ((longitude + 180.0) % 360.0 + 360.0) % 360.0 - 180.0;
        return double.IsFinite(latitude) && double.IsFinite(longitude) ? (latitude, longitude) : null;
    }

    private static T? Safe<T>(Func<T?> read)
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            // A cell whose features can't be read declares nothing.
            return default;
        }
    }

    private void OnInputChanged(object? sender, EventArgs e) => Invalidate();

    private void OnMarinerChanged(MarinerSettings settings) => Invalidate();
}
