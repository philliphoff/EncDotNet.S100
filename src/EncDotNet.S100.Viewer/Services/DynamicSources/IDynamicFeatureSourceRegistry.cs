using System;
using System.Collections.Generic;

namespace EncDotNet.S100.Viewer.Services.DynamicSources;

/// <summary>
/// Viewer-internal registry surface over the dynamic-source overlay
/// host (PR-D2.1). Exposes the set of currently registered sources
/// and a per-source visibility toggle so the Layer Stack panel can
/// render a row per source with the same UX as dataset entries.
/// </summary>
/// <remarks>
/// <para>
/// Implemented by <see cref="DynamicSourceOverlayHost"/>. The registry
/// is the canonical read/write surface for view-models; the host's
/// <c>Register</c> method remains the only way to add or remove a
/// source.
/// </para>
/// <para>
/// Visibility state is owned by the host. Toggling visibility flips
/// the backing <c>MemoryLayer.Enabled</c> bit — the source itself is
/// left alone (it keeps publishing into the hidden layer). This
/// preserves the PR-D1 <c>IDynamicFeatureSource</c> contract (no
/// <c>IsEnabled</c>) and avoids per-source toggle plumbing.
/// </para>
/// <para>
/// All members are safe to call from the UI thread. The host marshals
/// internal state mutations so <see cref="SourcesChanged"/> always
/// fires on the UI thread when raised through the host's normal flow.
/// </para>
/// </remarks>
internal interface IDynamicFeatureSourceRegistry
{
    /// <summary>
    /// Currently registered sources in registration order. Stable
    /// across visibility toggles; updated on Register / Dispose.
    /// </summary>
    IReadOnlyList<DynamicSourceRegistrationInfo> Sources { get; }

    /// <summary>
    /// Returns the current visibility for the source identified by
    /// <paramref name="sourceId"/>. Defaults to <see langword="true"/>
    /// for unregistered ids so seeding a not-yet-registered id is a
    /// no-op that still round-trips its eventual default.
    /// </summary>
    bool GetVisible(string sourceId);

    /// <summary>
    /// Sets the visibility for the source identified by
    /// <paramref name="sourceId"/>. May be called before the source
    /// registers (seeding from persisted settings). Idempotent; fires
    /// <see cref="SourcesChanged"/> only on a real transition.
    /// </summary>
    void SetVisible(string sourceId, bool visible);

    /// <summary>
    /// Raised when <see cref="Sources"/> changes (register / dispose)
    /// or when <see cref="SetVisible"/> transitions a source's
    /// visibility. Always raised on the UI thread.
    /// </summary>
    event Action? SourcesChanged;
}
