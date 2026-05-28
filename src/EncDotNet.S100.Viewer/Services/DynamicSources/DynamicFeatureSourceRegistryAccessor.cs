using System;
using System.Collections.Generic;

namespace EncDotNet.S100.Viewer.Services.DynamicSources;

/// <summary>
/// Late-bound accessor for the dynamic-source overlay host's
/// <see cref="IDynamicFeatureSourceRegistry"/> surface (PR-D2.1).
/// </summary>
/// <remarks>
/// <para>
/// The overlay host is constructed by <c>MainWindow</c> after the
/// Avalonia <c>MapControl</c> exists, which happens after DI is built
/// and after singletons like <c>LayerStackViewModel</c> have already
/// been resolved. This accessor bridges that ordering: services that
/// need the registry hold the accessor and read
/// <see cref="Current"/> (or subscribe to <see cref="SourcesChanged"/>)
/// at invocation time. Mirrors the
/// <c>IMapHostAccessor</c> / <c>MapHostAccessor</c> pattern already
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
internal sealed class DynamicFeatureSourceRegistryAccessor : IDynamicFeatureSourceRegistry
{
    private IDynamicFeatureSourceRegistry? _current;

    /// <summary>
    /// The attached registry, or <see langword="null"/> when no host
    /// has been constructed yet. Assignment subscribes / unsubscribes
    /// <see cref="SourcesChanged"/> passthrough and fires the event
    /// once so existing subscribers rebuild against the freshly
    /// attached registry.
    /// </summary>
    public IDynamicFeatureSourceRegistry? Current
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

    public IReadOnlyList<DynamicSourceRegistrationInfo> Sources =>
        _current?.Sources ?? Array.Empty<DynamicSourceRegistrationInfo>();

    public bool GetVisible(string sourceId) => _current?.GetVisible(sourceId) ?? true;

    public void SetVisible(string sourceId, bool visible) =>
        _current?.SetVisible(sourceId, visible);

    public event Action? SourcesChanged;

    private void RaiseSourcesChanged() => SourcesChanged?.Invoke();
}
