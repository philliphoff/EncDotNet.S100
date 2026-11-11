using EncDotNet.S100.DynamicSources.Ndbc;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Renderers.Mapsui.DynamicSources;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EncDotNet.S100.Viewer.Services.DynamicSources.Ndbc;

/// <summary>
/// The viewer's side of the NDBC observations overlay (#1070): owns the
/// <see cref="NdbcObservationSource"/> and its <see cref="HttpClient"/>, feeds
/// it the map viewport (debounced), and binds its
/// <see cref="NdbcObservationSource.IsActive"/> to the layer-stack visibility
/// toggle, so a hidden layer makes no requests.
/// </summary>
/// <remarks>
/// The overlay starts hidden: <see cref="SeedDefaultVisibility"/> turns it off
/// unless the user's settings already hold a choice for it. The source then
/// waits for a viewport narrower than its span gate before fetching anything.
/// </remarks>
internal sealed class NdbcObservationsOverlay : IAsyncDisposable
{
    /// <summary>The debounce between a viewport change and the area update.</summary>
    public static readonly TimeSpan DefaultViewportDebounce = TimeSpan.FromMilliseconds(300);

    private readonly HttpClient? _ownedHttpClient;
    private readonly IMapViewportNotifier? _notifier;
    private readonly TimeSpan _debounce;
    private readonly ILogger _logger;
    private readonly object _lock = new();
    private IS100DynamicSourceRegistry? _registry;
    private CancellationTokenSource? _debounceCts;
    private bool _disposed;

    /// <summary>Creates the overlay.</summary>
    /// <param name="source">The source.</param>
    /// <param name="ownedHttpClient">The source's client, disposed with the overlay; <see langword="null"/> when the caller owns it.</param>
    /// <param name="notifier">The viewport notifier; <see langword="null"/> leaves the area unset.</param>
    /// <param name="debounce">The viewport debounce; <see langword="null"/> for <see cref="DefaultViewportDebounce"/>.</param>
    /// <param name="logger">The logger.</param>
    public NdbcObservationsOverlay(
        NdbcObservationSource source,
        HttpClient? ownedHttpClient,
        IMapViewportNotifier? notifier,
        TimeSpan? debounce = null,
        ILogger<NdbcObservationsOverlay>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        Source = source;
        _ownedHttpClient = ownedHttpClient;
        _notifier = notifier;
        _debounce = debounce ?? DefaultViewportDebounce;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    /// <summary>The source.</summary>
    public NdbcObservationSource Source { get; }

    /// <summary>
    /// Hides the overlay on first run: unless <paramref name="persisted"/>
    /// already holds a choice for the source, sets it hidden in
    /// <paramref name="registry"/>. Call before the source is registered.
    /// </summary>
    /// <param name="registry">The registry.</param>
    /// <param name="persisted">The persisted per-source visibility.</param>
    public void SeedDefaultVisibility(IS100DynamicSourceRegistry registry, IReadOnlyDictionary<string, bool> persisted)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(persisted);
        if (!persisted.ContainsKey(Source.Id))
            registry.SetVisible(Source.Id, false);
    }

    /// <summary>
    /// Binds the source to <paramref name="registry"/>'s visibility and the
    /// viewport, and starts its polling loop.
    /// </summary>
    /// <param name="registry">The registry the source is registered with.</param>
    public void Attach(IS100DynamicSourceRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        lock (_lock)
        {
            if (_disposed || _registry is not null)
                return;
            _registry = registry;
        }

        registry.SourcesChanged += OnSourcesChanged;
        OnSourcesChanged();

        if (_notifier is not null)
        {
            _notifier.ViewportChanged += OnViewportChanged;
            if (_notifier.Current is { } current)
                Source.UpdateArea(ToBoundingBox(current));
        }

        Source.Start();
    }

    private void OnSourcesChanged()
    {
        var registry = _registry;
        if (registry is null || _disposed)
            return;
        Source.IsActive = registry.GetVisible(Source.Id);
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

        var area = ToBoundingBox(snapshot);
        _ = Task.Delay(_debounce, cts.Token).ContinueWith(
            t =>
            {
                if (t.IsCanceled || _disposed)
                    return;
                try
                {
                    Source.UpdateArea(area);
                }
                catch (ObjectDisposedException)
                {
                    // Shutting down.
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Updating the NDBC observation area failed.");
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);
    }

    internal static BoundingBox ToBoundingBox(MapViewportSnapshot snapshot) => new(
        southLatitude: snapshot.MinLatitude,
        westLongitude: snapshot.MinLongitude,
        northLatitude: snapshot.MaxLatitude,
        eastLongitude: snapshot.MaxLongitude);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        IS100DynamicSourceRegistry? registry;
        lock (_lock)
        {
            if (_disposed)
                return;
            _disposed = true;
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = null;
            registry = _registry;
        }

        if (registry is not null)
            registry.SourcesChanged -= OnSourcesChanged;
        if (_notifier is not null)
            _notifier.ViewportChanged -= OnViewportChanged;

        await Source.DisposeAsync().ConfigureAwait(false);
        _ownedHttpClient?.Dispose();
    }
}
