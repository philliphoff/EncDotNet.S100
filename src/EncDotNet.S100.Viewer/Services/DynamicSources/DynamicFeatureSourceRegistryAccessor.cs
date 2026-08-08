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
    private IS100DynamicSourceRegistry? _current;

    /// <summary>
    /// The attached registry, or <see langword="null"/> when no host
    /// has been constructed yet. Assignment subscribes / unsubscribes
    /// <see cref="SourcesChanged"/> passthrough and fires the event
    /// once so existing subscribers rebuild against the freshly
    /// attached registry.
    /// </summary>
    public IS100DynamicSourceRegistry? Current
    {
        get => _current;
        set
        {
            if (ReferenceEquals(_current, value)) return;

            if (_current is not null) _current.SourcesChanged -= RaiseSourcesChanged;
            _current = value;
            if (_current is not null) _current.SourcesChanged += RaiseSourcesChanged;

            RaiseSourcesChanged();
        }
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

    public void SetVisible(string sourceId, bool visible) =>
        _current?.SetVisible(sourceId, visible);

    public event Action? SourcesChanged;

    private void RaiseSourcesChanged() => SourcesChanged?.Invoke();
}
