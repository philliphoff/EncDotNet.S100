using EncDotNet.S100.DynamicSources;
using Mapsui;

namespace EncDotNet.S100.Renderers.Mapsui.DynamicSources;

/// <summary>
/// The read/write registry surface over the dynamic-source host: the set of
/// currently registered sources, a per-source visibility toggle, and geographic
/// hit-testing. Implemented by <see cref="S100DynamicSourceHost"/> and exposed
/// on a session via <c>IS100MapSession.DynamicSources</c>.
/// </summary>
/// <remarks>
/// <para>
/// The host's <see cref="S100DynamicSourceHost.Register"/> method remains the
/// only way to add or remove a source; this surface is the canonical read/write
/// facade for view-models and pick services.
/// </para>
/// <para>
/// Visibility state is owned by the host. Toggling visibility flips the backing
/// overlay layer's enabled bit — the source itself is left alone (it keeps
/// publishing into the hidden layer), preserving the graphics-agnostic
/// <see cref="IDynamicFeatureSource"/> contract.
/// </para>
/// <para>
/// Members are safe to call from the host's map thread. The host marshals its
/// internal state mutations, so <see cref="SourcesChanged"/> fires on that
/// thread when raised through the host's normal flow.
/// </para>
/// </remarks>
public interface IS100DynamicSourceRegistry
{
    /// <summary>
    /// Registers a source as a managed overlay layer. The renderer is resolved
    /// from the source's <see cref="DynamicSourceMetadata.RendererKey"/>. The
    /// returned <see cref="IDisposable"/> unregisters the source and detaches its
    /// overlay layer when disposed.
    /// </summary>
    /// <param name="source">The source to host.</param>
    /// <returns>A handle that unregisters the source when disposed.</returns>
    /// <exception cref="InvalidOperationException">
    /// A source with the same <see cref="IDynamicFeatureSource.Id"/> is already
    /// registered.
    /// </exception>
    IDisposable Register(IDynamicFeatureSource source);

    /// <summary>
    /// Currently registered sources in registration order. Stable across
    /// visibility toggles; updated on register / dispose.
    /// </summary>
    IReadOnlyList<DynamicSourceRegistrationInfo> Sources { get; }

    /// <summary>
    /// Returns the current visibility for the source identified by
    /// <paramref name="sourceId"/>. Defaults to <see langword="true"/> for
    /// unregistered ids so seeding a not-yet-registered id is a no-op that still
    /// round-trips its eventual default.
    /// </summary>
    bool GetVisible(string sourceId);

    /// <summary>
    /// Sets the visibility for the source identified by
    /// <paramref name="sourceId"/>. May be called before the source registers
    /// (seeding from persisted settings). Idempotent; fires
    /// <see cref="SourcesChanged"/> only on a real transition.
    /// </summary>
    void SetVisible(string sourceId, bool visible);

    /// <summary>
    /// Snapshot of currently registered, currently visible source instances, in
    /// registration order. Hidden sources are excluded. Returns source instances
    /// (rather than the <see cref="DynamicSourceRegistrationInfo"/> projection)
    /// so a pick path can read <see cref="IDynamicFeatureSource.CurrentFeatures"/>
    /// directly.
    /// </summary>
    IReadOnlyList<IDynamicFeatureSource> GetVisibleSourceInstances();

    /// <summary>
    /// Hit-tests every currently-visible source at a geographic click point and
    /// returns the matches ordered by ascending distance (a click inside a
    /// rendered vessel hull reports distance <c>0</c>). Hidden sources are
    /// excluded, so a click on a hidden target never appears in the result.
    /// </summary>
    /// <param name="mapPoint">Click position in Spherical Mercator world units.</param>
    /// <param name="resolution">Map units per device pixel at the current zoom.</param>
    /// <returns>Hits ordered by ascending distance from the click (possibly empty).</returns>
    IReadOnlyList<DynamicSourceHit> HitTest(MPoint mapPoint, double resolution);

    /// <summary>
    /// Raised when <see cref="Sources"/> changes (register / dispose) or when
    /// <see cref="SetVisible"/> transitions a source's visibility. Raised on the
    /// host's map thread through the host's marshal.
    /// </summary>
    event Action? SourcesChanged;
}
