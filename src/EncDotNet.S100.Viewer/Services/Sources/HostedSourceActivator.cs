using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EncDotNet.S100.Viewer.Services.Sources;

/// <summary>
/// Runs one hosted source processor for SoundCharts: starts it, tells it the
/// area in view (after the view settles), and pauses it while its dataset is
/// hidden or the view is wider than its <see cref="SourceActivation"/> allows.
/// The one wrapper every source shares (design note D4), in place of a
/// gating overlay per source.
/// </summary>
internal sealed class HostedSourceActivator : IDisposable
{
    /// <summary>How long the view must settle before the source is told the new area.</summary>
    public static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(300);

    private readonly IHostedDatasetProcessor _processor;
    private readonly SourceActivation _activation;
    private readonly IMapViewportNotifier? _notifier;
    private readonly TimeSpan _debounce;
    private readonly ILogger _logger;
    private readonly object _lock = new();
    private CancellationTokenSource? _debounceCts;
    private GeographicBounds? _viewport;
    private bool _visible;
    private bool _started;
    private bool _disposed;

    /// <summary>Creates an activator and applies the current view at once.</summary>
    /// <param name="processor">The source's processor; the activator doesn't own it.</param>
    /// <param name="activation">When the source runs.</param>
    /// <param name="notifier">The map's view, or <see langword="null"/> when there's no map.</param>
    /// <param name="visible">Whether the dataset is shown now.</param>
    /// <param name="debounce">How long the view must settle; <see cref="DefaultDebounce"/> when <see langword="null"/>.</param>
    /// <param name="logger">The logger.</param>
    public HostedSourceActivator(
        IHostedDatasetProcessor processor,
        SourceActivation activation,
        IMapViewportNotifier? notifier,
        bool visible,
        TimeSpan? debounce = null,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(processor);
        ArgumentNullException.ThrowIfNull(activation);
        _processor = processor;
        _activation = activation;
        _notifier = notifier;
        _debounce = debounce ?? DefaultDebounce;
        _logger = logger ?? NullLogger.Instance;
        _visible = visible;
        _viewport = notifier?.Current is { } current ? ToBounds(current) : null;

        if (_notifier is not null)
            _notifier.ViewportChanged += OnViewportChanged;
        Apply(updateArea: true);
    }

    /// <summary>Whether the source is running: shown and within its activation range.</summary>
    public bool IsRunning
    {
        get
        {
            lock (_lock)
                return _started && _processor.IsActive;
        }
    }

    /// <summary>Shows or hides the dataset; a hidden source pauses.</summary>
    /// <param name="visible">Whether the dataset is shown.</param>
    public void SetVisible(bool visible)
    {
        lock (_lock)
        {
            if (_disposed || _visible == visible)
                return;
            _visible = visible;
        }
        Apply(updateArea: false);
    }

    private void OnViewportChanged(object? sender, MapViewportSnapshot snapshot)
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

        var bounds = ToBounds(snapshot);
        _ = Task.Delay(_debounce, cts.Token).ContinueWith(
            t =>
            {
                if (t.IsCanceled)
                    return;
                lock (_lock)
                {
                    if (_disposed)
                        return;
                    _viewport = bounds;
                }
                Apply(updateArea: true);
            },
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);
    }

    private void Apply(bool updateArea)
    {
        try
        {
            lock (_lock)
            {
                if (_disposed)
                    return;
                var active = _visible && _activation.IsActiveFor(_viewport);
                if (updateArea && active)
                    _processor.UpdateArea(_viewport);
                _processor.IsActive = active;
                if (active && !_started)
                {
                    _processor.Start();
                    _started = true;
                }
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
    }

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
        if (_notifier is not null)
            _notifier.ViewportChanged -= OnViewportChanged;
    }
}
