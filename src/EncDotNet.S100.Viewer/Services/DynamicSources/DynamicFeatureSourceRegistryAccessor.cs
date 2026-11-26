using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.Renderers.Mapsui.DynamicSources;
using Mapsui;

namespace EncDotNet.S100.Viewer.Services.DynamicSources;

/// <summary>
/// Late-bound accessor for the dynamic-source host's
/// <see cref="IS100DynamicSourceRegistry"/> surface (PR-D2.1).
/// </summary>
/// <remarks>
/// <para>
/// The dynamic-source host is constructed by <c>MainWindow</c> after the
/// Avalonia <c>MapControl</c> exists, which happens after DI is built
/// and after singletons like <c>LayerStackViewModel</c> have already
/// been resolved. This accessor bridges that ordering: services that
/// need the registry hold the accessor and read
/// <see cref="Current"/> (or subscribe to <see cref="SourcesChanged"/>)
/// at invocation time. Mirrors the
/// typed map-capability accessor pattern already
/// established by the MCP wiring.
/// </para>
/// <para>
/// <see cref="SourcesChanged"/> on the accessor fires when (a) the
/// inner registry's event fires, or (b) <see cref="Current"/> is
/// assigned (the "registry just attached" transition forces a
/// rebuild). Subscribers therefore don't need to know whether the
/// inner registry has attached yet.
/// </para>
/// </remarks>
internal sealed class DynamicFeatureSourceRegistryAccessor : IS100DynamicSourceRegistry
{
    private readonly List<IS100DynamicSourceRegistry> _attached = [];
    private IS100DynamicSourceRegistry? _current;

    /// <summary>
    /// The registry reads go to: the first attached one, or
    /// <see langword="null"/> when no host has been constructed yet.
    /// A change fires <see cref="SourcesChanged"/> once so existing
    /// subscribers rebuild against it.
    /// </summary>
    public IS100DynamicSourceRegistry? Current
    {
        get => _current;
        private set
        {
            if (ReferenceEquals(_current, value)) return;

            if (_current is not null) _current.SourcesChanged -= RaiseSourcesChanged;
            _current = value;
            if (_current is not null) _current.SourcesChanged += RaiseSourcesChanged;

            RaiseSourcesChanged();
        }
    }

    /// <summary>
    /// Attaches a map's dynamic-source host (split map, #1732). Every map
    /// draws the same sources, so <see cref="SetVisible"/> goes to every
    /// attached host; reads go to <see cref="Current"/>, the first one.
    /// </summary>
    /// <param name="registry">The host to attach.</param>
    public void Attach(IS100DynamicSourceRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        if (_attached.Contains(registry))
            return;
        if (_current is { } current)
        {
            // A late host starts with the visibility the others have.
            foreach (var info in current.Sources)
                registry.SetVisible(info.Id, current.GetVisible(info.Id));
        }
        _attached.Add(registry);
        Current ??= registry;
    }

    /// <summary>Detaches a host; reads move to the next attached one.</summary>
    /// <param name="registry">The host to detach.</param>
    public void Detach(IS100DynamicSourceRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        if (!_attached.Remove(registry))
            return;
        if (ReferenceEquals(_current, registry))
            Current = _attached.Count > 0 ? _attached[0] : null;
    }

    public IDisposable Register(IDynamicFeatureSource source) =>
        _current is { } registry
            ? registry.Register(source)
            : throw new InvalidOperationException(
                "No dynamic-source host is attached yet; register directly on the host instead.");

    public IReadOnlyList<DynamicSourceRegistrationInfo> Sources =>
        _current?.Sources ?? Array.Empty<DynamicSourceRegistrationInfo>();

    public bool GetVisible(string sourceId) => _current?.GetVisible(sourceId) ?? true;

    public IReadOnlyList<IDynamicFeatureSource> GetVisibleSourceInstances() =>
        _current?.GetVisibleSourceInstances() ?? Array.Empty<IDynamicFeatureSource>();

    public IReadOnlyList<DynamicSourceHit> HitTest(MPoint mapPoint, double resolution) =>
        _current?.HitTest(mapPoint, resolution) ?? Array.Empty<DynamicSourceHit>();

    public void SetVisible(string sourceId, bool visible)
    {
        foreach (var registry in _attached.ToArray())
            registry.SetVisible(sourceId, visible);
    }

    public event Action? SourcesChanged;

    private void RaiseSourcesChanged() => SourcesChanged?.Invoke();
}
