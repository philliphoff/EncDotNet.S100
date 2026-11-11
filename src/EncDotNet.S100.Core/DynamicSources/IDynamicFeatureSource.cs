namespace EncDotNet.S100.DynamicSources;

/// <summary>
/// A push-driven publisher of one or more dynamic features. Sources
/// are graphics-agnostic and may be implemented by any consumer of
/// <c>EncDotNet.S100.Core</c> — there is no dependency on Mapsui,
/// Avalonia, or any specific rendering surface.
/// </summary>
/// <remarks>
/// <para>
/// Sources publish a current snapshot via
/// <see cref="CurrentFeatures"/> and a change-event via
/// <see cref="Changed"/>. The viewer-side glue subscribes, captures
/// the snapshot, marshals to the UI thread, and replays the
/// snapshot through a registered <c>IDynamicFeatureRenderer</c>
/// resolved from DI by <see cref="DynamicSourceMetadata.RendererKey"/>.
/// </para>
/// <para>
/// <b>Threading.</b> <see cref="Changed"/> may be raised on any
/// thread. <see cref="CurrentFeatures"/> and <see cref="Metadata"/>
/// must be safe to read from any thread; sources typically implement
/// this via an immutable snapshot swap or a lock.
/// </para>
/// <para>
/// See <c>docs/design/dynamic-feature-source.md</c> for the full
/// design rationale.
/// </para>
/// </remarks>
public interface IDynamicFeatureSource
{
    /// <summary>
    /// Instance-unique identifier — distinguishes this source from
    /// other sources of the same kind in the same host (e.g. two
    /// AIS feeds for different ports). Stability across the
    /// lifetime of the source is a hard contract.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Display metadata and renderer-resolution hints. Immutable
    /// for the lifetime of the source.
    /// </summary>
    DynamicSourceMetadata Metadata { get; }

    /// <summary>
    /// Most recent snapshot of features known to the source. Safe
    /// to read from any thread; the returned collection is treated
    /// as immutable by consumers.
    /// </summary>
    IReadOnlyList<DynamicFeature> CurrentFeatures { get; }

    /// <summary>
    /// Raised when <see cref="CurrentFeatures"/> changes. May be
    /// raised on any thread. The viewer-side overlay host marshals
    /// to the UI thread before mutating Mapsui state.
    /// </summary>
    event EventHandler<DynamicFeaturesChanged>? Changed;
}
