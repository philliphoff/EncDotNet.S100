using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EncDotNet.S100.Viewer.Services.Sources;

/// <summary>
/// Runs one hosted source processor for SoundCharts: starts it, tells it the
/// area in view (after the view settles), and pauses it unless some map both
/// shows its dataset and has a view within its <see cref="SourceActivation"/>.
/// The one wrapper every source shares (design note D4), in place of a
/// gating overlay per source.
/// </summary>
/// <remarks>
/// It serves every map in the window, not just the active one (split map,
/// #1730): the source runs while some map that shows it (split map step 2
/// §2.5) is within its activation range, and the area it's told is the box
/// around those maps' views, so a map the user isn't working in still gets
/// its traffic, and a map that hides the source doesn't widen the area.
/// </remarks>
internal sealed class HostedSourceActivator : IDisposable
{
    /// <summary>How long the view must settle before the source is told the new area.</summary>
    public static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(300);

    private readonly IHostedDatasetProcessor _processor;
    private readonly SourceActivation _activation;
    private readonly IMapViewportSet? _viewports;
    private readonly TimeSpan _debounce;
    private readonly ILogger _logger;
    private readonly object _lock = new();
    private CancellationTokenSource? _debounceCts;
    private readonly Func<MapViewId?, bool> _isShownIn;
    private IReadOnlyList<(MapViewId View, GeographicBounds Bounds)> _views = [];
    private bool _started;
    private bool _disposed;

    /// <summary>Creates an activator and applies the current view at once.</summary>
    /// <param name="processor">The source's processor; the activator doesn't own it.</param>
    /// <param name="activation">When the source runs.</param>
    /// <param name="viewports">Every map's view, or <see langword="null"/> when there's no map.</param>
    /// <param name="isShownIn">
    /// Whether a map shows the dataset; asked with <see langword="null"/> when
    /// there's no map at all, meaning shown in any. It's read on the thread
    /// that applies a view, so it must be safe to call from any thread. Call
    /// <see cref="Refresh"/> when its answers change.
    /// </param>
    /// <param name="debounce">How long the view must settle; <see cref="DefaultDebounce"/> when <see langword="null"/>.</param>
    /// <param name="logger">The logger.</param>
    public HostedSourceActivator(
        IHostedDatasetProcessor processor,
        SourceActivation activation,
        IMapViewportSet? viewports,
        Func<MapViewId?, bool> isShownIn,
        TimeSpan? debounce = null,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(processor);
        ArgumentNullException.ThrowIfNull(activation);
        ArgumentNullException.ThrowIfNull(isShownIn);
        _processor = processor;
        _activation = activation;
        _viewports = viewports;
        _debounce = debounce ?? DefaultDebounce;
        _logger = logger ?? NullLogger.Instance;
        _isShownIn = isShownIn;
        _views = viewports is null ? [] : ToBounds(viewports.Current);

        if (_viewports is not null)
            _viewports.ViewportsChanged += OnViewportsChanged;
        Apply(updateArea: true);
    }

    /// <summary>Whether the source is running: shown in a map whose view is within its activation range.</summary>
    public bool IsRunning => State == SourceRunState.Running;

    /// <summary>Why the source is or isn't running.</summary>
    public SourceRunState State { get; private set; } = SourceRunState.Hidden;

    /// <summary>Raised after <see cref="State"/> changes, on the thread that changed it.</summary>
    public event EventHandler? StateChanged;

    /// <summary>
    /// Asks the predicate again after the dataset was shown or hidden in some
    /// map: a source no showing map is in range of pauses, and a running one
    /// is told the area of the maps that show it now.
    /// </summary>
    public void Refresh() => Apply(updateArea: true);

    private void OnViewportsChanged(object? sender, IReadOnlyList<MapViewViewport> viewports)
    {
        CancellationTokenSource cts;
        lock (_lock)
        {
            if (_disposed)
                return;
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = cts = new CancellationTokenSource();
        }

        var bounds = ToBounds(viewports);
        _ = Task.Delay(_debounce, cts.Token).ContinueWith(
            t =>
            {
                if (t.IsCanceled)
                    return;
                lock (_lock)
                {
                    // A newer change replaced this one: with no debounce its
                    // delay had already ended when it was cancelled, and its
                    // continuation can run after the newer one's.
                    if (_disposed || !ReferenceEquals(_debounceCts, cts))
                        return;
                    _views = bounds;
                }
                Apply(updateArea: true);
            },
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);
    }

    private void Apply(bool updateArea)
    {
        var changed = false;
        try
        {
            lock (_lock)
            {
                if (_disposed)
                    return;
                var shown = ShownViews(out var anyShown);
                var area = AreaInRange(_activation, shown, out var inRange);
                var active = anyShown && inRange;
                if (updateArea && active)
                    _processor.UpdateArea(area);
                _processor.IsActive = active;
                if (active && !_started)
                {
                    _processor.Start();
                    _started = true;
                }
                var state = !anyShown ? SourceRunState.Hidden
                    : !inRange ? SourceRunState.WaitingForZoom
                    : SourceRunState.Running;
                changed = state != State;
                State = state;
            }
        }
        catch (ObjectDisposedException)
        {
            // The dataset was removed meanwhile.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Updating a data source for the view failed.");
        }
        if (changed)
            StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The views of the maps that show the dataset. With no map at all, the
    /// list is empty and <paramref name="anyShown"/> is the answer for
    /// "shown in any". Called under <see cref="_lock"/>.
    /// </summary>
    private List<GeographicBounds> ShownViews(out bool anyShown)
    {
        var shown = new List<GeographicBounds>(_views.Count);
        if (_views.Count == 0)
        {
            anyShown = _isShownIn(null);
            return shown;
        }

        foreach (var (view, bounds) in _views)
        {
            if (_isShownIn(view))
                shown.Add(bounds);
        }

        anyShown = shown.Count > 0;
        return shown;
    }

    /// <summary>
    /// The area to tell a source: the box around the views within its
    /// activation range. With no views at all, the source is in range only
    /// when it always runs, and the area is unknown (<see langword="null"/>).
    /// </summary>
    /// <param name="activation">When the source runs.</param>
    /// <param name="views">The views of the maps that show the source.</param>
    /// <param name="inRange">Whether any view (or, with none, no view) is within range.</param>
    /// <returns>The area, or <see langword="null"/> when there's no view in range.</returns>
    internal static GeographicBounds? AreaInRange(
        SourceActivation activation, IReadOnlyList<GeographicBounds> views, out bool inRange)
    {
        if (views.Count == 0)
        {
            inRange = activation.IsActiveFor(null);
            return null;
        }

        GeographicBounds? area = null;
        foreach (var view in views)
        {
            if (!activation.IsActiveFor(view))
                continue;
            area = area is { } a
                ? new GeographicBounds(
                    Math.Min(a.MinLongitude, view.MinLongitude),
                    Math.Min(a.MinLatitude, view.MinLatitude),
                    Math.Max(a.MaxLongitude, view.MaxLongitude),
                    Math.Max(a.MaxLatitude, view.MaxLatitude))
                : view;
        }

        inRange = area is not null;
        return area;
    }

    private static (MapViewId View, GeographicBounds Bounds)[] ToBounds(IReadOnlyList<MapViewViewport> viewports) =>
        viewports.Select(v => (v.View, ToBounds(v.Snapshot))).ToArray();

    internal static GeographicBounds ToBounds(MapViewportSnapshot snapshot) => new(
        snapshot.MinLongitude,
        snapshot.MinLatitude,
        snapshot.MaxLongitude,
        snapshot.MaxLatitude);

    /// <summary>Stops following the view. The processor is disposed by its owner.</summary>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            _disposed = true;
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = null;
        }
        if (_viewports is not null)
            _viewports.ViewportsChanged -= OnViewportsChanged;
    }
}

/// <summary>Why a hosted source is or isn't running.</summary>
internal enum SourceRunState
{
    /// <summary>Its dataset is hidden in every map, so it's paused.</summary>
    Hidden,

    /// <summary>Every view that shows it is wider than its activation range, so it's paused.</summary>
    WaitingForZoom,

    /// <summary>It's running.</summary>
    Running,
}
