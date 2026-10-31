using System.Runtime.CompilerServices;
using Avalonia.Threading;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services.Depth;

/// <summary>
/// Owns the current <see cref="DepthBasis"/> (#1191): what the depths on the
/// chart are relative to. Recomputes it when a dataset loads, is removed or
/// changes Active, when the view time moves, when a footprint changes, when
/// chart adjustment (#1132) changes, and when the map calls
/// <see cref="Invalidate"/> on a viewport change (as it updates the overscale
/// indication); a burst of changes is coalesced into one recompute on the next
/// UI frame.
/// </summary>
/// <remarks>
/// A chart cell's datum is read from its <c>SoundingDatum</c> areas, which
/// can mean indexing the cell's features, so it is read off the UI thread
/// once per processor and cached; the basis is recomputed when it arrives.
/// </remarks>
internal sealed class DepthBasisService : IDisposable
{
    private readonly IDatasetLoaderService _loader;
    private readonly IMapViewportNotifier? _viewport;
    private readonly IViewTime _viewTime;
    private readonly IWaterLevelAdjustmentState _adjustment;
    private readonly ITimelineMapScope? _scope;
    private readonly Action<Action> _post;
    private readonly Func<Func<int?>, Task<int?>> _readDatum;
    private readonly ConditionalWeakTable<IChartDepthCell, DatumSlot> _cellDatums = new();
    private bool _pending;
    private bool _disposed;

    /// <summary>Creates the service and subscribes to its inputs.</summary>
    /// <param name="loader">The loaded datasets.</param>
    /// <param name="viewport">The map view, read at each recompute; null in hosts without a map.</param>
    /// <param name="viewTime">The Timeline's view time.</param>
    /// <param name="adjustment">Chart water level adjustment; <see cref="NoWaterLevelAdjustment"/> until #1132.</param>
    /// <param name="scope">Raises when a dataset's footprint changes; optional.</param>
    /// <param name="post">Schedules the coalesced recompute; defaults to the UI thread at background priority.</param>
    /// <param name="readDatum">Runs a chart cell's datum read; defaults to the thread pool.</param>
    public DepthBasisService(
        IDatasetLoaderService loader,
        IMapViewportNotifier? viewport,
        IViewTime viewTime,
        IWaterLevelAdjustmentState adjustment,
        ITimelineMapScope? scope = null,
        Action<Action>? post = null,
        Func<Func<int?>, Task<int?>>? readDatum = null)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(viewTime);
        ArgumentNullException.ThrowIfNull(adjustment);
        _loader = loader;
        _viewport = viewport;
        _viewTime = viewTime;
        _adjustment = adjustment;
        _scope = scope;
        _post = post ?? (a => Dispatcher.UIThread.Post(a, DispatcherPriority.Background));
        _readDatum = readDatum ?? Task.Run;

        _loader.DatasetLoaded += OnDatasetChanged;
        _loader.DatasetRemoved += OnDatasetChanged;
        _loader.ActiveChanged += OnActiveChanged;
        _loader.LayerStackChanged += Invalidate;
        _viewTime.Changed += OnInputChanged;
        _adjustment.Changed += OnInputChanged;
        if (_scope is not null)
            _scope.Changed += Invalidate;
    }

    /// <summary>The current basis; <see cref="DepthBasis.Hidden"/> until the first recompute.</summary>
    public DepthBasis Current { get; private set; } = DepthBasis.Hidden;

    /// <summary>True when an active S-104 dataset is loaded, whether or not it meets the view.</summary>
    public bool WaterLevelLoaded { get; private set; }

    /// <summary>Raised on the UI thread after each recompute.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Schedules a recompute on the next UI frame. Calls before it runs
    /// collapse into one. The map calls it on each viewport change, next to
    /// the overscale indication.
    /// </summary>
    public void Invalidate()
    {
        if (_disposed || _pending)
            return;
        _pending = true;
        _post(() =>
        {
            _pending = false;
            if (!_disposed)
                Refresh();
        });
    }

    /// <summary>Recomputes the basis now and raises <see cref="Changed"/>.</summary>
    public void Refresh()
    {
        var datasets = new List<DepthBasisDataset>();
        var waterLevelLoaded = false;
        using (var processors = _loader.AcquireProcessors())
        {
            foreach (var (entry, processor) in processors)
            {
                if (Describe(entry, processor) is not { } dataset)
                    continue;
                datasets.Add(dataset);
                waterLevelLoaded |= dataset.Active && dataset.WaterLevelKind is not null;
            }
        }

        Current = DepthBasisEvaluator.Evaluate(new DepthBasisInputs(
            datasets,
            TimelineMapScope.ViewOf(_viewport?.Current),
            _viewTime.Current,
            _adjustment));
        WaterLevelLoaded = waterLevelLoaded;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _loader.DatasetLoaded -= OnDatasetChanged;
        _loader.DatasetRemoved -= OnDatasetChanged;
        _loader.ActiveChanged -= OnActiveChanged;
        _loader.LayerStackChanged -= Invalidate;
        _viewTime.Changed -= OnInputChanged;
        _adjustment.Changed -= OnInputChanged;
        if (_scope is not null)
            _scope.Changed -= Invalidate;
    }

    private DepthBasisDataset? Describe(DatasetEntry entry, IDatasetProcessor processor)
    {
        var active = _loader.GetActive(entry.Id.Value);
        var footprint = TimelineMapScope.FootprintOf(entry);
        switch (processor)
        {
            case S102DatasetProcessor s102:
                return new DepthBasisDataset(entry.DisplayName, entry.ProductSpec, active, footprint, s102.VerticalDatum);

            case IChartDepthCell cell:
                return new DepthBasisDataset(entry.DisplayName, entry.ProductSpec, active, footprint, CellDatum(cell));

            case S104DatasetProcessor s104:
                var times = s104.AvailableTimes;
                return new DepthBasisDataset(
                    entry.DisplayName,
                    DepthBasisEvaluator.WaterLevelSpec,
                    active,
                    footprint,
                    s104.VerticalDatum,
                    times.Count > 0 ? times.Min() : null,
                    times.Count > 0 ? times.Max() : null,
                    s104.IsGriddedSurface ? WaterLevelSourceKind.Grid : WaterLevelSourceKind.Station,
                    s104.IssueTime);

            default:
                return null;
        }
    }

    /// <summary>The cell's datum once read; starts the read the first time and recomputes when it lands.</summary>
    private int? CellDatum(IChartDepthCell cell)
    {
        if (_cellDatums.TryGetValue(cell, out var slot))
            return slot.Code;

        slot = new DatumSlot();
        _cellDatums.Add(cell, slot);
        _ = ReadAsync(cell, slot);
        return slot.Code;
    }

    private async Task ReadAsync(IChartDepthCell cell, DatumSlot slot)
    {
        try
        {
            var read = _readDatum(() => cell.SoundingDatum);
            if (read.IsCompleted)
            {
                // Read inline (tests): the caller picks the code up straight away.
                slot.Code = await read.ConfigureAwait(false);
                return;
            }

            slot.Code = await read.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // A cell whose features can't be read declares no datum.
            slot.Code = null;
        }

        _post(Invalidate);
    }

    private void OnDatasetChanged(DatasetEntry entry) => Invalidate();

    private void OnActiveChanged(string datasetId) => Invalidate();

    private void OnInputChanged(object? sender, EventArgs e) => Invalidate();

    private sealed class DatumSlot
    {
        public int? Code { get; set; }
    }
}
