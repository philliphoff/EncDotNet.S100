using System.Collections.Generic;
using Avalonia.Threading;
using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.Renderers.Mapsui.DynamicSources;
using Mapsui;
using Mapsui.Layers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EncDotNet.S100.Viewer.Services.DynamicSources;

/// <summary>
/// Viewer-side glue that hosts <see cref="IDynamicFeatureSource"/>
/// instances on the map's overlay tier.
/// </summary>
/// <remarks>
/// <para>
/// For each registered source the host: (1) resolves the appropriate
/// <see cref="IDynamicFeatureRenderer"/> from DI via
/// <see cref="DynamicSourceMetadata.RendererKey"/>, falling back to
/// <see cref="DefaultDynamicFeatureRenderer"/> when the key is
/// <see langword="null"/> or unresolved; (2) attaches a backing
/// <see cref="MemoryLayer"/> to <see cref="IMapHost.AddOverlayLayer"/>;
/// (3) subscribes to <see cref="IDynamicFeatureSource.Changed"/> and
/// marshals updates onto the UI thread; and (4) rebuilds the layer's
/// features on each (debounced) change.
/// </para>
/// <para>
/// Marshalling is performed through a caller-injectable callback so
/// the host is unit-testable without spinning up Avalonia. The
/// default delegates to <see cref="Dispatcher.UIThread"/>.
/// </para>
/// <para>
/// v1 deliberately ignores the global time slider — see
/// <c>docs/design/dynamic-feature-source.md</c> §5 Q8.
/// </para>
/// </remarks>
internal sealed class DynamicSourceOverlayHost : IDisposable, IDynamicFeatureSourceRegistry
{
    private readonly IMapHost _mapHost;
    private readonly IServiceProvider _services;
    private readonly Action<Action> _marshal;
    private readonly ILogger<DynamicSourceOverlayHost> _logger;
    /// <summary>
    /// Minimum time between full layer rebuilds for a single source.
    /// PR-D3 made this matter: high-frequency sources (AIS at world
    /// scale = 10–100+ events/sec, each touching 100s of features)
    /// would otherwise pin the UI thread. The throttle is leading-
    /// edge (first event in a quiet window rebuilds immediately) plus
    /// trailing-edge (subsequent bursts collapse to one rebuild at
    /// the end of the window) so own-ship's ~1 Hz cadence still
    /// renders without perceptible delay.
    /// </summary>
    private readonly TimeSpan _coalesceWindow;
    private readonly Dictionary<string, Registration> _byId = new(StringComparer.Ordinal);
    // Registration order — preserved separately from _byId so the
    // Layer Stack panel can render sources in the order they were
    // registered (PR-D2.1 Q7). Mutated under _lock.
    private readonly List<Registration> _ordered = new();
    // Visibility map keyed by source id. Pre-seeded entries (set
    // before Register) survive a later Register call so persisted
    // visibility from ViewerSettings can be applied without a race.
    private readonly Dictionary<string, bool> _visibility = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    private static readonly DefaultDynamicFeatureRenderer s_defaultRenderer = new();

    /// <inheritdoc />
    public event Action? SourcesChanged;

    /// <summary>
    /// Creates a new overlay host.
    /// </summary>
    /// <param name="mapHost">
    /// Target map host. The host must already be initialised
    /// (basemap added) before any source is registered; layers
    /// added before initialisation are silently dropped by
    /// <see cref="MapsuiMapHost"/>.
    /// </param>
    /// <param name="services">
    /// DI container used to resolve renderers via
    /// <see cref="IKeyedServiceProvider.GetKeyedService"/>.
    /// </param>
    /// <param name="marshal">
    /// Optional override for UI-thread marshalling. Defaults to
    /// <see cref="Dispatcher.UIThread"/>. Tests inject a synchronous
    /// or test-dispatcher-backed implementation.
    /// </param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="coalesceWindow">
    /// Minimum interval between full rebuilds of a single source's
    /// layer. <see langword="null"/> uses the default 250 ms (AIS
    /// at world scale streams 10–100+ events/sec; the default keeps
    /// the UI responsive while still feeling live for own-ship's
    /// ~1 Hz cadence). Tests pass <see cref="TimeSpan.Zero"/> to
    /// disable the throttle and keep rebuilds synchronous.
    /// </param>
    public DynamicSourceOverlayHost(
        IMapHost mapHost,
        IServiceProvider services,
        Action<Action>? marshal = null,
        ILogger<DynamicSourceOverlayHost>? logger = null,
        TimeSpan? coalesceWindow = null)
    {
        ArgumentNullException.ThrowIfNull(mapHost);
        ArgumentNullException.ThrowIfNull(services);
        _mapHost = mapHost;
        _services = services;
        _marshal = marshal ?? DispatcherMarshal;
        _logger = logger ?? NullLogger<DynamicSourceOverlayHost>.Instance;
        _coalesceWindow = coalesceWindow ?? TimeSpan.FromMilliseconds(250);
    }

    /// <summary>
    /// Registers a source. Resolves
    /// <see cref="IDynamicFeatureRenderer"/> keyed by
    /// <c>source.Metadata.RendererKey</c>; falls back to the default
    /// renderer when the key is <see langword="null"/> or
    /// unregistered. The returned <see cref="IDisposable"/>
    /// unregisters the source and detaches its overlay layer when
    /// disposed.
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
            // restored before MainWindow constructed the host).
            initialVisible = !_visibility.TryGetValue(source.Id, out var v) || v;
            _visibility[source.Id] = initialVisible;
        }

        layer.Enabled = initialVisible;

        _marshal(() =>
        {
            _mapHost.AddOverlayLayer(layer);
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
            return s_defaultRenderer;
        }

        var resolved = _services.GetKeyedService<IDynamicFeatureRenderer>(rendererKey);
        if (resolved is not null) return resolved;

        _logger.LogWarning(
            "No IDynamicFeatureRenderer registered under key '{RendererKey}' for source '{SourceId}'; falling back to default renderer.",
            rendererKey,
            sourceId);
        return s_defaultRenderer;
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

    // Pure-CPU helper; safe to call off the UI thread because the
    // renderer contract is pure (no Avalonia / Mapsui state mutation
    // beyond constructing GeometryFeature/Style objects, which are
    // POCOs until added to a layer).
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

    private static void DispatcherMarshal(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }

    /// <summary>
    /// Unregisters all sources and detaches their overlay layers.
    /// Safe to call from any thread.
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
            // Still fire so subscribers re-render any stub VM rows
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
        private readonly DynamicSourceOverlayHost _host;
        private int _disposed;

        public Registration(
            IDynamicFeatureSource source,
            IDynamicFeatureRenderer renderer,
            MemoryLayer layer,
            DynamicSourceOverlayHost host)
        {
            Source = source;
            Renderer = renderer;
            Layer = layer;
            _host = host;
        }

        public void OnChanged(object? sender, DynamicFeaturesChanged e)
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            _host._marshal(HandleChangeOnUiThread);
        }

        // UI-thread only; _trailingScheduled, _backgroundInFlight, and
        // _lastRebuildUtc are not synchronised because all reads/
        // writes happen here after the _marshal hop.
        private bool _trailingScheduled;
        private bool _backgroundInFlight;
        private DateTime _lastRebuildUtc = DateTime.MinValue;

        private void HandleChangeOnUiThread()
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

        // UI thread → background thread for the heavy render loop →
        // UI thread to assign the result. Keeps long-running renders
        // (AIS at world scale: 1000s of features × multiple styles
        // each) off the UI thread so panning / zoom stay responsive.
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
                _host._mapHost.RemoveOverlayLayer(Layer);
                _host.SourcesChanged?.Invoke();
            });
        }

        internal void DisposeInternal()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            Source.Changed -= OnChanged;
            _host._marshal(() => _host._mapHost.RemoveOverlayLayer(Layer));
        }
    }
}
