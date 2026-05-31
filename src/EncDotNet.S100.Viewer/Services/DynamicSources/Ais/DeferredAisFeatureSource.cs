using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.DynamicSources.Ais;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EncDotNet.S100.Viewer.Services.DynamicSources.Ais;

/// <summary>
/// Viewer-side decorator that gates the construction of the real
/// <see cref="AisDynamicFeatureSource"/> on the visible viewport
/// shrinking below a configured span (in degrees of latitude AND
/// longitude). While the gate is closed the source is silently
/// inactive: <see cref="CurrentFeatures"/> is empty,
/// <see cref="Changed"/> never fires, and no aisstream.io traffic
/// is fetched.
/// </summary>
/// <remarks>
/// <para>
/// Once both spans of a viewport snapshot drop to or below
/// <c>activationSpanDegrees</c>, the decorator constructs the inner
/// source via the supplied factory (passing the live viewport bbox
/// as the seed) and forwards every subsequent <see cref="Changed"/>
/// event verbatim. Activation is <b>one-shot</b>: zooming back out
/// after activation does not deactivate the source — the
/// subscription stays alive for the rest of the viewer session.
/// </para>
/// <para>
/// After activation, every viewport change triggers a debounced
/// (trailing-edge) call to <see cref="AisDynamicFeatureSource.UpdateArea"/>
/// so the wire bbox tracks what the user can see. The default
/// debounce window is 250 ms — matching <c>DynamicSourceOverlayHost</c>
/// — which keeps a single pan or zoom from triggering hundreds of
/// re-subscribes.
/// </para>
/// <para>
/// See <c>docs/design/ais-zoom-gated-subscription.md</c> for the
/// full rationale (Q1–Q6).
/// </para>
/// </remarks>
internal sealed class DeferredAisFeatureSource : IDynamicFeatureSource, IAsyncDisposable
{
    /// <summary>
    /// Default debounce window for the post-activation
    /// <c>UpdateArea</c> stream. Mirrors
    /// <c>DynamicSourceOverlayHost._coalesceWindow</c>.
    /// </summary>
    public static readonly TimeSpan DefaultUpdateAreaDebounce = TimeSpan.FromMilliseconds(250);

    private readonly double _activationSpanDegrees;
    private readonly Func<BoundingBox, AisDynamicFeatureSource> _factory;
    private readonly IMapViewportNotifier _notifier;
    private readonly TimeSpan _debounce;
    private readonly ILogger<DeferredAisFeatureSource> _logger;
    private readonly object _activationLock = new();
    private volatile AisDynamicFeatureSource? _inner;
    private CancellationTokenSource? _debounceCts;
    private BoundingBox? _latestBbox;
    private bool _disposed;

    /// <summary>
    /// Constructs a new gated AIS source.
    /// </summary>
    /// <param name="id">Stable instance id (e.g. <c>"ais"</c>).</param>
    /// <param name="activationSpanDegrees">
    /// Maximum lat-span AND lon-span (degrees) at which the inner
    /// source is allowed to start. Must be positive.
    /// </param>
    /// <param name="factory">
    /// Lazy constructor for the real
    /// <see cref="AisDynamicFeatureSource"/>. Called at most once,
    /// the first time the gate opens. The argument is the live
    /// viewport bbox at the moment of activation, suitable for use
    /// as the seed <c>AisSubscriptionRequest.Area</c>.
    /// </param>
    /// <param name="notifier">Map-viewport publisher.</param>
    /// <param name="debounce">
    /// Debounce window for post-activation <c>UpdateArea</c> calls.
    /// <see langword="null"/> uses
    /// <see cref="DefaultUpdateAreaDebounce"/>; pass
    /// <see cref="TimeSpan.Zero"/> in tests to disable debouncing.
    /// </param>
    /// <param name="logger">Optional logger.</param>
    public DeferredAisFeatureSource(
        string id,
        double activationSpanDegrees,
        Func<BoundingBox, AisDynamicFeatureSource> factory,
        IMapViewportNotifier notifier,
        TimeSpan? debounce = null,
        ILogger<DeferredAisFeatureSource>? logger = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(notifier);
        if (!(activationSpanDegrees > 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(activationSpanDegrees),
                activationSpanDegrees,
                "Activation span must be a positive number of degrees.");
        }

        Id = id;
        _activationSpanDegrees = activationSpanDegrees;
        _factory = factory;
        _notifier = notifier;
        _debounce = debounce ?? DefaultUpdateAreaDebounce;
        _logger = logger ?? NullLogger<DeferredAisFeatureSource>.Instance;

        Metadata = new DynamicSourceMetadata
        {
            DisplayName = "AIS targets",
            RendererKey = "vessel.ais",
        };

        _notifier.ViewportChanged += OnViewportChanged;

        // If the notifier has already seen a viewport (e.g. binding
        // happened before this decorator was constructed), evaluate
        // the gate immediately so we don't have to wait for the next
        // user interaction to (de)activate.
        if (_notifier.Current is { } seed)
        {
            Evaluate(seed);
        }
    }

    /// <inheritdoc />
    public string Id { get; }

    /// <inheritdoc />
    public DynamicSourceMetadata Metadata { get; }

    /// <inheritdoc />
    public IReadOnlyList<DynamicFeature> CurrentFeatures =>
        _inner?.CurrentFeatures ?? Array.Empty<DynamicFeature>();

    /// <inheritdoc />
    public event EventHandler<DynamicFeaturesChanged>? Changed;

    /// <summary>
    /// <see langword="true"/> once the gate has opened and the inner
    /// source has been constructed. Used by tests; not part of the
    /// public <see cref="IDynamicFeatureSource"/> contract.
    /// </summary>
    internal bool IsActivated => _inner is not null;

    /// <summary>
    /// The seed bounding box passed to the factory at activation,
    /// or <see langword="null"/> if not yet activated. Test hook.
    /// </summary>
    internal BoundingBox? ActivationBoundingBox { get; private set; }

    private void OnViewportChanged(object? sender, MapViewportSnapshot snapshot)
    {
        if (_disposed) return;
        Evaluate(snapshot);
    }

    private void Evaluate(MapViewportSnapshot snapshot)
    {
        var bbox = ToBoundingBox(snapshot);
        if (_inner is null)
        {
            // Gate is still closed: only act if both spans satisfy
            // the threshold. We deliberately use <= so a viewport
            // exactly equal to the threshold trips the gate.
            if (snapshot.LatitudeSpanDegrees <= _activationSpanDegrees
                && snapshot.LongitudeSpanDegrees <= _activationSpanDegrees)
            {
                Activate(bbox);
            }
            return;
        }

        // Already activated — feed viewport changes to UpdateArea.
        ScheduleUpdateArea(bbox);
    }

    private void Activate(BoundingBox seedBbox)
    {
        AisDynamicFeatureSource? built = null;
        lock (_activationLock)
        {
            if (_inner is not null) return; // Lost the race.
            if (_disposed) return;

            try
            {
                built = _factory(seedBbox);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Deferred AIS source factory threw during activation.");
                return;
            }

            ActivationBoundingBox = seedBbox;
            built.Changed += OnInnerChanged;
            _inner = built;
        }

        // Surface the freshly-activated state to consumers — even an
        // empty CurrentFeatures snapshot is meaningful because layer
        // visibility, picks, etc. now have a real source to query.
        Changed?.Invoke(this, new DynamicFeaturesChanged
        {
            Kind = DynamicSourceChangeKind.Reset,
            ChangedIds = Array.Empty<string>(),
        });
    }

    private void OnInnerChanged(object? sender, DynamicFeaturesChanged e)
    {
        if (_disposed) return;
        Changed?.Invoke(this, e);
    }

    private void ScheduleUpdateArea(BoundingBox bbox)
    {
        // Capture the latest bbox under a lock so the trailing
        // continuation always sees the most recent value, even if
        // multiple events landed while a previous Task.Delay was
        // pending.
        CancellationTokenSource newCts;
        lock (_activationLock)
        {
            if (_disposed || _inner is null) return;
            _latestBbox = bbox;

            if (_debounce <= TimeSpan.Zero)
            {
                _inner.UpdateArea(bbox);
                return;
            }

            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            newCts = new CancellationTokenSource();
            _debounceCts = newCts;
        }

        _ = Task.Delay(_debounce, newCts.Token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            AisDynamicFeatureSource? inner;
            BoundingBox? pending;
            lock (_activationLock)
            {
                inner = _inner;
                pending = _latestBbox;
                if (ReferenceEquals(_debounceCts, newCts))
                {
                    _debounceCts = null;
                }
            }
            if (inner is null || pending is null || _disposed) return;
            try
            {
                inner.UpdateArea(pending);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Deferred AIS source UpdateArea threw post-activation.");
            }
        }, TaskScheduler.Default);
    }

    private static BoundingBox ToBoundingBox(MapViewportSnapshot snapshot) => new(
        southLatitude: snapshot.MinLatitude,
        westLongitude: snapshot.MinLongitude,
        northLatitude: snapshot.MaxLatitude,
        eastLongitude: snapshot.MaxLongitude);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        AisDynamicFeatureSource? inner;
        lock (_activationLock)
        {
            if (_disposed) return;
            _disposed = true;
            _notifier.ViewportChanged -= OnViewportChanged;
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = null;
            inner = _inner;
            _inner = null;
        }

        if (inner is not null)
        {
            inner.Changed -= OnInnerChanged;
            await inner.DisposeAsync().ConfigureAwait(false);
        }
    }
}
