using EncDotNet.S100.DataModel;
using EncDotNet.S100.Renderers.Mapsui.DynamicSources;
using EncDotNet.S100.Viewer.Services.Depth;

namespace EncDotNet.S100.Viewer.Services.Tides;

/// <summary>
/// Binds the <see cref="TideStationSource"/> to its Layer stack toggle, the
/// map viewport, the view time and the loaded S-104 (tide predictions design
/// D4, D5), as <c>NdbcObservationsOverlay</c> does for NDBC.
/// </summary>
/// <remarks>
/// The overlay starts hidden (D4: off by default, kept after a restart).
/// While it's on and the clock is Live it's refreshed every minute, so the
/// glyphs follow the tide.
/// </remarks>
internal sealed class TideStationsOverlay : IDisposable
{
    /// <summary>The debounce between a viewport change and the refresh.</summary>
    public static readonly TimeSpan ViewportDebounce = TimeSpan.FromMilliseconds(200);

    private readonly IMapViewportNotifier? _notifier;
    private readonly IViewTime _viewTime;
    private readonly IWaterLevelCatalog _waterLevel;
    private readonly ITimer _tick;
    private readonly object _gate = new();
    private IS100DynamicSourceRegistry? _registry;
    private CancellationTokenSource? _debounce;
    private bool _disposed;

    /// <summary>Creates the overlay and its source.</summary>
    /// <param name="list">The station list.</param>
    /// <param name="notifier">The viewport notifier; <c>null</c> leaves the view unset.</param>
    /// <param name="viewTime">The view time.</param>
    /// <param name="waterLevel">The loaded water level, to leave out stations S-104 covers.</param>
    /// <param name="time">The clock of the minute tick.</param>
    public TideStationsOverlay(
        TideStationListService list,
        IMapViewportNotifier? notifier,
        IViewTime viewTime,
        IWaterLevelCatalog waterLevel,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(viewTime);
        ArgumentNullException.ThrowIfNull(waterLevel);
        ArgumentNullException.ThrowIfNull(time);
        _notifier = notifier;
        _viewTime = viewTime;
        _waterLevel = waterLevel;
        Source = new TideStationSource(list, () => viewTime.Current.UtcDateTime, CoveredByS104);
        _viewTime.Changed += OnRefreshNeeded;
        _waterLevel.Changed += OnRefreshNeeded;
        _tick = time.CreateTimer(_ => Source.Refresh(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    /// <summary>The source.</summary>
    public TideStationSource Source { get; }

    /// <summary>Whether the overlay is on.</summary>
    public bool IsVisible => _registry?.GetVisible(Source.Id) ?? Source.IsActive;

    /// <summary>Turns the overlay on or off, as its Layer stack row does.</summary>
    /// <param name="visible">On or off.</param>
    public void SetVisible(bool visible)
    {
        if (_registry is { } registry)
        {
            registry.SetVisible(Source.Id, visible);
        }
        else
        {
            Source.IsActive = visible;
        }
    }

    /// <summary>
    /// Hides the overlay on first run: unless <paramref name="persisted"/>
    /// already holds a choice for it, sets it hidden in <paramref name="registry"/>.
    /// </summary>
    /// <param name="registry">The registry.</param>
    /// <param name="persisted">The persisted per-source visibility.</param>
    public void SeedDefaultVisibility(IS100DynamicSourceRegistry registry, IReadOnlyDictionary<string, bool> persisted)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(persisted);
        if (!persisted.ContainsKey(Source.Id))
        {
            registry.SetVisible(Source.Id, false);
        }
    }

    /// <summary>Binds the source to <paramref name="registry"/>'s visibility and to the viewport.</summary>
    /// <param name="registry">The registry the source is registered with.</param>
    public void Attach(IS100DynamicSourceRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        if (_registry is not null)
        {
            return;
        }

        _registry = registry;
        registry.SourcesChanged += OnSourcesChanged;
        if (_notifier is not null)
        {
            _notifier.ViewportChanged += OnViewportChanged;
            Source.UpdateView(_notifier.Current);
        }

        OnSourcesChanged();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _debounce?.Cancel();
            _debounce?.Dispose();
        }

        _tick.Dispose();
        _viewTime.Changed -= OnRefreshNeeded;
        _waterLevel.Changed -= OnRefreshNeeded;
        if (_registry is { } registry)
        {
            registry.SourcesChanged -= OnSourcesChanged;
        }

        if (_notifier is not null)
        {
            _notifier.ViewportChanged -= OnViewportChanged;
        }
    }

    private bool CoveredByS104(GeoPosition position, DateTime utc)
    {
        if (_waterLevel.WaterLevel is not { } source)
        {
            return false;
        }

        foreach (var series in source.Locate(position))
        {
            if (!series.Source.IsPredicted && series.Start <= utc && utc <= series.End)
            {
                return true;
            }
        }

        return false;
    }

    private void OnSourcesChanged()
    {
        if (_registry is { } registry && !_disposed)
        {
            Source.IsActive = registry.GetVisible(Source.Id);
        }
    }

    private void OnRefreshNeeded(object? sender, EventArgs e)
    {
        if (Source.IsActive)
        {
            Source.Refresh();
        }
    }

    private void OnViewportChanged(object? sender, MapViewportSnapshot snapshot)
    {
        CancellationTokenSource cts;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _debounce?.Cancel();
            _debounce?.Dispose();
            _debounce = cts = new CancellationTokenSource();
        }

        _ = Task.Delay(ViewportDebounce, cts.Token).ContinueWith(
            t =>
            {
                if (!t.IsCanceled && !_disposed)
                {
                    Source.UpdateView(snapshot);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);
    }
}
