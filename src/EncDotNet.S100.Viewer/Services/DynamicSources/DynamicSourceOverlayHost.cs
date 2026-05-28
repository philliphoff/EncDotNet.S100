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
internal sealed class DynamicSourceOverlayHost : IDisposable
{
    private readonly IMapHost _mapHost;
    private readonly IServiceProvider _services;
    private readonly Action<Action> _marshal;
    private readonly ILogger<DynamicSourceOverlayHost> _logger;
    private readonly Dictionary<string, Registration> _byId = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    private static readonly DefaultDynamicFeatureRenderer s_defaultRenderer = new();

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
    public DynamicSourceOverlayHost(
        IMapHost mapHost,
        IServiceProvider services,
        Action<Action>? marshal = null,
        ILogger<DynamicSourceOverlayHost>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(mapHost);
        ArgumentNullException.ThrowIfNull(services);
        _mapHost = mapHost;
        _services = services;
        _marshal = marshal ?? DispatcherMarshal;
        _logger = logger ?? NullLogger<DynamicSourceOverlayHost>.Instance;
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

        lock (_lock)
        {
            if (_byId.ContainsKey(source.Id))
            {
                throw new InvalidOperationException(
                    $"A dynamic feature source with id '{source.Id}' is already registered.");
            }
            _byId[source.Id] = registration;
        }

        _marshal(() =>
        {
            _mapHost.AddOverlayLayer(layer);
            Rebuild(registration);
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
        registration.Layer.Features = features;
        registration.Layer.DataHasChanged();
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
            regs = _byId.Values.ToArray();
            _byId.Clear();
        }
        foreach (var r in regs) r.DisposeInternal();
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
            _host._marshal(() =>
            {
                if (Volatile.Read(ref _disposed) != 0) return;
                _host.Rebuild(this);
            });
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            Source.Changed -= OnChanged;
            lock (_host._lock) _host._byId.Remove(Source.Id);
            _host._marshal(() => _host._mapHost.RemoveOverlayLayer(Layer));
        }

        internal void DisposeInternal()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            Source.Changed -= OnChanged;
            _host._marshal(() => _host._mapHost.RemoveOverlayLayer(Layer));
        }
    }
}
