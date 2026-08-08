using EncDotNet.S100.DynamicSources;
using Mapsui;
using Mapsui.Layers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EncDotNet.S100.Renderers.Mapsui.DynamicSources;

/// <summary>
/// Reusable host that draws <see cref="IDynamicFeatureSource"/> instances on a
/// map's overlay tier and exposes them as an <see cref="IS100DynamicSourceRegistry"/>.
/// </summary>
/// <remarks>
/// <para>
/// For each registered source the host: (1) resolves an
/// <see cref="IDynamicFeatureRenderer"/> via the caller-supplied resolver keyed
/// by <see cref="DynamicSourceMetadata.RendererKey"/>, falling back to
/// <see cref="DefaultDynamicFeatureRenderer"/> when the key is
/// <see langword="null"/> or unresolved; (2) attaches a backing
/// <see cref="MemoryLayer"/> to <see cref="IMapsuiOverlayLayerHost.AddOverlayLayer"/>;
/// (3) subscribes to <see cref="IDynamicFeatureSource.Changed"/> and marshals
/// updates onto the map thread; and (4) rebuilds the layer's features on each
/// (debounced) change.
/// </para>
/// <para>
/// This is the reusable extraction of the Viewer's dynamic-source overlay glue
/// (issue #512, step 8). It depends only on Mapsui and the renderer-neutral
/// <see cref="IDynamicFeatureSource"/> contract — not on Avalonia, a view model,
/// or a DI container:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <b>Marshalling</b> is a caller-injected <see cref="Action{T}"/>. The default
/// runs inline (synchronously), suitable for headless or single-threaded hosts;
/// a UI host passes a dispatcher marshal (e.g. one that posts to the UI thread).
/// </description></item>
/// <item><description>
/// <b>Renderer resolution</b> is a caller-injected
/// <see cref="Func{T, TResult}"/> so the host needs no
/// <c>IServiceProvider</c>; a DI host passes a resolver over its keyed services.
/// </description></item>
/// </list>
/// <para>
/// v1 deliberately ignores the global time slider.
/// </para>
/// </remarks>
public sealed class S100DynamicSourceHost : IDisposable, IS100DynamicSourceRegistry
{
    private readonly IMapsuiOverlayLayerHost _overlayHost;
    private readonly Func<string?, IDynamicFeatureRenderer?> _rendererResolver;
    private readonly Action<Action> _marshal;
    private readonly ILogger<S100DynamicSourceHost> _logger;
    /// <summary>
    /// Minimum time between full layer rebuilds for a single source.
    /// High-frequency sources (AIS at world scale = 10–100+ events/sec,
    /// each touching 100s of features) would otherwise pin the map
    /// thread. The throttle is leading-edge (first event in a quiet
    /// window rebuilds immediately) plus trailing-edge (subsequent
    /// bursts collapse to one rebuild at the end of the window) so
    /// own-ship's ~1 Hz cadence still renders without perceptible delay.
    /// </summary>
    private readonly TimeSpan _coalesceWindow;
    private readonly Dictionary<string, Registration> _byId = new(StringComparer.Ordinal);
    // Registration order — preserved separately from _byId so a host
    // can render sources in the order they were registered. Mutated
    // under _lock.
    private readonly List<Registration> _ordered = new();
    // Visibility map keyed by source id. Pre-seeded entries (set
    // before Register) survive a later Register call so persisted
    // visibility can be applied without a race.
    private readonly Dictionary<string, bool> _visibility = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    private static readonly DefaultDynamicFeatureRenderer DefaultRenderer = new();

    /// <inheritdoc />
    public event Action? SourcesChanged;

    /// <summary>
    /// Creates a new dynamic-source host.
    /// </summary>
    /// <param name="overlayHost">
    /// Target overlay band. The map must already be initialised (basemap added)
    /// before any source is registered when the host's overlay-insert policy
    /// depends on it.
    /// </param>
    /// <param name="rendererResolver">
    /// Resolves an <see cref="IDynamicFeatureRenderer"/> for a source's
    /// <see cref="DynamicSourceMetadata.RendererKey"/>. Returns
    /// <see langword="null"/> to fall back to the default renderer.
    /// <see langword="null"/> for the whole delegate always uses the default
    /// renderer.
    /// </param>
    /// <param name="marshal">
    /// Optional marshal onto the map thread. Defaults to inline (synchronous)
    /// execution. A UI host passes a dispatcher-backed marshal.
    /// </param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="coalesceWindow">
    /// Minimum interval between full rebuilds of a single source's layer.
    /// <see langword="null"/> uses the default 250 ms. Pass
    /// <see cref="TimeSpan.Zero"/> to disable the throttle and keep rebuilds
    /// synchronous (used by tests).
    /// </param>
    public S100DynamicSourceHost(
        IMapsuiOverlayLayerHost overlayHost,
        Func<string?, IDynamicFeatureRenderer?>? rendererResolver = null,
        Action<Action>? marshal = null,
        ILogger<S100DynamicSourceHost>? logger = null,
        TimeSpan? coalesceWindow = null)
    {
        ArgumentNullException.ThrowIfNull(overlayHost);
        _overlayHost = overlayHost;
        _rendererResolver = rendererResolver ?? (static _ => null);
        _marshal = marshal ?? (static action => action());
        _logger = logger ?? NullLogger<S100DynamicSourceHost>.Instance;
        _coalesceWindow = coalesceWindow ?? TimeSpan.FromMilliseconds(250);
    }

    /// <summary>
    /// Registers a source. Resolves <see cref="IDynamicFeatureRenderer"/> keyed
    /// by <c>source.Metadata.RendererKey</c>; falls back to the default renderer
    /// when the key is <see langword="null"/> or unresolved. The returned
    /// <see cref="IDisposable"/> unregisters the source and detaches its overlay
    /// layer when disposed.
    /// </summary>
    public IDisposable Register(IDynamicFeatureSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var renderer = ResolveRenderer(source.Metadata.RendererKey, source.Id);
        var layer = new MemoryLayer
        {
            Name = $"Dynamic Source: {source.Metadata.DisplayName}",
            Style = null,
            Features = new List<IFeature>(),
        };

        var registration = new Registration(source, renderer, layer, this);
        bool initialVisible;

        lock (_lock)
        {
            if (_byId.ContainsKey(source.Id))
            {
                throw new InvalidOperationException(
                    $"A dynamic feature source with id '{source.Id}' is already registered.");
            }
            _byId[source.Id] = registration;
            _ordered.Add(registration);

            // Apply any pre-seeded visibility (e.g. from settings
            // restored before the host was constructed).
            initialVisible = !_visibility.TryGetValue(source.Id, out var v) || v;
            _visibility[source.Id] = initialVisible;
        }

        layer.Enabled = initialVisible;

        _marshal(() =>
        {
            _overlayHost.AddOverlayLayer(layer);
            Rebuild(registration);
            SourcesChanged?.Invoke();
        });

        source.Changed += registration.OnChanged;
        return registration;
    }

    private IDynamicFeatureRenderer ResolveRenderer(string? rendererKey, string sourceId)
    {
        if (string.IsNullOrEmpty(rendererKey))
        {
            return DefaultRenderer;
        }

        var resolved = _rendererResolver(rendererKey);
        if (resolved is not null) return resolved;

        _logger.LogWarning(
            "No IDynamicFeatureRenderer resolved for key '{RendererKey}' (source '{SourceId}'); falling back to default renderer.",
            rendererKey,
            sourceId);
        return DefaultRenderer;
    }

    private void Rebuild(Registration registration)
    {
        // Synchronous rebuild — used for the initial Register() build
        // and by tests with throttle disabled. Cheap when the source
        // has few features (own-ship == 1).
        var features = RenderSnapshot(registration);
        registration.Layer.Features = features;
        registration.Layer.DataHasChanged();
    }

    // Pure-CPU helper; safe to call off the map thread because the
    // renderer contract is pure (no Mapsui state mutation beyond
    // constructing GeometryFeature/Style objects, which are POCOs
    // until added to a layer).
    private static List<IFeature> RenderSnapshot(Registration registration)
    {
        var snapshot = registration.Source.CurrentFeatures;
        var features = new List<IFeature>(snapshot.Count);
        foreach (var feature in snapshot)
        {
            if (!registration.Renderer.CanRender(feature)) continue;
            foreach (var rendered in registration.Renderer.Render(feature))
            {
                features.Add(rendered);
            }
        }
        return features;
    }

    /// <summary>
    /// Unregisters all sources and detaches their overlay layers. Safe to call
    /// from any thread.
    /// </summary>
    public void Dispose()
    {
        Registration[] regs;
        lock (_lock)
        {
            regs = _ordered.ToArray();
            _byId.Clear();
            _ordered.Clear();
        }
        foreach (var r in regs) r.DisposeInternal();
        SourcesChanged?.Invoke();
    }

    /// <inheritdoc />
    public IReadOnlyList<DynamicSourceRegistrationInfo> Sources
    {
        get
        {
            lock (_lock)
            {
                var list = new List<DynamicSourceRegistrationInfo>(_ordered.Count);
                foreach (var r in _ordered)
                {
                    list.Add(new DynamicSourceRegistrationInfo(
                        Id: r.Source.Id,
                        DisplayName: r.Source.Metadata.DisplayName,
                        Description: r.Source.Metadata.Description));
                }
                return list;
            }
        }
    }

    /// <inheritdoc />
    public bool GetVisible(string sourceId)
    {
        ArgumentNullException.ThrowIfNull(sourceId);
        lock (_lock)
        {
            return !_visibility.TryGetValue(sourceId, out var v) || v;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<IDynamicFeatureSource> GetVisibleSourceInstances()
    {
        lock (_lock)
        {
            var list = new List<IDynamicFeatureSource>(_ordered.Count);
            foreach (var r in _ordered)
            {
                // Default to visible when no entry exists, matching
                // GetVisible's contract.
                if (_visibility.TryGetValue(r.Source.Id, out var v) && !v) continue;
                list.Add(r.Source);
            }
            return list;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<DynamicSourceHit> HitTest(MPoint mapPoint, double resolution)
    {
        ArgumentNullException.ThrowIfNull(mapPoint);

        var sources = GetVisibleSourceInstances();
        if (sources.Count == 0) return Array.Empty<DynamicSourceHit>();

        return DynamicSourceHitTester.HitTest(mapPoint, resolution, sources);
    }

    /// <inheritdoc />
    public void SetVisible(string sourceId, bool visible)
    {
        ArgumentNullException.ThrowIfNull(sourceId);

        Registration? registration;
        bool changed;
        lock (_lock)
        {
            var hadEntry = _visibility.TryGetValue(sourceId, out var current);
            changed = !hadEntry || current != visible;
            if (changed) _visibility[sourceId] = visible;
            _byId.TryGetValue(sourceId, out registration);
        }

        if (!changed) return;

        if (registration is not null)
        {
            _marshal(() =>
            {
                registration.Layer.Enabled = visible;
                registration.Layer.DataHasChanged();
                SourcesChanged?.Invoke();
            });
        }
        else
        {
            // Source not registered yet (seeding from settings).
            // Still fire so subscribers re-render any stub rows
            // that key off seeded values.
            SourcesChanged?.Invoke();
        }
    }

    /// <summary>Captured registration for one source.</summary>
    private sealed class Registration : IDisposable
    {
        public IDynamicFeatureSource Source { get; }
        public IDynamicFeatureRenderer Renderer { get; }
        public MemoryLayer Layer { get; }
        private readonly S100DynamicSourceHost _host;
        private int _disposed;

        public Registration(
            IDynamicFeatureSource source,
            IDynamicFeatureRenderer renderer,
            MemoryLayer layer,
            S100DynamicSourceHost host)
        {
            Source = source;
            Renderer = renderer;
            Layer = layer;
            _host = host;
        }

        public void OnChanged(object? sender, DynamicFeaturesChanged e)
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            _host._marshal(HandleChangeOnMapThread);
        }

        // Map-thread only; _trailingScheduled, _backgroundInFlight, and
        // _lastRebuildUtc are not synchronised because all reads/
        // writes happen here after the _marshal hop.
        private bool _trailingScheduled;
        private bool _backgroundInFlight;
        private DateTime _lastRebuildUtc = DateTime.MinValue;

        private void HandleChangeOnMapThread()
        {
            if (Volatile.Read(ref _disposed) != 0) return;

            var window = _host._coalesceWindow;
            if (window <= TimeSpan.Zero)
            {
                // Synchronous path for tests and the initial seed.
                _host.Rebuild(this);
                _lastRebuildUtc = DateTime.UtcNow;
                return;
            }

            var elapsed = DateTime.UtcNow - _lastRebuildUtc;
            if (elapsed >= window && !_backgroundInFlight)
            {
                ScheduleBackgroundRebuild();
                return;
            }

            // Inside an active window or rebuild already running.
            // Collapse this event into the trailing rebuild.
            if (_trailingScheduled) return;
            _trailingScheduled = true;
            var delay = elapsed >= window ? TimeSpan.Zero : window - elapsed;
            _ = Task.Delay(delay).ContinueWith(_ =>
                _host._marshal(() =>
                {
                    _trailingScheduled = false;
                    if (Volatile.Read(ref _disposed) != 0) return;
                    if (_backgroundInFlight)
                    {
                        // Re-queue once the in-flight rebuild lands —
                        // its completion will check this flag.
                        _trailingScheduled = true;
                        return;
                    }
                    ScheduleBackgroundRebuild();
                }), TaskScheduler.Default);
        }

        // Map thread → background thread for the heavy render loop →
        // map thread to assign the result. Keeps long-running renders
        // (AIS at world scale: 1000s of features × multiple styles
        // each) off the map thread so panning / zoom stay responsive.
        private void ScheduleBackgroundRebuild()
        {
            _backgroundInFlight = true;
            _lastRebuildUtc = DateTime.UtcNow;
            _ = Task.Run(() =>
            {
                List<IFeature>? features = null;
                try
                {
                    if (Volatile.Read(ref _disposed) == 0)
                    {
                        features = RenderSnapshot(this);
                    }
                }
                catch (Exception ex)
                {
                    _host._logger.LogError(
                        ex,
                        "Dynamic source '{SourceId}' renderer threw during rebuild.",
                        Source.Id);
                }
                _host._marshal(() =>
                {
                    _backgroundInFlight = false;
                    if (Volatile.Read(ref _disposed) != 0) return;
                    if (features is not null)
                    {
                        Layer.Features = features;
                        Layer.DataHasChanged();
                        _lastRebuildUtc = DateTime.UtcNow;
                    }
                    if (_trailingScheduled)
                    {
                        // A burst event landed mid-rebuild and was
                        // deferred above; honour it now.
                        _trailingScheduled = false;
                        ScheduleBackgroundRebuild();
                    }
                });
            });
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            Source.Changed -= OnChanged;
            lock (_host._lock)
            {
                _host._byId.Remove(Source.Id);
                _host._ordered.Remove(this);
            }
            _host._marshal(() =>
            {
                _host._overlayHost.RemoveOverlayLayer(Layer);
                _host.SourcesChanged?.Invoke();
            });
        }

        internal void DisposeInternal()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            Source.Changed -= OnChanged;
            _host._marshal(() => _host._overlayHost.RemoveOverlayLayer(Layer));
        }
    }
}
