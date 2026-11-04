using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Navigation;

namespace EncDotNet.S100.Viewer.Services.Depth;

/// <summary>The mariner's water level adjustment setting, as the adjustment service reads it.</summary>
internal interface IWaterLevelAdjustmentSettings
{
    /// <summary>Off, or at the Timeline's view time.</summary>
    WaterLevelAdjustmentMode WaterLevelAdjustment { get; }

    /// <summary>The "Apply anyway when datum is not declared" override.</summary>
    bool WaterLevelApplyUndeclaredDatum { get; }

    /// <summary>Raised after either setting changes.</summary>
    event Action? WaterLevelAdjustmentChanged;
}

/// <summary>
/// The water level the map should add to its depths, and the hand-back once
/// the map has drawn it.
/// </summary>
internal interface IDepthAdjustmentRequests
{
    /// <summary>The adjustment to draw, or <see langword="null"/> for charted depths.</summary>
    IDepthAdjustment? Requested { get; }

    /// <summary>Raised when <see cref="Requested"/> changes.</summary>
    event EventHandler? RequestChanged;

    /// <summary>Records that the map has drawn <paramref name="adjustment"/>.</summary>
    /// <param name="adjustment">The adjustment drawn.</param>
    void MarkApplied(IDepthAdjustment? adjustment);
}

/// <summary>The loaded S-104 water level, as the route check and the pick report read it.</summary>
internal interface IWaterLevelCatalog
{
    /// <summary>The water level of the loaded datasets, or <see langword="null"/> when none is loaded.</summary>
    IWaterLevelSource? WaterLevel { get; }

    /// <summary>Changes whenever the loaded datasets change, so an adjustment built from them can be told apart.</summary>
    string Key { get; }

    /// <summary>Raised when the loaded datasets change.</summary>
    event EventHandler? Changed;
}

/// <summary>
/// <see cref="IWaterLevelCatalog"/> over the viewer's dataset catalog and the
/// route check's source cache, so the chart, the pick report and the route
/// check read one water-level model.
/// </summary>
internal sealed class ViewerWaterLevelCatalog : IWaterLevelCatalog, IDisposable
{
    private readonly ViewerDatasetCatalog _catalog;
    private readonly RouteCheckSourceCache _sources;
    private long _generation;

    /// <summary>Creates the catalog.</summary>
    /// <param name="catalog">The viewer's loaded datasets.</param>
    /// <param name="sources">The route check's source cache.</param>
    public ViewerWaterLevelCatalog(ViewerDatasetCatalog catalog, RouteCheckSourceCache sources)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(sources);
        _catalog = catalog;
        _sources = sources;
        _catalog.Changed += OnCatalogChanged;
    }

    /// <inheritdoc />
    public IWaterLevelSource? WaterLevel => _sources.Get(_catalog.Datasets).WaterLevel;

    /// <inheritdoc />
    public string Key => Interlocked.Read(ref _generation).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public void Dispose() => _catalog.Changed -= OnCatalogChanged;

    private void OnCatalogChanged(object? sender, DatasetCatalogChangedEventArgs e)
    {
        Interlocked.Increment(ref _generation);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// Chart-wide S-104 water level adjustment (#1132): builds the water level
/// the S-101 and S-102 layers add to their charted depths, from the mariner's
/// setting, the Timeline's view time and the loaded S-104, and reports what
/// the drawn layers apply to the depth basis indicator.
/// </summary>
/// <remarks>
/// <para>
/// The adjustment is requested (<see cref="Requested"/>,
/// <see cref="RequestChanged"/>) and, once the map has re-drawn with it,
/// marked applied (<see cref="MarkApplied"/>). <see cref="Current"/> only ever
/// describes an applied adjustment, so while the Timeline is dragged the
/// indicator keeps the time the drawn layers are for
/// (<c>docs/design/chart-wide-water-level-adjustment.md</c> §5).
/// </para>
/// <para>
/// The water level comes from the route check's source
/// (<see cref="RouteCheckSourceCache"/>), so the chart, the pick report and
/// the route check apply the same series at a point.
/// </para>
/// </remarks>
internal sealed class ChartWaterLevelAdjustmentService : IWaterLevelAdjustmentState, IDepthAdjustmentRequests, IDisposable
{
    private readonly IWaterLevelAdjustmentSettings _settings;
    private readonly IViewTime _viewTime;
    private readonly IWaterLevelCatalog _catalog;
    private WaterLevelDepthAdjustment? _requested;
    private IReadOnlyList<WaterLevelSourceInfo> _requestedSources = [];
    private IReadOnlyList<DataModel.CoverageArea> _requestedFootprint = [];
    private bool _disposed;

    /// <summary>Creates the service and builds the first request.</summary>
    /// <param name="settings">The mariner's setting.</param>
    /// <param name="viewTime">The Timeline's view time.</param>
    /// <param name="catalog">The loaded water level.</param>
    public ChartWaterLevelAdjustmentService(
        IWaterLevelAdjustmentSettings settings,
        IViewTime viewTime,
        IWaterLevelCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(viewTime);
        ArgumentNullException.ThrowIfNull(catalog);
        _settings = settings;
        _viewTime = viewTime;
        _catalog = catalog;

        _settings.WaterLevelAdjustmentChanged += OnSettingChanged;
        _viewTime.Changed += OnViewTimeChanged;
        _catalog.Changed += OnCatalogChanged;
        Rebuild(raise: false);
    }

    /// <inheritdoc />
    public bool IsEnabled => _settings.WaterLevelAdjustment != WaterLevelAdjustmentMode.Off;

    /// <inheritdoc />
    public ChartAdjustmentState? Current { get; private set; }

    /// <summary>
    /// The adjustment the map should draw next, or <see langword="null"/> for
    /// charted depths (adjustment off, or no S-104 loaded).
    /// </summary>
    public IDepthAdjustment? Requested => _requested;

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <summary>Raised when <see cref="Requested"/> changes and the map should re-draw its depths.</summary>
    public event EventHandler? RequestChanged;

    /// <summary>
    /// Records that the map has drawn <paramref name="adjustment"/>. Ignored
    /// unless it is still the latest request, so a superseded re-draw never
    /// reports its time.
    /// </summary>
    /// <param name="adjustment">The adjustment the map drew (the <see cref="Requested"/> it was given).</param>
    public void MarkApplied(IDepthAdjustment? adjustment)
    {
        if (_disposed || !ReferenceEquals(adjustment, _requested))
            return;

        var state = !IsEnabled
            ? null
            : new ChartAdjustmentState(
                _settings.WaterLevelAdjustment,
                _requested?.Field.Basis.Start,
                _requestedSources,
                _requestedFootprint,
                _settings.WaterLevelApplyUndeclaredDatum);
        if (state == Current)
            return;

        Current = state;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _settings.WaterLevelAdjustmentChanged -= OnSettingChanged;
        _viewTime.Changed -= OnViewTimeChanged;
        _catalog.Changed -= OnCatalogChanged;
    }

    private void OnSettingChanged()
    {
        Rebuild(raise: true);

        // Turning it off is reported at once; anything else is reported when
        // the re-drawn layers land (MarkApplied).
        if (!IsEnabled)
            Current = null;

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnViewTimeChanged(object? sender, EventArgs e)
    {
        if (IsEnabled)
            Rebuild(raise: true);
    }

    private void OnCatalogChanged(object? sender, EventArgs e) => Rebuild(raise: true);

    private void Rebuild(bool raise)
    {
        if (_disposed)
            return;

        WaterLevelDepthAdjustment? next = null;
        if (IsEnabled && _catalog.WaterLevel is { } source && source.Datasets.Count > 0)
        {
            var field = new WaterLevelSnapshot(source, _viewTime.Current.UtcDateTime);
            next = new WaterLevelDepthAdjustment(field, _settings.WaterLevelApplyUndeclaredDatum, _catalog.Key);
            _requestedSources = source.Datasets;
            _requestedFootprint = source.Footprint;
        }
        else
        {
            _requestedSources = [];
            _requestedFootprint = [];
        }

        if (next is null && _requested is null)
            return;
        if (next is not null && _requested is not null && next.Key == _requested.Key)
            return;

        _requested = next;
        if (raise)
            RequestChanged?.Invoke(this, EventArgs.Empty);
    }
}
